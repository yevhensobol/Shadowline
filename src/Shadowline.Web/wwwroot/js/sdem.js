import * as THREE from "../lib/three/three.min.js";

const DEG = Math.PI / 180;
const KEEP = 40;
const MAX_LOADS = 6;
const RETRY_MS = 8000;
const SKIRT_KM = 0.12;
const LEVELS = [[5, 2.5], [20, 10], [80, 40]];
const PHOTO_FAR = Infinity;
const HOLD = 1.25;

async function readHeights(url) {
    const response = await fetch(url);
    if (!response.ok) throw new Error(url + " " + response.status);
    let bytes = new Uint8Array(await response.arrayBuffer());
    if (bytes.length > 2 && bytes[0] === 0x1f && bytes[1] === 0x8b) {
        const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("gzip"));
        bytes = new Uint8Array(await new Response(stream).arrayBuffer());
    }
    return new Int16Array(bytes.slice().buffer);
}

function gap(t, x, y) {
    return Math.hypot(Math.max(t.x0 - x, 0, x - t.x1), Math.max(t.y0 - y, 0, y - t.y1));
}

function overlaps(a, b) {
    return Math.min(a.x1, b.x1) - Math.max(a.x0, b.x0) > 1 && Math.min(a.y1, b.y1) - Math.max(a.y0, b.y0) > 1;
}

export class Sdem {
    constructor(options) {
        this.base = options.base;
        this.radius = options.radius;
        this.scene = options.scene;
        this.poleHalf = options.poleHalf;
        this.count = options.count;
        this.rects = options.rects;
        this.limit = options.rects.length;
        this.onChange = options.onChange || (() => {});
        this.loader = new THREE.TextureLoader();
        this.anisotropy = options.anisotropy || 1;
        this.albedo = options.albedo || null;
        this.group = new THREE.Group();
        this.scene.add(this.group);
        this.entries = new Map();
        this.loading = new Set();
        this.failed = new Map();
        this.shown = [];
        this.mode = "relief";
        this.level = 0;
        this.frame = 0;
        this.frustum = new THREE.Frustum();
        this.matrix = new THREE.Matrix4();
        this.sphere = new THREE.Sphere();
    }

    async init() {
        const response = await fetch(this.base + "index.json");
        if (!response.ok) throw new Error("sdem index " + response.status);
        const index = await response.json();
        this.unit = index.unitM / 1000;
        this.regions = index.regions.map((region) => {
            const [x0, y0, x1, y1] = region.bounds;
            const tiles = region.tiles.map(([id, level, tx0, ty0, tx1, ty1, cols, rows]) => {
                const center = this.toScene((tx0 + tx1) / 2, (ty0 + ty1) / 2, 0);
                return { key: region.id + "/" + id, region: region.id, id, level, x0: tx0, y0: ty0, x1: tx1, y1: ty1, cols, rows, center: new THREE.Vector3(...center), bound: Math.hypot(tx1 - tx0, ty1 - ty0) / 2000 + 6 };
            });
            return { id: region.id, name: region.name, x0, y0, x1, y1, tiles };
        });
    }

    toScene(x, y, h) {
        const rho = Math.hypot(x, y) / 1000;
        const lat = -Math.PI / 2 + 2 * Math.atan(rho / (2 * this.radius));
        const lon = Math.atan2(x, y);
        const r = this.radius + h;
        const c = Math.cos(lat);
        return [r * c * Math.cos(lon), r * Math.sin(lat), -r * c * Math.sin(lon)];
    }

    polar(latDeg, lonDeg) {
        const rho = 2 * this.radius * 1000 * Math.tan((90 + latDeg) * DEG / 2);
        return [rho * Math.sin(lonDeg * DEG), rho * Math.cos(lonDeg * DEG)];
    }

    covers(latDeg, lonDeg) {
        if (!this.regions || latDeg > -75) return false;
        const [x, y] = this.polar(latDeg, lonDeg);
        return this.regions.some((r) => gap(r, x, y) === 0);
    }

    heightAt(latDeg, lonDeg) {
        if (!this.shown.length || latDeg > -75) return null;
        const [x, y] = this.polar(latDeg, lonDeg);
        for (const tile of this.shown) {
            if (gap(tile, x, y) > 0) continue;
            const h = this.entries.get(tile.key).heights;
            const n = tile.cols;
            const fx = Math.min(n - 1.001, (x - tile.x0) / (tile.x1 - tile.x0) * (n - 1));
            const fy = Math.min(tile.rows - 1.001, (tile.y1 - y) / (tile.y1 - tile.y0) * (tile.rows - 1));
            const j = Math.floor(fx), i = Math.floor(fy), u = fx - j, v = fy - i, k = i * n + j;
            const top = h[k] + (h[k + 1] - h[k]) * u;
            const bottom = h[k + n] + (h[k + n + 1] - h[k + n]) * u;
            return (top + (bottom - top) * v) * this.unit;
        }
        return null;
    }

    meshes() {
        return this.shown.map((tile) => this.entries.get(tile.key).mesh);
    }

    pickLevel(mpp) {
        for (const [level, base] of LEVELS) {
            const limit = level === 80 && this.mode === "photo" ? PHOTO_FAR : base;
            if (mpp < limit * (this.level === level ? HOLD : 1)) return level;
        }
        return 0;
    }

    update(camera, heightPx, latDeg, lonDeg, target, active) {
        if (!this.regions || !active || latDeg > -75) return this.hide();
        this.frame++;
        const [x, y] = this.polar(latDeg, lonDeg);
        const dist = camera.position.distanceTo(target);
        const span = 2 * Math.tan(camera.fov * DEG / 2) * dist * 1000;
        const level = this.pickLevel(span / heightPx);
        if (!level) return this.hide();
        this.matrix.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse);
        this.frustum.setFromProjectionMatrix(this.matrix);
        const reach = span * Math.max(1, camera.aspect);
        const candidates = [];
        for (const region of this.regions) {
            if (gap(region, x, y) > reach) continue;
            for (const tile of region.tiles) {
                if (tile.level !== level || gap(tile, x, y) > reach) continue;
                this.sphere.set(tile.center, tile.bound);
                if (!this.frustum.intersectsSphere(this.sphere)) continue;
                candidates.push(tile);
            }
        }
        candidates.sort((a, b) => gap(a, x, y) - gap(b, x, y) || a.center.distanceToSquared(target) - b.center.distanceToSquared(target));
        const wanted = [];
        for (const tile of candidates) {
            if (wanted.length >= this.limit) break;
            if (this.failed.has(tile.key) && performance.now() - this.failed.get(tile.key) < RETRY_MS) continue;
            if (wanted.some((other) => other.region !== tile.region && overlaps(other, tile))) continue;
            wanted.push(tile);
        }
        let ready = true;
        for (const tile of wanted) {
            const entry = this.entries.get(tile.key);
            if (entry) entry.used = this.frame;
            else {
                ready = false;
                this.fetch(tile);
            }
        }
        for (const tile of this.shown) this.entries.get(tile.key).used = this.frame;
        let changed = false;
        if (ready && (wanted.length !== this.shown.length || wanted.some((tile, i) => tile !== this.shown[i]))) {
            this.show(wanted);
            this.level = level;
            changed = true;
        }
        this.evict();
        return changed;
    }

    show(tiles) {
        for (const tile of this.shown) this.entries.get(tile.key).mesh.visible = false;
        this.shown = tiles;
        const size = 2 * this.poleHalf;
        tiles.forEach((tile, i) => {
            this.entries.get(tile.key).mesh.visible = true;
            this.rects[i].set((tile.x0 + this.poleHalf) / size, (tile.y0 + this.poleHalf) / size, (tile.x1 + this.poleHalf) / size, (tile.y1 + this.poleHalf) / size);
        });
        this.count.value = tiles.length;
    }

    hide() {
        if (!this.shown.length) return false;
        this.show([]);
        this.level = 0;
        return true;
    }

    fetch(tile) {
        if (this.loading.has(tile.key) || this.loading.size >= MAX_LOADS) return;
        this.loading.add(tile.key);
        const url = this.base + tile.key;
        Promise.all([readHeights(url + ".bin.gz"), this.loader.loadAsync(url + ".jpg")]).then(([heights, texture]) => {
            if (heights.length !== tile.cols * tile.rows) throw new Error(tile.key + " size");
            if (this.disposed) {
                texture.dispose();
                return;
            }
            texture.colorSpace = THREE.SRGBColorSpace;
            texture.anisotropy = this.anisotropy;
            const photo = new THREE.MeshBasicMaterial({ map: texture });
            const mesh = new THREE.Mesh(this.build(tile, heights), this.mode === "photo" ? photo : this.reliefMaterial());
            mesh.position.copy(tile.center);
            mesh.castShadow = true;
            mesh.receiveShadow = true;
            mesh.visible = false;
            this.group.add(mesh);
            this.entries.set(tile.key, { heights, mesh, photo, texture, used: this.frame });
            this.failed.delete(tile.key);
        }).catch((error) => {
            this.failed.set(tile.key, performance.now());
            console.warn("sdem tile", error);
        }).finally(() => {
            this.loading.delete(tile.key);
            this.onChange();
        });
    }

    build(tile, heights) {
        const W = tile.cols, H = tile.rows;
        const dx = (tile.x1 - tile.x0) / (W - 1), dy = (tile.y1 - tile.y0) / (H - 1);
        const ring = [];
        for (let j = 0; j < W; j++) ring.push(j);
        for (let i = 1; i < H; i++) ring.push(i * W + W - 1);
        for (let j = W - 2; j >= 0; j--) ring.push((H - 1) * W + j);
        for (let i = H - 2; i > 0; i--) ring.push(i * W);
        ring.push(0);
        const count = W * H + ring.length;
        const pos = new Float32Array(count * 3), uv = new Float32Array(count * 2), uv1 = new Float32Array(count * 2), nor = new Float32Array(count * 3);
        const c = tile.center, size = 2 * this.poleHalf;
        for (let i = 0; i < H; i++) {
            for (let j = 0; j < W; j++) {
                const k = i * W + j;
                const x = tile.x0 + j * dx, y = tile.y1 - i * dy;
                const p = this.toScene(x, y, heights[k] * this.unit);
                pos[k * 3] = p[0] - c.x; pos[k * 3 + 1] = p[1] - c.y; pos[k * 3 + 2] = p[2] - c.z;
                uv[k * 2] = j / (W - 1);
                uv[k * 2 + 1] = 1 - i / (H - 1);
                uv1[k * 2] = (x + this.poleHalf) / size;
                uv1[k * 2 + 1] = (y + this.poleHalf) / size;
            }
        }
        const at = (i, j, a) => pos[(i * W + j) * 3 + a];
        for (let i = 0; i < H; i++) {
            for (let j = 0; j < W; j++) {
                const k = i * W + j;
                const jl = Math.max(0, j - 1), jr = Math.min(W - 1, j + 1), iu = Math.max(0, i - 1), id = Math.min(H - 1, i + 1);
                const ex = at(i, jr, 0) - at(i, jl, 0), ey = at(i, jr, 1) - at(i, jl, 1), ez = at(i, jr, 2) - at(i, jl, 2);
                const nx0 = at(iu, j, 0) - at(id, j, 0), ny0 = at(iu, j, 1) - at(id, j, 1), nz0 = at(iu, j, 2) - at(id, j, 2);
                let nx = ey * nz0 - ez * ny0, ny = ez * nx0 - ex * nz0, nz = ex * ny0 - ey * nx0;
                const px = pos[k * 3] + c.x, py = pos[k * 3 + 1] + c.y, pz = pos[k * 3 + 2] + c.z;
                if (nx * px + ny * py + nz * pz < 0) { nx = -nx; ny = -ny; nz = -nz; }
                const len = Math.hypot(nx, ny, nz) || 1;
                nor[k * 3] = nx / len; nor[k * 3 + 1] = ny / len; nor[k * 3 + 2] = nz / len;
            }
        }
        let o = W * H;
        const skirt = [];
        for (const k of ring) {
            const px = pos[k * 3] + c.x, py = pos[k * 3 + 1] + c.y, pz = pos[k * 3 + 2] + c.z;
            const f = (Math.hypot(px, py, pz) - SKIRT_KM) / Math.hypot(px, py, pz);
            pos[o * 3] = px * f - c.x; pos[o * 3 + 1] = py * f - c.y; pos[o * 3 + 2] = pz * f - c.z;
            uv[o * 2] = uv[k * 2]; uv[o * 2 + 1] = uv[k * 2 + 1];
            uv1[o * 2] = uv1[k * 2]; uv1[o * 2 + 1] = uv1[k * 2 + 1];
            nor[o * 3] = nor[k * 3]; nor[o * 3 + 1] = nor[k * 3 + 1]; nor[o * 3 + 2] = nor[k * 3 + 2];
            skirt.push(o++);
        }
        const index = new Uint32Array((W - 1) * (H - 1) * 6 + (ring.length - 1) * 6);
        let t = 0;
        for (let i = 0; i < H - 1; i++) {
            for (let j = 0; j < W - 1; j++) {
                const a = i * W + j, b = a + 1, d = a + W, e = d + 1;
                index[t++] = a; index[t++] = d; index[t++] = b;
                index[t++] = b; index[t++] = d; index[t++] = e;
            }
        }
        for (let m = 0; m < ring.length - 1; m++) {
            const a = ring[m], b = ring[m + 1], d = skirt[m], e = skirt[m + 1];
            index[t++] = a; index[t++] = d; index[t++] = b;
            index[t++] = b; index[t++] = d; index[t++] = e;
        }
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute("position", new THREE.BufferAttribute(pos, 3));
        geometry.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
        geometry.setAttribute("uv1", new THREE.BufferAttribute(uv1, 2));
        geometry.setAttribute("normal", new THREE.BufferAttribute(nor, 3));
        geometry.setIndex(new THREE.BufferAttribute(index, 1));
        geometry.computeBoundingSphere();
        return geometry;
    }

    evict() {
        if (this.entries.size <= KEEP) return;
        const shown = new Set(this.shown.map((tile) => tile.key));
        const old = [...this.entries.entries()].filter(([key, e]) => !shown.has(key) && e.used < this.frame).sort((a, b) => a[1].used - b[1].used);
        for (const [key, entry] of old.slice(0, this.entries.size - KEEP)) {
            this.release(entry);
            this.entries.delete(key);
        }
    }

    reliefMaterial() {
        if (!this.relief) {
            const map = this.albedo ? this.albedo.clone() : null;
            if (map) {
                map.channel = 1;
                map.needsUpdate = true;
            }
            this.relief = new THREE.MeshStandardMaterial({ map, color: map ? 0xffffff : 0xb4b4b4, roughness: 1, metalness: 0 });
        }
        return this.relief;
    }

    setMode(mode) {
        this.mode = mode === "photo" ? "photo" : "relief";
        for (const entry of this.entries.values()) entry.mesh.material = this.mode === "photo" ? entry.photo : this.reliefMaterial();
    }

    release(entry) {
        this.group.remove(entry.mesh);
        entry.mesh.geometry.dispose();
        entry.texture.dispose();
        entry.photo.dispose();
    }

    dispose() {
        this.disposed = true;
        this.show([]);
        for (const entry of this.entries.values()) this.release(entry);
        this.entries.clear();
        if (this.relief) {
            if (this.relief.map) this.relief.map.dispose();
            this.relief.dispose();
        }
        this.scene.remove(this.group);
    }
}
