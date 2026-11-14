import * as THREE from "../lib/three/three.min.js";

const DEG = Math.PI / 180;
const SEG = 64;
const ROOT_DEG = 32;
const MAX_LOADS = 16;
const MAX_BUILDS = 12;
const BUILD_MS = 8;
const RETRY_MS = 8000;
const MAX_DATA = 256;
const MAX_MESHES = 500;
const KEEP_FRAMES = 240;
const SPLIT_PX = 2.5;
const POLE_SPLIT_PX = 4;
const NORMAL_SCALE = [0, 0.35, 0.8, 1.3, 1.8, 2.2];

function toScene(latDeg, lonDeg, r, out, o) {
    const lat = latDeg * DEG, lon = lonDeg * DEG;
    const c = Math.cos(lat);
    out[o] = r * c * Math.cos(lon);
    out[o + 1] = r * Math.sin(lat);
    out[o + 2] = -r * c * Math.sin(lon);
}

async function readTile(url) {
    const response = await fetch(url);
    if (!response.ok) throw new Error(url + " " + response.status);
    let bytes = new Uint8Array(await response.arrayBuffer());
    if (bytes.length > 2 && bytes[0] === 0x1f && bytes[1] === 0x8b) {
        const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("gzip"));
        bytes = new Uint8Array(await new Response(stream).arrayBuffer());
    }
    return new Int16Array(bytes.slice().buffer);
}

function integrate(d, n) {
    const out = new Int16Array(n * n);
    for (let i = 0; i < n; i++) {
        for (let j = 0; j < n; j++) {
            const k = i * n + j;
            let v = d[k];
            if (i > 0) v += out[k - n];
            if (j > 0) v += out[k - 1];
            if (i > 0 && j > 0) v -= out[k - n - 1];
            out[k] = v;
        }
    }
    return out;
}

export class Relief {
    constructor(options) {
        this.base = options.base;
        this.radius = options.radius;
        this.scene = options.scene;
        this.source = options.source;
        this.onChange = options.onChange || (() => {});
        this.poleHalf = { value: 0 };
        this.group = new THREE.Group();
        this.group.visible = false;
        this.scene.add(this.group);
        this.data = new Map();
        this.loading = new Set();
        this.failed = new Map();
        this.pending = new Set();
        this.meshes = new Set();
        this.frame = 0;
        this.frustum = new THREE.Frustum();
        this.matrix = new THREE.Matrix4();
        this.sphere = new THREE.Sphere();
        this.materials = [];
        this.map = null;
    }

    async init() {
        const response = await fetch(this.base + "relief.json");
        if (!response.ok) throw new Error("relief.json " + response.status);
        this.meta = await response.json();
        this.n = this.meta.samples + 2 * this.meta.border;
        this.roots = [];
        for (let row = 0; row * ROOT_DEG < 180; row++) {
            for (let col = 0; col * ROOT_DEG < 360; col++) this.roots.push(this.node(5, row, col));
        }
    }

    node(k, row, col) {
        const s = 2 ** k;
        const latTop = 90 - row * s, latBot = Math.max(-90, 90 - (row + 1) * s);
        const lonL = col * s, lonR = Math.min(360, (col + 1) * s);
        const d = Math.min(3, k);
        const t = this.meta.levels[d].tileDeg;
        const node = { k, s, row, col, latTop, latBot, lonL, lonR, d, r: Math.floor((90 - latTop) / t + 1e-9), c: Math.floor(lonL / t + 1e-9), mesh: null, kids: null, touched: 0, used: 0 };
        node.key = `${d}/${node.r}/${node.c}`;
        const p = new Float32Array(3);
        const midLat = (latTop + latBot) / 2, midLon = (lonL + lonR) / 2;
        toScene(midLat, midLon, this.radius, p, 0);
        node.center = new THREE.Vector3(p[0], p[1], p[2]);
        node.dir = node.center.clone().normalize();
        let radius = 0;
        for (const la of [latTop, midLat, latBot]) {
            for (const lo of [lonL, midLon, lonR]) {
                toScene(la, lo, this.radius, p, 0);
                radius = Math.max(radius, node.center.distanceTo(new THREE.Vector3(p[0], p[1], p[2])));
            }
        }
        node.bound = radius + 12;
        node.spacing = s / SEG * DEG * this.radius;
        return node;
    }

    children(node) {
        if (!node.kids) {
            node.kids = [];
            for (let i = 0; i < 2; i++) {
                for (let j = 0; j < 2; j++) {
                    const row = node.row * 2 + i, col = node.col * 2 + j;
                    const s = 2 ** (node.k - 1);
                    if (90 - row * s <= -90 || col * s >= 360) continue;
                    node.kids.push(this.node(node.k - 1, row, col));
                }
            }
        }
        return node.kids;
    }

    skipped(node) {
        return this.poleHalf.value > 0 && node.latTop < -80.5;
    }

    inView(node) {
        const angle = Math.acos(Math.max(-1, Math.min(1, node.dir.dot(this.camDir))));
        const reach = (node.bound - 12) / this.radius;
        if (this.camDist > this.radius + 12) {
            const horizon = Math.acos((this.radius - 10) / this.camDist) + Math.acos((this.radius - 10) / (this.radius + 11));
            if (angle - reach > horizon) return false;
        }
        this.sphere.set(node.center, node.bound);
        return this.frustum.intersectsSphere(this.sphere);
    }

    pixels(node) {
        const dist = Math.max(this.camPos.distanceTo(node.center) - node.bound, 0.3);
        return node.spacing / dist * this.pxScale;
    }

    request(node) {
        node.touched = this.frame;
        if (node.mesh) node.used = this.frame;
        else this.pending.add(node);
        const entry = this.data.get(node.key);
        if (entry) entry.used = this.frame;
    }

    visit(node) {
        if (this.skipped(node) || !this.inView(node)) return;
        if (node.mesh) node.used = this.frame;
        if (node.k > 0 && this.pixels(node) > SPLIT_PX) {
            const kids = this.children(node);
            const need = kids.filter((kid) => !this.skipped(kid) && this.inView(kid));
            for (const kid of need) {
                this.request(kid);
                if (kid.k > 0 && this.prefetch.length < 64 && this.pixels(kid) > SPLIT_PX) {
                    for (const grand of this.children(kid)) if (!grand.mesh && !this.skipped(grand) && this.inView(grand)) this.prefetch.push(grand);
                }
            }
            if (need.every((kid) => kid.mesh)) {
                for (const kid of kids) this.visit(kid);
                return;
            }
        }
        this.request(node);
        if (node.mesh) {
            node.mesh.visible = true;
            node.used = this.frame;
            this.drawn++;
            this.leaves.push(node);
        }
    }

    stitch() {
        const drawn = new Map();
        for (const node of this.leaves) drawn.set(`${node.k}/${node.row}/${node.col}`, node);
        const coarser = (node, lat, lon) => {
            if (lat >= 90 || lat <= -90) return null;
            lon = ((lon % 360) + 360) % 360;
            for (let k = node.k + 1; k <= 5; k++) {
                const s = 2 ** k;
                const found = drawn.get(`${k}/${Math.floor((90 - lat) / s)}/${Math.floor(lon / s)}`);
                if (found) return found;
            }
            return null;
        };
        for (const node of this.leaves) {
            const midLat = (node.latTop + node.latBot) / 2, midLon = (node.lonL + node.lonR) / 2;
            const eps = 1e-6;
            const n = coarser(node, node.latTop + eps, midLon), sth = coarser(node, node.latBot - eps, midLon);
            const w = coarser(node, midLat, node.lonL - eps), ea = coarser(node, midLat, node.lonR + eps);
            const state = [n, ea, sth, w].map((m) => (m ? `${m.k}/${m.row}/${m.col}` : "")).join("|");
            if (state === node.edgeState) continue;
            node.edgeState = state;
            this.applyEdges(node, n, ea, sth, w);
        }
    }

    applyEdges(node, n, ea, sth, w) {
        const u = node.mesh.userData;
        const pos = node.mesh.geometry.getAttribute("position");
        const a = pos.array;
        const W = u.W, H = u.H;
        const set = (k, r) => {
            const dir = u.dirs, o = k * 3;
            a[o] = dir[o] * r; a[o + 1] = dir[o + 1] * r; a[o + 2] = dir[o + 2] * r;
            const rs = r - u.skirt;
            for (const sk of [u.skirtOf[k], u.skirtOf2[k]]) {
                if (sk >= 0) { a[sk * 3] = dir[o] * rs; a[sk * 3 + 1] = dir[o + 1] * rs; a[sk * 3 + 2] = dir[o + 2] * rs; }
            }
        };
        const edge = (indices, params, other, otherSide) => {
            if (!other || !other.mesh) {
                for (const k of indices) set(k, u.radii[k]);
                return;
            }
            const v = other.mesh.userData;
            const oi = v.edges[otherSide], op = v.params[otherSide];
            for (let m = 0; m < indices.length; m++) {
                const p = params[m];
                let lo = 0, hi = op.length - 1;
                if (op[0] > op[hi]) {
                    while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (op[mid] >= p) lo = mid; else hi = mid; }
                } else {
                    while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (op[mid] <= p) lo = mid; else hi = mid; }
                }
                const span = op[hi] - op[lo];
                const f = Math.abs(span) < 1e-12 ? 0 : Math.max(0, Math.min(1, (p - op[lo]) / span));
                set(indices[m], v.radii[oi[lo]] + (v.radii[oi[hi]] - v.radii[oi[lo]]) * f);
            }
        };
        edge(u.edges[0], u.params[0], n, 2);
        edge(u.edges[1], u.params[1], ea, 3);
        edge(u.edges[2], u.params[2], sth, 0);
        edge(u.edges[3], u.params[3], w, 1);
        pos.needsUpdate = true;
    }

    syncMaterials() {
        const src = this.source();
        if (!src || !src.map) return false;
        if (this.map !== src.map || !this.materials.length) {
            this.map = src.map;
            this.map.wrapS = THREE.RepeatWrapping;
            this.map.needsUpdate = true;
            if (src.normalMap) {
                src.normalMap.wrapS = THREE.RepeatWrapping;
                src.normalMap.needsUpdate = true;
            }
            if (!this.materials.length) {
                for (let k = 0; k <= 5; k++) {
                    const material = new THREE.MeshStandardMaterial({ map: this.map, roughness: 1, metalness: 0, side: THREE.DoubleSide });
                    material.color.copy(src.color);
                    if (src.normalMap && NORMAL_SCALE[k] > 0) {
                        material.normalMap = src.normalMap;
                        material.normalScale.set(NORMAL_SCALE[k], NORMAL_SCALE[k]);
                    }
                    material.onBeforeCompile = (shader) => {
                        shader.uniforms.poleHalf = this.poleHalf;
                        shader.vertexShader = shader.vertexShader
                            .replace("#include <common>", "#include <common>\nvarying vec3 vObj;")
                            .replace("#include <begin_vertex>", "#include <begin_vertex>\nvObj = position;");
                        shader.fragmentShader = shader.fragmentShader
                            .replace("#include <common>", "#include <common>\nvarying vec3 vObj;\nuniform float poleHalf;")
                            .replace("void main() {", `void main() {
    if (poleHalf > 0.0) {
        vec3 u = normalize(vec3(vObj.x, -vObj.z, vObj.y));
        if (u.z < -0.5) {
            float k = 2.0 * ${this.radius.toFixed(1)} / (1.0 - u.z);
            if (abs(k * u.y) < poleHalf && abs(k * u.x) < poleHalf) discard;
        }
    }`);
                    };
                    this.materials.push(material);
                }
            } else {
                for (const material of this.materials) {
                    material.map = this.map;
                    material.needsUpdate = true;
                }
            }
        }
        return true;
    }

    update(camera, heightPx, active) {
        if (!this.meta || !active || !this.syncMaterials()) {
            this.group.visible = false;
            return false;
        }
        this.group.visible = true;
        this.frame++;
        this.camPos = camera.position;
        this.camDist = this.camPos.length();
        this.camDir = this.camPos.clone().normalize();
        this.pxScale = heightPx / (2 * Math.tan(camera.fov * DEG / 2));
        this.matrix.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse);
        this.frustum.setFromProjectionMatrix(this.matrix);
        for (const mesh of this.meshes) mesh.visible = false;
        this.drawn = 0;
        this.leaves = [];
        this.prefetch = [];
        for (const root of this.roots) this.visit(root);
        this.stitch();
        let built = 0, waiting = false;
        const start = performance.now();
        const list = [...this.pending].filter((node) => {
            if (node.touched !== this.frame || node.mesh) { this.pending.delete(node); return false; }
            return true;
        });
        list.sort((a, b) => b.k - a.k || this.camPos.distanceToSquared(a.center) - this.camPos.distanceToSquared(b.center));
        for (const node of list) {
            const entry = this.data.get(node.key);
            if (entry) {
                if (built === 0 || (built < MAX_BUILDS && performance.now() - start < BUILD_MS)) {
                    entry.used = this.frame;
                    this.build(node, entry);
                    this.pending.delete(node);
                    built++;
                } else {
                    waiting = true;
                }
            } else {
                this.fetch(node.d, node.r, node.c);
            }
        }
        this.prefetch.sort((a, b) => this.camPos.distanceToSquared(a.center) - this.camPos.distanceToSquared(b.center));
        for (const node of this.prefetch) {
            if (this.loading.size >= MAX_LOADS) break;
            this.fetch(node.d, node.r, node.c);
        }
        this.evict();
        return waiting || built > 0;
    }

    fetch(d, r, c) {
        const key = `${d}/${r}/${c}`;
        if (this.data.has(key) || this.loading.has(key) || this.loading.size >= MAX_LOADS) return;
        if (this.failed.has(key) && performance.now() - this.failed.get(key) < RETRY_MS) return;
        this.loading.add(key);
        readTile(`${this.base}${d}/r${r}c${c}.bin.gz`).then((raw) => {
            const level = this.meta.levels[d];
            this.data.set(key, { d, r, c, ppd: level.ppd, tileDeg: level.tileDeg, h: integrate(raw, this.n), used: this.frame });
        }).catch((error) => {
            this.failed.set(key, performance.now());
            console.warn("relief tile", error);
        }).finally(() => {
            this.loading.delete(key);
            this.onChange();
        });
    }

    sample(entry, lat, lon) {
        const n = this.n, b = this.meta.border, size = this.meta.samples;
        let fr = (90 - lat) * entry.ppd - 0.5 - entry.r * size + b;
        let fc = lon * entry.ppd - 0.5 - entry.c * size + b;
        fr = Math.max(0, Math.min(n - 1.001, fr));
        fc = Math.max(0, Math.min(n - 1.001, fc));
        const i = Math.floor(fr), j = Math.floor(fc), u = fr - i, v = fc - j;
        const k = i * n + j, h = entry.h;
        const top = h[k] + (h[k + 1] - h[k]) * v;
        const bottom = h[k + n] + (h[k + n + 1] - h[k + n]) * v;
        return (top + (bottom - top) * u) * this.meta.unitM / 1000;
    }

    heightAt(latDeg, lonDeg) {
        const lon = ((lonDeg % 360) + 360) % 360;
        for (let d = 0; d < this.meta?.levels.length; d++) {
            const t = this.meta.levels[d].tileDeg;
            const entry = this.data.get(`${d}/${Math.floor((90 - latDeg) / t)}/${Math.floor(lon / t)}`);
            if (entry) return this.sample(entry, latDeg, lon);
        }
        return null;
    }

    build(node, entry) {
        const step = node.s / SEG;
        const nLat = Math.max(1, Math.round((node.latTop - node.latBot) / step));
        const nLon = Math.max(1, Math.round((node.lonR - node.lonL) / step));
        const dLat = (node.latTop - node.latBot) / nLat, dLon = (node.lonR - node.lonL) / nLon;
        const W = nLon + 1, H = nLat + 1;
        const count = W * H + 2 * (W + H);
        const pos = new Float32Array(count * 3), nor = new Float32Array(count * 3), uv = new Float32Array(count * 2);
        const dirs = new Float32Array(W * H * 3), radii = new Float32Array(W * H);
        const e = 0.5 / entry.ppd;
        const R = this.radius;
        const skirt = Math.max(0.25, node.spacing * 1.5);
        const cosLon = new Float64Array(W), sinLon = new Float64Array(W), lons = new Float64Array(W), lats = new Float64Array(H);
        for (let j = 0; j < W; j++) {
            lons[j] = node.lonL + j * dLon;
            cosLon[j] = Math.cos(lons[j] * DEG);
            sinLon[j] = Math.sin(lons[j] * DEG);
        }
        for (let i = 0; i < H; i++) {
            const lat = node.latTop - i * dLat;
            lats[i] = lat;
            const cl = Math.cos(lat * DEG), sl = Math.sin(lat * DEG);
            const latN = Math.min(90, lat + e), latS = Math.max(-90, lat - e);
            const dy = (latN - latS) * DEG * R;
            const dx = 2 * e * DEG * R * cl;
            for (let j = 0; j < W; j++) {
                const lon = lons[j];
                const k = i * W + j, o = k * 3;
                const r = R + this.sample(entry, lat, lon);
                radii[k] = r;
                const cx = cl * cosLon[j], cy = cl * sinLon[j];
                dirs[o] = cx; dirs[o + 1] = sl; dirs[o + 2] = -cy;
                pos[o] = cx * r; pos[o + 1] = sl * r; pos[o + 2] = -cy * r;
                const hx = cl > 1e-4 ? (this.sample(entry, lat, lon + e) - this.sample(entry, lat, lon - e)) / dx : 0;
                const hy = (this.sample(entry, latN, lon) - this.sample(entry, latS, lon)) / dy;
                let nx = cx + hx * sinLon[j] + hy * sl * cosLon[j];
                let ny = cy - hx * cosLon[j] + hy * sl * sinLon[j];
                let nz = sl - hy * cl;
                const len = Math.hypot(nx, ny, nz) || 1;
                nor[o] = nx / len; nor[o + 1] = nz / len; nor[o + 2] = -ny / len;
                uv[k * 2] = 0.5 + lon / 360;
                uv[k * 2 + 1] = 0.5 + lat / 180;
            }
        }
        const ring = [];
        for (let j = 0; j < W; j++) ring.push(j);
        for (let i = 1; i < H; i++) ring.push(i * W + W - 1);
        for (let j = W - 2; j >= 0; j--) ring.push((H - 1) * W + j);
        for (let i = H - 2; i > 0; i--) ring.push(i * W);
        ring.push(0);
        let o = W * H;
        const skirtIndex = [];
        for (const k of ring) {
            const x = pos[k * 3], y = pos[k * 3 + 1], z = pos[k * 3 + 2];
            const p = Math.hypot(x, y, z), f = (p - skirt) / p;
            pos[o * 3] = x * f; pos[o * 3 + 1] = y * f; pos[o * 3 + 2] = z * f;
            nor[o * 3] = nor[k * 3]; nor[o * 3 + 1] = nor[k * 3 + 1]; nor[o * 3 + 2] = nor[k * 3 + 2];
            uv[o * 2] = uv[k * 2]; uv[o * 2 + 1] = uv[k * 2 + 1];
            skirtIndex.push(o++);
        }
        const size = (W - 1) * (H - 1) * 6 + (ring.length - 1) * 6;
        const index = o < 65536 ? new Uint16Array(size) : new Uint32Array(size);
        let t = 0;
        for (let i = 0; i < H - 1; i++) {
            for (let j = 0; j < W - 1; j++) {
                const p = i * W + j, q = p + 1, r = p + W, s = r + 1;
                index[t++] = p; index[t++] = r; index[t++] = q;
                index[t++] = q; index[t++] = r; index[t++] = s;
            }
        }
        for (let m = 0; m < ring.length - 1; m++) {
            const p = ring[m], q = ring[m + 1], r = skirtIndex[m], s = skirtIndex[m + 1];
            index[t++] = p; index[t++] = r; index[t++] = q;
            index[t++] = q; index[t++] = r; index[t++] = s;
        }
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute("position", new THREE.BufferAttribute(pos.subarray(0, o * 3), 3));
        geometry.setAttribute("normal", new THREE.BufferAttribute(nor.subarray(0, o * 3), 3));
        geometry.setAttribute("uv", new THREE.BufferAttribute(uv.subarray(0, o * 2), 2));
        geometry.setIndex(new THREE.BufferAttribute(index.subarray(0, t), 1));
        geometry.boundingSphere = new THREE.Sphere(node.center.clone(), node.bound);
        const mesh = new THREE.Mesh(geometry, this.materials[node.k]);
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        mesh.matrixAutoUpdate = false;
        mesh.visible = false;
        const skirtOf = new Int32Array(W * H).fill(-1), skirtOf2 = new Int32Array(W * H).fill(-1);
        for (let m = 0; m < ring.length; m++) {
            if (skirtOf[ring[m]] < 0) skirtOf[ring[m]] = skirtIndex[m]; else skirtOf2[ring[m]] = skirtIndex[m];
        }
        const north = [], south = [], east = [], west = [];
        for (let j = 0; j < W; j++) { north.push(j); south.push((H - 1) * W + j); }
        for (let i = 0; i < H; i++) { east.push(i * W + W - 1); west.push(i * W); }
        mesh.userData = { node, W, H, dirs, radii, skirt, skirtOf, skirtOf2, edges: [north, east, south, west], params: [lons, lats, lons, lats] };
        node.edgeState = "";
        node.mesh = mesh;
        node.used = this.frame;
        this.meshes.add(mesh);
        this.group.add(mesh);
    }

    evict() {
        if (this.meshes.size > MAX_MESHES) {
            const old = [...this.meshes].filter((m) => !m.visible && m.userData.node.used < this.frame - KEEP_FRAMES).sort((a, b) => a.userData.node.used - b.userData.node.used);
            for (const mesh of old.slice(0, this.meshes.size - MAX_MESHES)) {
                const node = mesh.userData.node;
                this.group.remove(mesh);
                mesh.geometry.dispose();
                this.meshes.delete(mesh);
                node.mesh = null;
            }
        }
        if (this.data.size > MAX_DATA) {
            const old = [...this.data.entries()].filter((e) => e[1].used < this.frame - KEEP_FRAMES).sort((a, b) => a[1].used - b[1].used);
            for (const [key] of old.slice(0, this.data.size - MAX_DATA)) this.data.delete(key);
        }
    }

    stats() {
        return { drawn: this.drawn, meshes: this.meshes.size, data: this.data.size, loading: this.loading.size, pending: this.pending.size };
    }

    dispose() {
        for (const mesh of this.meshes) mesh.geometry.dispose();
        for (const material of this.materials) material.dispose();
        this.scene.remove(this.group);
        this.meshes.clear();
        this.data.clear();
    }
}

const POLE_SUB = 4;
const POLE_SEG = 128;
const POLE_BASE_M = 800;

export class PoleDetail {
    constructor(options) {
        this.base = options.base;
        this.radius = options.radius;
        this.scene = options.scene;
        this.material = options.material;
        this.depthMaterial = options.depthMaterial || null;
        this.mask = options.mask;
        this.onChange = options.onChange || (() => {});
        this.group = new THREE.Group();
        this.group.visible = false;
        this.scene.add(this.group);
        this.data = new Map();
        this.loading = new Set();
        this.failed = new Map();
        this.meshes = new Set();
        this.frame = 0;
        this.frustum = new THREE.Frustum();
        this.matrix = new THREE.Matrix4();
        this.sphere = new THREE.Sphere();
    }

    async init() {
        const response = await fetch(this.base + "pole.json");
        if (!response.ok) throw new Error("pole.json " + response.status);
        const meta = await response.json();
        this.half = meta.halfExtentM;
        this.spacing = meta.spacingM;
        this.tile = meta.tilePx;
        this.unit = meta.unitM;
        this.count = meta.tiles;
        this.tileM = this.tile * this.spacing;
        this.tiles = meta.present.map(([r, c]) => this.patch(r, c, -1));
        this.maskBytes = this.mask.image.data;
    }

    patch(r, c, sub) {
        const span = sub < 0 ? this.tileM : this.tileM / POLE_SUB;
        const x0 = -this.half + c * this.tileM + (sub < 0 ? 0 : (sub % POLE_SUB) * span);
        const y1 = this.half - r * this.tileM - (sub < 0 ? 0 : Math.floor(sub / POLE_SUB) * span);
        const w = Math.min(x0 + span, this.half) - x0, hgt = y1 - Math.max(y1 - span, -this.half);
        if (w <= 0 || hgt <= 0) return null;
        const p = this.toScene(x0 + w / 2, y1 - hgt / 2, 0);
        const center = new THREE.Vector3(p[0], p[1], p[2]);
        return { r, c, sub, x0, y1, span, w, hgt, center, dir: center.clone().normalize(), bound: span / 1000 * 0.75 + 8, spacing: span / POLE_SEG / 1000, mesh: null, kids: null, used: 0 };
    }

    toScene(x, y, h) {
        const rho = Math.hypot(x, y) / 1000;
        const lat = -Math.PI / 2 + 2 * Math.atan(rho / (2 * this.radius));
        const lon = Math.atan2(x, y);
        const r = this.radius + h;
        const c = Math.cos(lat);
        return [r * c * Math.cos(lon), r * Math.sin(lat), -r * c * Math.sin(lon)];
    }

    inView(node) {
        this.sphere.set(node.center, node.bound);
        if (!this.frustum.intersectsSphere(this.sphere)) return false;
        if (this.camDist > this.radius + 12) {
            const angle = Math.acos(Math.max(-1, Math.min(1, node.dir.dot(this.camDir))));
            const horizon = Math.acos((this.radius - 10) / this.camDist) + 0.12;
            if (angle - node.bound / this.radius > horizon) return false;
        }
        return true;
    }

    pixels(node, spacingKm) {
        const dist = Math.max(this.camPos.distanceTo(node.center) - node.bound, 0.2);
        return spacingKm / dist * this.pxScale;
    }

    show(node) {
        node.mesh.visible = true;
        node.used = this.frame;
        this.drawn++;
    }

    update(camera, heightPx, active) {
        if (!this.tiles || !active) {
            this.group.visible = false;
            if (this.maskOn) this.setMask(new Set());
            return false;
        }
        this.group.visible = true;
        this.frame++;
        this.camPos = camera.position;
        this.camDist = this.camPos.length();
        this.camDir = this.camPos.clone().normalize();
        this.pxScale = heightPx / (2 * Math.tan(camera.fov * DEG / 2));
        this.matrix.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse);
        this.frustum.setFromProjectionMatrix(this.matrix);
        for (const mesh of this.meshes) mesh.visible = false;
        this.drawn = 0;
        const covered = new Set();
        let built = 0, waiting = false;
        const start = performance.now();
        const order = this.tiles.filter((t) => this.inView(t) && this.pixels(t, POLE_BASE_M / 1000) > POLE_SPLIT_PX)
            .sort((a, b) => this.camPos.distanceToSquared(a.center) - this.camPos.distanceToSquared(b.center));
        for (const tile of order) {
            const key = `${tile.r}/${tile.c}`;
            const entry = this.data.get(key);
            if (!entry) {
                this.fetch(tile.r, tile.c);
                continue;
            }
            entry.used = this.frame;
            if (this.pixels(tile, tile.spacing) > POLE_SPLIT_PX) {
                if (!tile.kids) tile.kids = Array.from({ length: POLE_SUB * POLE_SUB }, (_, i) => this.patch(tile.r, tile.c, i)).filter((kid) => kid);
                const need = tile.kids.filter((kid) => this.inView(kid));
                for (const kid of tile.kids) if (kid.mesh) kid.used = this.frame;
                for (const kid of need) {
                    if (!kid.mesh) {
                        if (built === 0 || (built < MAX_BUILDS && performance.now() - start < BUILD_MS)) { this.build(kid, entry); built++; } else waiting = true;
                    }
                }
                if (need.every((kid) => kid.mesh)) {
                    for (const kid of need) this.show(kid);
                    covered.add(key);
                    continue;
                }
            }
            if (tile.mesh) tile.used = this.frame;
            if (!tile.mesh) {
                if (built === 0 || (built < MAX_BUILDS && performance.now() - start < BUILD_MS)) { this.build(tile, entry); built++; } else { waiting = true; continue; }
            }
            this.show(tile);
            covered.add(key);
        }
        this.setMask(covered);
        this.evict();
        return waiting || built > 0;
    }

    setMask(covered) {
        const bytes = this.maskBytes;
        let changed = false;
        for (let r = 0; r < 16; r++) {
            for (let c = 0; c < 16; c++) {
                const v = covered.has(`${r}/${c}`) ? 255 : 0;
                const k = r * 16 + c;
                if (bytes[k] !== v) { bytes[k] = v; changed = true; }
            }
        }
        this.maskOn = covered.size > 0;
        if (changed) this.mask.needsUpdate = true;
    }

    fetch(r, c) {
        const key = `${r}/${c}`;
        if (this.data.has(key) || this.loading.has(key) || this.loading.size >= MAX_LOADS) return;
        if (this.failed.has(key) && performance.now() - this.failed.get(key) < RETRY_MS) return;
        this.loading.add(key);
        readTile(`${this.base}80/r${r}c${c}.bin.gz`).then((raw) => {
            this.data.set(key, { h: integrate(raw, this.tile), used: this.frame });
        }).catch((error) => {
            this.failed.set(key, performance.now());
            console.warn("pole tile", error);
        }).finally(() => {
            this.loading.delete(key);
            this.onChange();
        });
    }

    height(entry, node, x, y) {
        const n = this.tile;
        const tx = -this.half + node.c * this.tileM, ty = this.half - node.r * this.tileM;
        const fc = Math.max(0, Math.min(n - 1.001, (x - tx) / this.spacing - 0.5));
        const fr = Math.max(0, Math.min(n - 1.001, (ty - y) / this.spacing - 0.5));
        const i = Math.floor(fr), j = Math.floor(fc), u = fr - i, v = fc - j;
        const k = i * n + j, h = entry.h;
        const top = h[k] + (h[k + 1] - h[k]) * v;
        const bottom = h[k + n] + (h[k + n + 1] - h[k + n]) * v;
        return (top + (bottom - top) * u) * this.unit / 1000;
    }

    edgeHeight(entry, node, x, y, alongX) {
        const g = this.tileM / POLE_SEG;
        const t = ((alongX ? x : -y) + this.half) / g;
        const k = Math.floor(t + 1e-6), f = Math.max(0, t - k);
        const a = -this.half + k * g;
        if (f < 1e-4) return alongX ? this.height(entry, node, a, y) : this.height(entry, node, x, -a);
        const h0 = alongX ? this.height(entry, node, a, y) : this.height(entry, node, x, -a);
        const h1 = alongX ? this.height(entry, node, a + g, y) : this.height(entry, node, x, -(a + g));
        return h0 + (h1 - h0) * f;
    }

    build(node, entry) {
        const step = node.span / POLE_SEG;
        const nx = Math.max(1, Math.round(node.w / step)), ny = Math.max(1, Math.round(node.hgt / step));
        const W = nx + 1, H = ny + 1;
        const dx = node.w / nx, dy = node.hgt / ny;
        const count = W * H + 2 * (W + H);
        const pos = new Float32Array(count * 3), uv = new Float32Array(count * 2), nor = new Float32Array(count * 3);
        const e = this.spacing;
        for (let i = 0; i < H; i++) {
            const y = node.y1 - i * dy;
            for (let j = 0; j < W; j++) {
                const x = node.x0 + j * dx;
                const k = i * W + j;
                const h = i === 0 || i === H - 1 ? this.edgeHeight(entry, node, x, y, true) : j === 0 || j === W - 1 ? this.edgeHeight(entry, node, x, y, false) : this.height(entry, node, x, y);
                const p = this.toScene(x, y, h);
                pos[k * 3] = p[0]; pos[k * 3 + 1] = p[1]; pos[k * 3 + 2] = p[2];
                const hx = (this.height(entry, node, x + e, y) - this.height(entry, node, x - e, y)) / (2 * e / 1000);
                const hy = (this.height(entry, node, x, y + e) - this.height(entry, node, x, y - e)) / (2 * e / 1000);
                const pl = Math.hypot(p[0], p[1], p[2]);
                const ux = p[0] / pl, uy = p[1] / pl, uz = p[2] / pl;
                let ax = 0 + uz * ux, ay = uz * uy, az = -1 + uz * uz;
                const al = Math.hypot(ax, ay, az) || 1;
                ax /= al; ay /= al; az /= al;
                let bx = 1 - ux * ux, by = -ux * uy, bz = -ux * uz;
                const bl = Math.hypot(bx, by, bz) || 1;
                bx /= bl; by /= bl; bz /= bl;
                const nx = ux - hx * ax - hy * bx, ny = uy - hx * ay - hy * by, nz = uz - hx * az - hy * bz;
                const len = Math.hypot(nx, ny, nz) || 1;
                nor[k * 3] = nx / len; nor[k * 3 + 1] = ny / len; nor[k * 3 + 2] = nz / len;
                uv[k * 2] = (x + this.half) / (2 * this.half);
                uv[k * 2 + 1] = (y + this.half) / (2 * this.half);
            }
        }
        const ring = [];
        for (let j = 0; j < W; j++) ring.push(j);
        for (let i = 1; i < H; i++) ring.push(i * W + W - 1);
        for (let j = W - 2; j >= 0; j--) ring.push((H - 1) * W + j);
        for (let i = H - 2; i > 0; i--) ring.push(i * W);
        ring.push(0);
        let o = W * H;
        const skirt = Math.max(0.15, node.spacing * 2);
        const skirtIndex = [];
        for (const k of ring) {
            const x = pos[k * 3], y = pos[k * 3 + 1], z = pos[k * 3 + 2];
            const p = Math.hypot(x, y, z), f = (p - skirt) / p;
            pos[o * 3] = x * f; pos[o * 3 + 1] = y * f; pos[o * 3 + 2] = z * f;
            uv[o * 2] = uv[k * 2]; uv[o * 2 + 1] = uv[k * 2 + 1];
            nor[o * 3] = nor[k * 3]; nor[o * 3 + 1] = nor[k * 3 + 1]; nor[o * 3 + 2] = nor[k * 3 + 2];
            skirtIndex.push(o++);
        }
        const size = (W - 1) * (H - 1) * 6 + (ring.length - 1) * 6;
        const index = o < 65536 ? new Uint16Array(size) : new Uint32Array(size);
        let t = 0;
        for (let i = 0; i < H - 1; i++) {
            for (let j = 0; j < W - 1; j++) {
                const a = i * W + j, b = a + 1, c = a + W, d = c + 1;
                index[t++] = a; index[t++] = c; index[t++] = b;
                index[t++] = b; index[t++] = c; index[t++] = d;
            }
        }
        for (let m = 0; m < ring.length - 1; m++) {
            const a = ring[m], b = ring[m + 1], c = skirtIndex[m], d = skirtIndex[m + 1];
            index[t++] = a; index[t++] = c; index[t++] = b;
            index[t++] = b; index[t++] = c; index[t++] = d;
        }
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute("position", new THREE.BufferAttribute(pos.subarray(0, o * 3), 3));
        geometry.setAttribute("uv", new THREE.BufferAttribute(uv.subarray(0, o * 2), 2));
        geometry.setAttribute("normal", new THREE.BufferAttribute(nor.subarray(0, o * 3), 3));
        geometry.setIndex(new THREE.BufferAttribute(index.subarray(0, t), 1));
        geometry.boundingSphere = new THREE.Sphere(node.center.clone(), node.bound);
        const mesh = new THREE.Mesh(geometry, this.material);
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        if (this.depthMaterial) mesh.customDepthMaterial = this.depthMaterial;
        mesh.matrixAutoUpdate = false;
        mesh.visible = false;
        mesh.userData.node = node;
        node.mesh = mesh;
        node.used = this.frame;
        this.meshes.add(mesh);
        this.group.add(mesh);
    }

    evict() {
        if (this.meshes.size > 160) {
            const old = [...this.meshes].filter((m) => !m.visible && m.userData.node.used < this.frame - KEEP_FRAMES).sort((a, b) => a.userData.node.used - b.userData.node.used);
            for (const mesh of old.slice(0, this.meshes.size - 160)) {
                this.group.remove(mesh);
                mesh.geometry.dispose();
                this.meshes.delete(mesh);
                mesh.userData.node.mesh = null;
            }
        }
        if (this.data.size > 24) {
            const old = [...this.data.entries()].filter((e) => e[1].used < this.frame - KEEP_FRAMES).sort((a, b) => a[1].used - b[1].used);
            for (const [key] of old.slice(0, this.data.size - 24)) this.data.delete(key);
        }
    }

    dispose() {
        for (const mesh of this.meshes) mesh.geometry.dispose();
        this.scene.remove(this.group);
        this.meshes.clear();
        this.data.clear();
    }
}
