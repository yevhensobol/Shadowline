import * as THREE from "../lib/three/three.min.js";

const LABEL_SHIFTS = [0, 16, -16, 32, -32];
const DEG = Math.PI / 180;
const R = 1737.4;
const EARTH_R = 6371.0;
const EARTH_SCENE_DIST = 30 * R;
const SUN_SCENE_DIST = 60000;
const STAR_DIST = 90000;
const POLE_LAT = -76;
const MIN_RANGE = 6;
const MAX_RANGE = 40000;
const RELIEF_RANGE = 3000;
const TERRAIN_TILES = 24;
const TERRAIN_MIN_RANGE = 0.4;
const WAC_TILES = "https://trek.nasa.gov/tiles/Moon/SP/LRO_WAC_Mosaic_SPole60_100m_v02/1.0.0/default/default028mm/";
const WAC_ORIGIN_M = 1095930;
const WAC_LEVEL = 5;
const WAC_SIZE = 2048;
const WAC_CAP_M = 931000;
const NAC_TILES = "https://trek.nasa.gov/tiles/Moon/SP/LRO_NAC_AvgMosaic_SPole855_1mp/1.0.0/default/default028mm/";
const NAC_EDGE_M = 136500;
const NAC_BASE_LEVEL = 6;
const NAC_MAX_LEVEL = 11;
const NAC_SPAN = 9;
const ZONES = [{ lat: -87, key: "poleZoneInner" }, { lat: -84, key: "poleZoneOuter" }];

const terrainDiscard = (uv) => `    for (int i = 0; i < ${TERRAIN_TILES}; i++) {
        if (i >= terrainCount) break;
        vec4 sr = terrainRects[i];
        if (${uv}.x > sr.x && ${uv}.x < sr.z && ${uv}.y > sr.y && ${uv}.y < sr.w) discard;
    }`;

const me = (x, y, z) => new THREE.Vector3(x, z, -y);

function fromLatLon(latDeg, lonDeg, radius = R) {
    const lat = latDeg * DEG, lon = lonDeg * DEG;
    return me(radius * Math.cos(lat) * Math.cos(lon), radius * Math.cos(lat) * Math.sin(lon), radius * Math.sin(lat));
}

function toLatLon(v) {
    const x = v.x, y = -v.z, z = v.y;
    const r = Math.hypot(x, y, z);
    return { lat: Math.asin(z / r) / DEG, lon: Math.atan2(y, x) / DEG };
}

function localFrame(latDeg, lonDeg) {
    const up = fromLatLon(latDeg, lonDeg, 1).normalize();
    const pole = new THREE.Vector3(0, 1, 0);
    let east = pole.clone().cross(up);
    if (east.lengthSq() < 1e-10) east = new THREE.Vector3(1, 0, 0);
    east.normalize();
    const north = up.clone().cross(east).normalize();
    return { up, east, north };
}

function julianDay(date) {
    return date.getTime() / 86400000 + 2440587.5;
}

function moonFrameFromJ2000(date) {
    const d = julianDay(date) - 2451545.0 + 69.184 / 86400;
    const T = d / 36525;
    const e = [0, 125.045 - 0.0529921 * d, 250.089 - 0.1059842 * d, 260.008 + 13.0120009 * d, 176.625 + 13.3407154 * d,
        357.529 + 0.9856003 * d, 311.589 + 26.4057084 * d, 134.963 + 13.0649930 * d, 276.617 + 0.3287146 * d,
        34.226 + 1.7484877 * d, 15.134 - 0.1589763 * d, 119.743 + 0.0036096 * d, 239.961 + 0.1643573 * d,
        25.053 + 12.9590088 * d].map((v) => v * DEG);
    const s = (i) => Math.sin(e[i]), c = (i) => Math.cos(e[i]);
    const ra = 269.9949 + 0.0031 * T - 3.8787 * s(1) - 0.1204 * s(2) + 0.0700 * s(3) - 0.0172 * s(4) + 0.0072 * s(6) - 0.0052 * s(10) + 0.0043 * s(13);
    const dec = 66.5392 + 0.0130 * T + 1.5419 * c(1) + 0.0239 * c(2) - 0.0278 * c(3) + 0.0068 * c(4) - 0.0029 * c(6) + 0.0009 * c(7) + 0.0008 * c(10) - 0.0009 * c(13);
    const w = 38.3213 + 13.17635815 * d - 1.4e-12 * d * d + 3.5610 * s(1) + 0.1208 * s(2) - 0.0642 * s(3) + 0.0158 * s(4) + 0.0252 * s(5)
        - 0.0066 * s(6) - 0.0047 * s(7) - 0.0046 * s(8) + 0.0028 * s(9) + 0.0052 * s(10) + 0.0040 * s(11) + 0.0019 * s(12) - 0.0044 * s(13);
    const rz = (a) => new THREE.Matrix3().set(Math.cos(a), Math.sin(a), 0, -Math.sin(a), Math.cos(a), 0, 0, 0, 1);
    const rx = (a) => new THREE.Matrix3().set(1, 0, 0, 0, Math.cos(a), Math.sin(a), 0, -Math.sin(a), Math.cos(a));
    return rz(w * DEG).multiply(rx((90 - dec) * DEG)).multiply(rz((90 + ra) * DEG));
}

function gmstRad(date) {
    const d = julianDay(date) - 2451545.0;
    return ((280.46061837 + 360.98564736629 * d) % 360) * DEG;
}

function toThreeMatrix(m3) {
    const t = new THREE.Matrix3().set(1, 0, 0, 0, 0, 1, 0, -1, 0);
    const ti = t.clone().transpose();
    const r = t.clone().multiply(m3).multiply(ti);
    const e = r.elements;
    return new THREE.Matrix4().set(e[0], e[3], e[6], 0, e[1], e[4], e[7], 0, e[2], e[5], e[8], 0, 0, 0, 0, 1);
}

function radialTexture(stops, size = 256) {
    const canvas = document.createElement("canvas");
    canvas.width = canvas.height = size;
    const g = canvas.getContext("2d");
    const gradient = g.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
    stops.forEach(([offset, color]) => gradient.addColorStop(offset, color));
    g.fillStyle = gradient;
    g.fillRect(0, 0, size, size);
    const texture = new THREE.CanvasTexture(canvas);
    texture.colorSpace = THREE.SRGBColorSpace;
    return texture;
}

function easeInOut(t) {
    return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
}

const ATMOSPHERE_VERTEX = `
#include <common>
#include <logdepthbuf_pars_vertex>
varying vec3 vNormal;
varying vec3 vView;
void main() {
    vNormal = normalize(mat3(modelMatrix) * normal);
    vec4 world = modelMatrix * vec4(position, 1.0);
    vView = normalize(cameraPosition - world.xyz);
    gl_Position = projectionMatrix * viewMatrix * world;
    #include <logdepthbuf_vertex>
}`;

const ATMOSPHERE_FRAGMENT = `
#include <logdepthbuf_pars_fragment>
uniform vec3 sunDirection;
uniform float strength;
varying vec3 vNormal;
varying vec3 vView;
void main() {
    #include <logdepthbuf_fragment>
    float rim = pow(1.0 - abs(dot(vNormal, vView)), 2.0);
    float day = smoothstep(-0.35, 0.35, dot(vNormal, sunDirection));
    float back = pow(max(dot(-vView, sunDirection), 0.0), 6.0);
    vec3 color = mix(vec3(0.25, 0.45, 1.0), vec3(0.45, 0.72, 1.0), day);
    color = mix(color, vec3(1.0, 0.42, 0.18), back);
    float glow = max(day, back * smoothstep(-0.6, 0.05, dot(vNormal, sunDirection)));
    gl_FragColor = vec4(color * rim * glow * strength * (1.0 + back * 2.0), rim * glow);
}`;

const EARTH_VERTEX = `
#include <common>
#include <logdepthbuf_pars_vertex>
varying vec2 vUv;
varying vec3 vNormal;
varying vec3 vView;
void main() {
    vUv = uv;
    vNormal = normalize(mat3(modelMatrix) * normal);
    vec4 world = modelMatrix * vec4(position, 1.0);
    vView = normalize(cameraPosition - world.xyz);
    gl_Position = projectionMatrix * viewMatrix * world;
    #include <logdepthbuf_vertex>
}`;

const EARTH_FRAGMENT = `
#include <common>
#include <logdepthbuf_pars_fragment>
uniform sampler2D dayMap;
uniform sampler2D nightMap;
uniform sampler2D cloudMap;
uniform float hasNight;
uniform float hasClouds;
uniform vec3 sunDirection;
varying vec2 vUv;
varying vec3 vNormal;
varying vec3 vView;
void main() {
    #include <logdepthbuf_fragment>
    float ndl = dot(vNormal, sunDirection);
    float light = clamp(ndl, 0.0, 1.0);
    vec3 day = texture2D(dayMap, vUv).rgb;
    float cloud = hasClouds * texture2D(cloudMap, vUv).r;
    vec3 lit = mix(day, vec3(1.0), cloud * 0.85) * light * 1.35;
    vec3 halfway = normalize(sunDirection + vView);
    float water = smoothstep(0.12, 0.02, length(day - vec3(0.02, 0.06, 0.16))) * (1.0 - cloud);
    lit += vec3(1.0, 0.92, 0.8) * pow(max(dot(vNormal, halfway), 0.0), 60.0) * water * light * 0.8;
    float night = smoothstep(0.05, -0.15, ndl);
    vec3 cities = hasNight * texture2D(nightMap, vUv).rgb * vec3(1.0, 0.8, 0.55) * night * (1.0 - cloud * 0.7) * 1.6;
    float rim = pow(1.0 - max(dot(vNormal, vView), 0.0), 2.5);
    vec3 haze = vec3(0.35, 0.6, 1.0) * rim * smoothstep(-0.2, 0.4, ndl) * 0.9;
    gl_FragColor = vec4(lit + cities + haze, 1.0);
    #include <colorspace_fragment>
}`;

class MoonScene {
    constructor(canvas, overlay, callbacks, options) {
        this.canvas = canvas;
        this.overlay = overlay;
        this.callbacks = callbacks;
        this.options = Object.assign({ base: "img/scene/", quality: "auto" }, options || {});
        this.sites = [];
        this.siteElements = new Map();
        this.selected = null;
        this.dirty = true;
        this.time = new Date(Date.UTC(2026, 10, 1));
        this.sunDir = new THREE.Vector3(1, 0, 0);
        this.earthDir = new THREE.Vector3(1, 0, 0);
        this.view = { lat: -30, lon: 10, range: 6200, tilt: 0, heading: 0 };
        this.velocity = { x: 0, y: 0 };
        this.flight = null;
        this.disposed = false;
    }

    async init() {
        const mobile = Math.min(window.innerWidth, window.innerHeight) < 700;
        this.renderer = new THREE.WebGLRenderer({ canvas: this.canvas, antialias: true, logarithmicDepthBuffer: true, powerPreference: "high-performance" });
        this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, mobile ? 1.5 : 2));
        this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
        this.renderer.toneMappingExposure = 1.15;
        this.renderer.outputColorSpace = THREE.SRGBColorSpace;
        this.renderer.shadowMap.enabled = true;
        this.renderer.shadowMap.type = THREE.PCFShadowMap;
        const maxTexture = this.renderer.capabilities.maxTextureSize;
        this.big = !mobile && maxTexture >= 8192 && this.options.quality !== "low";

        this.scene = new THREE.Scene();
        this.camera = new THREE.PerspectiveCamera(40, 1, 0.005, 400000);

        this.sun = new THREE.DirectionalLight(0xfff3e2, 3.3);
        this.sun.castShadow = true;
        this.sun.shadow.mapSize.set(mobile ? 2048 : 4096, mobile ? 512 : 1024);
        const sc = this.sun.shadow.camera;
        sc.left = -345; sc.right = 345; sc.top = 60; sc.bottom = -60; sc.near = 10; sc.far = 8000;
        this.sun.shadow.bias = -0.0002;
        this.sun.shadow.normalBias = 0.4;
        this.scene.add(this.sun, this.sun.target);
        this.earthshine = new THREE.DirectionalLight(0x9bbcff, 0.05);
        this.scene.add(this.earthshine, this.earthshine.target);
        this.scene.add(new THREE.AmbientLight(0x1c2840, 0.03));

        this.loader = new THREE.TextureLoader();
        this.buildStars();
        await this.buildMoon();
        this.buildEarth();
        this.buildSun();
        this.buildComposer();

        this.resizeObserver = new ResizeObserver(() => this.resize());
        this.resizeObserver.observe(this.canvas.parentElement);
        this.resize();
        this.bindInput();
        this.applyTime();
        this.loop = this.loop.bind(this);
        requestAnimationFrame(this.loop);

        this.polePromise = this.loadPole();
        this.loadRelief();
        this.loadTerrain();
        this.loadEarthTextures();
        this.loadStarMap();
    }

    texture(name, srgb = true) {
        return this.loader.loadAsync(this.options.base + name).then((t) => {
            if (srgb) t.colorSpace = THREE.SRGBColorSpace;
            t.anisotropy = Math.min(8, this.renderer.capabilities.getMaxAnisotropy());
            this.dirty = true;
            return t;
        });
    }

    optionalTexture(name, srgb = true) {
        return this.texture(name, srgb).catch(() => null);
    }

    buildStars() {
        let seed = 7;
        const random = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
        const positions = [], colors = [];
        for (let i = 0; i < 6000; i++) {
            const u = random() * 2 - 1, theta = random() * Math.PI * 2, r = Math.sqrt(1 - u * u);
            positions.push(r * Math.cos(theta) * STAR_DIST, u * STAR_DIST, r * Math.sin(theta) * STAR_DIST);
            const b = Math.pow(random(), 9) + 0.05, t = random();
            const c = t < 0.15 ? [1, 0.82, 0.66] : t < 0.35 ? [0.76, 0.86, 1] : [1, 1, 1];
            colors.push(c[0] * b, c[1] * b, c[2] * b);
        }
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
        geometry.setAttribute("color", new THREE.Float32BufferAttribute(colors, 3));
        this.starPoints = new THREE.Points(geometry, new THREE.PointsMaterial({ size: 1.6, sizeAttenuation: false, vertexColors: true, depthWrite: false }));
        this.starPoints.renderOrder = -10;
        this.scene.add(this.starPoints);
    }

    async loadStarMap() {
        const map = await this.optionalTexture(this.big ? "stars_4k.jpg" : "stars_2k.jpg");
        if (!map || this.disposed) return;
        const sphere = new THREE.Mesh(new THREE.SphereGeometry(STAR_DIST, 64, 32),
            new THREE.MeshBasicMaterial({ map, side: THREE.DoubleSide, depthWrite: false, toneMapped: false, color: 0x9c9c9c }));
        sphere.renderOrder = -10;
        sphere.matrixAutoUpdate = false;
        this.starSphere = sphere;
        this.scene.add(sphere);
        this.scene.remove(this.starPoints);
        this.applyTime();
    }

    async buildMoon() {
        const map = await this.texture("moon_color_4k.jpg");
        const normalMap = await this.optionalTexture(this.big ? "moon_normal_4k.jpg" : "moon_normal_2k.jpg", false);
        const material = new THREE.MeshStandardMaterial({ map, roughness: 1, metalness: 0 });
        if (normalMap) {
            material.normalMap = normalMap;
            material.normalScale.set(2.2, 2.2);
        } else {
            material.bumpMap = map;
            material.bumpScale = 1.6;
        }
        this.photoUniform = this.photoUniform || { value: 0 };
        if (!this.photoMap) {
            const blank = new THREE.DataTexture(new Uint8Array([0, 0, 0, 255]), 1, 1);
            blank.needsUpdate = true;
            this.photoMap = { value: blank };
        }
        material.onBeforeCompile = (shader) => {
            shader.uniforms.poleHalf = { value: this.poleReady ? 300.0 : 0.0 };
            shader.uniforms.photoOn = this.photoUniform;
            shader.uniforms.photoMap = this.photoMap;
            shader.uniforms.holeDir = { value: new THREE.Vector3() };
            shader.uniforms.holeCos = { value: 2.0 };
            this.moonShader = shader;
            shader.vertexShader = shader.vertexShader
                .replace("#include <common>", "#include <common>\nvarying vec3 vObj;")
                .replace("#include <begin_vertex>", "#include <begin_vertex>\nvObj = position;");
            shader.fragmentShader = shader.fragmentShader
                .replace("#include <common>", "#include <common>\nvarying vec3 vObj;\nuniform float poleHalf;\nuniform vec3 holeDir;\nuniform float holeCos;\nuniform float photoOn;\nuniform sampler2D photoMap;")
                .replace("#include <opaque_fragment>", "vec3 pu = normalize(vec3(vObj.x, -vObj.z, vObj.y));\nfloat photoMix = photoOn * smoothstep(-0.87, -0.9, pu.z);\nif (photoMix > 0.0) {\n    float pk = 3474800.0 / (1.0 - pu.z);\n    vec3 pc = texture2D(photoMap, vec2(pk * pu.y, pk * pu.x) / 1862000.0 + 0.5).rgb;\n    outgoingLight = mix(outgoingLight, pc * 0.62, photoMix);\n}\n#include <opaque_fragment>")
                .replace("void main() {", `void main() {
    if (dot(normalize(vObj), holeDir) > holeCos) discard;
    if (poleHalf > 0.0) {
        vec3 u = normalize(vec3(vObj.x, -vObj.z, vObj.y));
        if (u.z < -0.5) {
            float k = 2.0 * ${R.toFixed(1)} / (1.0 - u.z);
            if (abs(k * u.y) < poleHalf && abs(k * u.x) < poleHalf) discard;
        }
    }`);
        };
        this.moonMaterial = material;
        this.moon = new THREE.Mesh(new THREE.SphereGeometry(R, 384, 192), material);
        this.moon.receiveShadow = true;
        this.scene.add(this.moon);
    }

    async loadRelief() {
        try {
            const module = await import(new URL("./relief.js" + new URL(import.meta.url).search, import.meta.url).href);
            if (this.disposed) return;
            const relief = new module.Relief({ base: this.options.base + "../../data/relief/", radius: R, scene: this.scene, photo: this.photoUniform, photoMap: this.photoMap, source: () => this.moonMaterial, onChange: () => { this.dirty = true; } });
            await relief.init();
            if (this.disposed) { relief.dispose(); return; }
            this.relief = relief;
            if (this.poleReady && this.poleGrid) relief.poleHalf.value = this.poleGrid.half / 1000 - 0.4;
            this.dirty = true;
            if (this.polePromise) await this.polePromise;
            if (this.pole && !this.disposed) {
                const material = this.pole.material.clone();
                material.onBeforeCompile = this.pole.material.onBeforeCompile;
                material.defines = Object.assign({}, material.defines, { POLE_DETAIL: "" });
                const detail = new module.PoleDetail({ base: this.options.base + "../../data/pole/", radius: R, scene: this.scene, material, depthMaterial: this.poleDepth, mask: this.layerUniforms.detailMask.value, onChange: () => { this.dirty = true; } });
                await detail.init();
                if (this.disposed) { detail.dispose(); return; }
                this.layerUniforms.detailScale.value = 2 * detail.half / (16 * detail.tileM);
                this.poleDetail = detail;
                this.dirty = true;
            }
        } catch (error) {
            console.warn("relief", error);
        }
    }

    async loadTerrain() {
        try {
            const module = await import(new URL("./terrain.js" + new URL(import.meta.url).search, import.meta.url).href);
            if (this.polePromise) await this.polePromise;
            if (this.disposed || !this.poleGrid) return;
            const terrain = new module.Terrain({ decorate: (material) => this.decorateTerrain(material), base: this.options.base + "../../data/ldem/", radius: R, scene: this.scene, poleHalf: this.poleGrid.half, albedo: this.pole ? this.pole.material.map : null, count: this.layerUniforms.terrainCount, rects: this.layerUniforms.terrainRects.value, onChange: () => { this.dirty = true; } });
            await terrain.init();
            if (this.disposed) { terrain.dispose(); return; }
            this.terrain = terrain;
            this.dirty = true;
        } catch (error) {
            console.warn("terrain", error);
        }
    }

    decorateTerrain(material) {
        material.onBeforeCompile = (shader) => {
            shader.uniforms.zoneOn = this.layerUniforms.zoneOn;
            shader.uniforms.zoneR = this.layerUniforms.zoneR;
            shader.fragmentShader = shader.fragmentShader
                .replace("#include <common>", "#include <common>\nuniform float zoneOn;\nuniform vec2 zoneR;")
                .replace("#include <emissivemap_fragment>", `#include <emissivemap_fragment>
if (zoneOn > 0.5) {
    float zd = length(vMapUv - 0.5);
    float zw = max(fwidth(zd), 1e-7);
    float inner = 1.0 - smoothstep(zw * 0.4, zw * 1.4, abs(zd - zoneR.x));
    float dash = step(0.5, fract(atan(vMapUv.y - 0.5, vMapUv.x - 0.5) * 28.647890));
    float outer = (1.0 - smoothstep(zw * 0.3, zw * 1.2, abs(zd - zoneR.y))) * dash;
    float fill = (1.0 - smoothstep(zoneR.x - zw, zoneR.x + zw, zd)) * 0.012;
    totalEmissiveRadiance += vec3(1.0, 0.72, 0.38) * (max(inner, outer * 0.8) * 0.5 + fill);
}`);
        };
    }

    hideRelief() {
        if (this.relief) this.relief.group.visible = false;
        if (this.terrain && this.terrain.hide()) this.dirty = true;
        if (this.poleDetail) {
            this.poleDetail.update(this.camera, 1, false);
            this.layerUniforms.detailOn.value = 0;
        }
        if (this.moon.scale.x !== 1) {
            this.moon.scale.setScalar(1);
            this.moon.receiveShadow = true;
            this.moonMaterial.needsUpdate = true;
        }
    }

    setReliefShadow(target, range) {
        const half = 20 * 2 ** Math.ceil(Math.log2(Math.max(1, range * 1.1 / 20)));
        const step = Math.max(5, half / 2);
        const snapped = target.clone().divideScalar(step).round().multiplyScalar(step);
        const size = this.big ? 4096 : 2048;
        const sc = this.sun.shadow.camera;
        if (this.shadowMode !== "relief") {
            this.sun.shadow.map && this.sun.shadow.map.dispose();
            this.sun.shadow.map = null;
            this.sun.shadow.mapSize.set(size, size);
            this.shadowMode = "relief";
            this.shadowHalf = 0;
        }
        if (this.shadowHalf !== half) {
            sc.left = -half; sc.right = half; sc.top = half; sc.bottom = -half; sc.near = 10; sc.far = 8000;
            sc.updateProjectionMatrix();
            this.sun.shadow.normalBias = Math.max(0.08, 5 * half / size);
            this.sun.shadow.bias = -0.0006;
            this.shadowHalf = half;
        }
        this.reliefTarget = snapped;
    }

    setPoleShadow() {
        const sc = this.sun.shadow.camera;
        sc.left = -345; sc.right = 345; sc.top = 60; sc.bottom = -60; sc.near = 10; sc.far = 8000;
        sc.updateProjectionMatrix();
        this.sun.shadow.normalBias = 0.4;
        this.sun.shadow.bias = -0.0002;
        this.sun.shadow.map && this.sun.shadow.map.dispose();
        this.sun.shadow.map = null;
        this.sun.shadow.mapSize.set(this.big ? 4096 : 2048, this.big ? 1024 : 512);
        this.shadowMode = "pole";
        this.reliefTarget = null;
    }

    async loadPole() {
        try {
            const response = await fetch(this.options.base + "pole_height.bin");
            if (!response.ok) return;
            const buffer = await response.arrayBuffer();
            const header = new DataView(buffer, 0, 12);
            const n = header.getInt32(0, true), half = header.getFloat32(4, true), unit = header.getFloat32(8, true);
            const heights = new Int16Array(buffer, 12, n * n);
            const spacing = 2 * half / n;
            const positions = new Float32Array(n * n * 3), uvs = new Float32Array(n * n * 2);
            const p = new THREE.Vector3();
            for (let i = 0; i < n; i++) {
                for (let j = 0; j < n; j++) {
                    const k = i * n + j;
                    const xm = -half + (j + 0.5) * spacing, ym = half - (i + 0.5) * spacing;
                    const rho = Math.hypot(xm, ym) / 1000;
                    const lat = -Math.PI / 2 + 2 * Math.atan(rho / (2 * R));
                    const lon = Math.atan2(xm, ym);
                    const r = R + heights[k] * unit / 1000;
                    p.copy(me(r * Math.cos(lat) * Math.cos(lon), r * Math.cos(lat) * Math.sin(lon), r * Math.sin(lat)));
                    positions[k * 3] = p.x; positions[k * 3 + 1] = p.y; positions[k * 3 + 2] = p.z;
                    uvs[k * 2] = (j + 0.5) / n; uvs[k * 2 + 1] = 1 - (i + 0.5) / n;
                }
            }
            const index = new Uint32Array((n - 1) * (n - 1) * 6);
            let t = 0;
            for (let i = 0; i < n - 1; i++) {
                for (let j = 0; j < n - 1; j++) {
                    const a = i * n + j, b = a + 1, c = a + n, d = c + 1;
                    index[t++] = a; index[t++] = c; index[t++] = b; index[t++] = b; index[t++] = c; index[t++] = d;
                }
            }
            const geometry = new THREE.BufferGeometry();
            geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
            geometry.setAttribute("uv", new THREE.BufferAttribute(uvs, 2));
            geometry.setIndex(new THREE.BufferAttribute(index, 1));
            geometry.computeVertexNormals();
            geometry.computeBoundingSphere();
            const albedo = await this.texture("pole_albedo.jpg");
            if (this.disposed) return;
            const material = new THREE.MeshStandardMaterial({ map: albedo, roughness: 1, metalness: 0 });
            this.installLayerShader(material);
            this.pole = new THREE.Mesh(geometry, material);
            this.pole.castShadow = true;
            this.pole.receiveShadow = true;
            this.pole.customDepthMaterial = this.poleDepth;
            this.scene.add(this.pole);
            this.poleCenter = fromLatLon(-90, 0, R);
            this.poleGrid = { n, half, unit, heights, index };
            this.poleReady = true;
            if (this.moonShader) this.moonShader.uniforms.poleHalf.value = half / 1000 - 1.5;
            if (this.relief) this.relief.poleHalf.value = half / 1000 - 0.4;
            this.layerUniforms.gridCells.value = 2 * half / 1000;
            this.applyTime();
        } catch (error) {
            console.warn("pole tile", error);
        }
    }

    installLayerShader(material) {
        const blank = new THREE.DataTexture(new Uint8Array([0, 0, 0, 255]), 1, 1);
        blank.needsUpdate = true;
        const mask = new THREE.DataTexture(new Uint8Array(256), 16, 16, THREE.RedFormat, THREE.UnsignedByteType);
        mask.magFilter = THREE.NearestFilter;
        mask.minFilter = THREE.NearestFilter;
        mask.needsUpdate = true;
        this.layerUniforms = { detailMask: { value: mask }, detailOn: { value: 0 }, detailScale: { value: 1 }, layerMode: { value: 0 }, lightMap: { value: blank }, slopeMap: { value: blank }, mlMap: { value: blank }, divMap: { value: blank }, nowMap: { value: blank }, nowOn: { value: 0 }, nowScale: { value: 1 }, gridOn: { value: 0 }, gridCells: { value: 608 }, photoOn: this.photoUniform, nacMap: { value: blank }, nacRect: { value: new THREE.Vector4() }, nacScale: { value: 1 }, focusMap: { value: blank }, focusRect: { value: new THREE.Vector4() }, focusOn: { value: 0 }, zoneOn: { value: 0 }, zoneR: { value: new THREE.Vector2() }, terrainCount: { value: 0 }, terrainRects: { value: Array.from({ length: TERRAIN_TILES }, () => new THREE.Vector4()) } };
        this.poleDepth = new THREE.MeshDepthMaterial({ depthPacking: THREE.RGBADepthPacking });
        this.poleDepth.onBeforeCompile = (shader) => {
            shader.uniforms.terrainCount = this.layerUniforms.terrainCount;
            shader.uniforms.terrainRects = this.layerUniforms.terrainRects;
            shader.vertexShader = shader.vertexShader
                .replace("#include <common>", "#include <common>\nvarying vec2 vTerrainUv;")
                .replace("#include <begin_vertex>", "#include <begin_vertex>\nvTerrainUv = uv;");
            shader.fragmentShader = shader.fragmentShader
                .replace("#include <common>", `#include <common>
varying vec2 vTerrainUv;
uniform int terrainCount;
uniform vec4 terrainRects[${TERRAIN_TILES}];`)
                .replace("void main() {", `void main() {
${terrainDiscard("vTerrainUv")}`);
        };
        material.onBeforeCompile = (shader) => {
            Object.assign(shader.uniforms, this.layerUniforms);
            shader.fragmentShader = shader.fragmentShader
                .replace("#include <common>", `#include <common>
uniform float layerMode;
uniform sampler2D lightMap;
uniform sampler2D slopeMap;
uniform sampler2D mlMap;
uniform sampler2D divMap;
uniform sampler2D nowMap;
uniform float nowOn;
uniform float nowScale;
uniform float gridOn;
uniform float gridCells;
uniform sampler2D detailMask;
uniform float detailOn;
uniform float detailScale;
uniform float photoOn;
uniform sampler2D nacMap;
uniform vec4 nacRect;
uniform float nacScale;
uniform sampler2D focusMap;
uniform vec4 focusRect;
uniform float focusOn;
uniform float zoneOn;
uniform vec2 zoneR;
uniform int terrainCount;
uniform vec4 terrainRects[${TERRAIN_TILES}];
vec4 rectSample(sampler2D tex, vec4 rect, vec2 uv) {
    vec2 t = (uv - rect.xy) / max(rect.zw - rect.xy, vec2(1e-9));
    vec2 e = min(t, 1.0 - t);
    vec4 c = texture2D(tex, t);
    return vec4(c.rgb, c.a * smoothstep(0.0, 0.03, min(e.x, e.y)));
}
float gridLine(vec2 c, vec2 w, float width) {
    vec2 d = abs(fract(c + 0.5) - 0.5) / max(w, vec2(1e-6));
    return 1.0 - smoothstep(width - 0.5, width + 0.5, min(d.x, d.y));
}
vec3 ramp5(float v, float a, float b, float c, float d, float e, vec3 ca, vec3 cb, vec3 cc, vec3 cd, vec3 ce) {
    if (v < b) return mix(ca, cb, clamp((v - a) / (b - a), 0.0, 1.0));
    if (v < c) return mix(cb, cc, (v - b) / (c - b));
    if (v < d) return mix(cc, cd, (v - c) / (d - c));
    return mix(cd, ce, clamp((v - d) / (e - d), 0.0, 1.0));
}`)
                .replace("void main() {", `void main() {
#ifndef POLE_DETAIL
    if (detailOn > 0.5 && texture2D(detailMask, vec2(vMapUv.x, 1.0 - vMapUv.y) * detailScale).r > 0.5) discard;
#endif
${terrainDiscard("vMapUv")}`)
                .replace("#include <emissivemap_fragment>", `#include <emissivemap_fragment>
if (layerMode > 0.5) {
    vec3 layer;
    float cover = 1.0;
    if (layerMode < 2.5 && nowOn > 0.5) {
        vec2 nu = (vMapUv - 0.5) * nowScale + 0.5;
        vec4 nw = texture2D(nowMap, nu);
        cover = (nu.x < 0.0 || nu.x > 1.0 || nu.y < 0.0 || nu.y > 1.0) ? 0.0 : smoothstep(0.3, 0.7, nw.b);
        layer = layerMode < 1.5 ? mix(vec3(0.05, 0.05, 0.09), vec3(1.0, 0.82, 0.38), nw.r) : mix(vec3(0.05, 0.05, 0.09), vec3(0.45, 0.72, 1.0), nw.g);
    } else if (layerMode < 1.5) {
        vec4 l = texture2D(lightMap, vMapUv);
        layer = l.b > 0.5 ? vec3(0.17, 0.45, 0.92) : ramp5(l.r, 0.0, 0.25, 0.5, 0.75, 1.0, vec3(0.07, 0.07, 0.12), vec3(0.36, 0.22, 0.1), vec3(0.8, 0.5, 0.15), vec3(0.97, 0.75, 0.3), vec3(1.0, 0.97, 0.85));
    } else if (layerMode < 2.5) {
        float g = texture2D(lightMap, vMapUv).g;
        layer = ramp5(g, 0.0, 0.25, 0.5, 0.75, 1.0, vec3(0.06, 0.06, 0.1), vec3(0.1, 0.2, 0.38), vec3(0.16, 0.4, 0.7), vec3(0.4, 0.66, 0.92), vec3(0.8, 0.92, 1.0));
    } else if (layerMode < 3.5) {
        float s = texture2D(slopeMap, vMapUv).r * 63.75;
        layer = ramp5(s, 0.0, 5.0, 10.0, 15.0, 25.0, vec3(0.16, 0.6, 0.32), vec3(0.5, 0.76, 0.28), vec3(0.93, 0.8, 0.25), vec3(0.93, 0.47, 0.16), vec3(0.7, 0.12, 0.16));
    } else if (layerMode > 4.5 && layerMode < 5.5) {
        vec4 dv = texture2D(divMap, vMapUv);
        float k = dv.g * 510.0;
        layer = dv.g < 0.002 ? vec3(0.05, 0.05, 0.07) : ramp5(k, 40.0, 100.0, 200.0, 300.0, 400.0, vec3(0.12, 0.1, 0.45), vec3(0.2, 0.45, 0.85), vec3(0.85, 0.8, 0.75), vec3(0.93, 0.45, 0.2), vec3(0.7, 0.08, 0.1));
    } else if (layerMode > 5.5) {
        vec4 dv = texture2D(divMap, vMapUv);
        float depth = dv.b * 255.0 / 50.0;
        layer = dv.b > 0.998 ? vec3(0.16, 0.13, 0.12) : ramp5(depth, 0.0, 0.1, 0.5, 1.0, 2.9, vec3(0.75, 0.97, 1.0), vec3(0.3, 0.8, 0.95), vec3(0.15, 0.5, 0.8), vec3(0.12, 0.28, 0.6), vec3(0.1, 0.12, 0.35));
    } else {
        float m = texture2D(mlMap, vMapUv).r;
        layer = m < 0.002 ? vec3(0.05, 0.05, 0.07) : ramp5(m, 0.0, 0.5, 0.8, 0.95, 1.0, vec3(0.08, 0.05, 0.16), vec3(0.23, 0.12, 0.42), vec3(0.15, 0.5, 0.56), vec3(0.45, 0.82, 0.45), vec3(0.98, 0.93, 0.35));
    }
    float relief = 0.55 + 0.45 * clamp(dot(normal, normalize(vec3(-0.35, 0.55, 0.75))), 0.0, 1.0);
    float fade = (1.0 - smoothstep(0.94, 0.995, length(vMapUv - 0.5) * 2.0)) * cover;
    totalEmissiveRadiance += pow(layer, vec3(2.2)) * relief * 0.75 * fade;
    diffuseColor.rgb *= mix(1.0, 0.25, fade);
}
if (gridOn > 0.5) {
    vec2 c = vMapUv * gridCells - 0.5 * gridCells;
    vec2 w = fwidth(c);
    float px = max(w.x, w.y);
    float fine = gridLine(c, w, 0.5) * smoothstep(6.0, 16.0, 1.0 / px);
    float major = gridLine(c / 10.0, w / 10.0, 0.6) * smoothstep(5.0, 14.0, 10.0 / px);
    float wide = gridLine(c / 100.0, w / 100.0, 0.9);
    float a = max(max(fine * 0.6, major * 0.8), wide);
    totalEmissiveRadiance += vec3(0.3, 0.5, 0.68) * a * 0.45;
    diffuseColor.rgb *= 1.0 - 0.3 * a;
}
if (zoneOn > 0.5) {
    float zd = length(vMapUv - 0.5);
    float zw = max(fwidth(zd), 1e-7);
    float inner = 1.0 - smoothstep(zw * 0.4, zw * 1.4, abs(zd - zoneR.x));
    float dash = step(0.5, fract(atan(vMapUv.y - 0.5, vMapUv.x - 0.5) * 28.647890));
    float outer = (1.0 - smoothstep(zw * 0.3, zw * 1.2, abs(zd - zoneR.y))) * dash;
    float fill = (1.0 - smoothstep(zoneR.x - zw, zoneR.x + zw, zd)) * 0.012;
    totalEmissiveRadiance += vec3(1.0, 0.72, 0.38) * (max(inner, outer * 0.8) * 0.5 + fill);
}`)
                .replace("#include <opaque_fragment>", `if (photoOn > 0.5 && layerMode < 0.5) {
    vec3 pc = diffuseColor.rgb;
    float inside = 1.0 - smoothstep(0.9, 0.98, length(vMapUv - 0.5) * nacScale);
    vec4 nc = rectSample(nacMap, nacRect, vMapUv);
    pc = mix(pc, nc.rgb, nc.a * inside);
    vec4 fc = rectSample(focusMap, focusRect, vMapUv);
    pc = mix(pc, fc.rgb, fc.a * focusOn * inside);
    outgoingLight = pc * 0.62 + totalEmissiveRadiance;
}
#include <opaque_fragment>`);
        };
    }

    async loadMl() {
        if (!this.mlLoading) {
            this.mlLoading = fetch(this.options.base + "../../data/pole_artemis.bin").then(async (response) => {
                if (!response.ok) throw new Error("ml layer");
                const buffer = await response.arrayBuffer();
                const header = new DataView(buffer, 0, 12);
                const n = header.getInt32(0, true);
                const bytes = new Uint8Array(buffer, 12, n * n);
                const flipped = new Uint8Array(n * n);
                for (let r = 0; r < n; r++) flipped.set(bytes.subarray((n - 1 - r) * n, (n - r) * n), r * n);
                const texture = new THREE.DataTexture(flipped, n, n, THREE.RedFormat, THREE.UnsignedByteType);
                texture.minFilter = THREE.LinearFilter;
                texture.magFilter = THREE.LinearFilter;
                texture.needsUpdate = true;
                this.layerUniforms.mlMap.value = texture;
                this.layerData = Object.assign(this.layerData || {}, { ml: { w: n, h: n, data: bytes } });
            });
        }
        await this.mlLoading;
    }

    async loadDiviner() {
        if (!this.divLoading) {
            this.divLoading = fetch(this.options.base + "../../data/pole_diviner.bin").then(async (response) => {
                if (!response.ok) throw new Error("diviner layer");
                const buffer = await response.arrayBuffer();
                const header = new DataView(buffer, 0, 12);
                const n = header.getInt32(0, true), unit = header.getFloat32(8, true);
                const planes = new Uint8Array(buffer, 12, 3 * n * n);
                const rgba = new Uint8Array(4 * n * n);
                for (let r = 0; r < n; r++) {
                    const src = (n - 1 - r) * n, dst = r * n;
                    for (let c = 0; c < n; c++) {
                        const o = 4 * (dst + c);
                        rgba[o] = planes[src + c]; rgba[o + 1] = planes[n * n + src + c]; rgba[o + 2] = planes[2 * n * n + src + c]; rgba[o + 3] = 255;
                    }
                }
                const texture = new THREE.DataTexture(rgba, n, n, THREE.RGBAFormat, THREE.UnsignedByteType);
                texture.minFilter = THREE.NearestFilter;
                texture.magFilter = THREE.NearestFilter;
                texture.needsUpdate = true;
                this.layerUniforms.divMap.value = texture;
                this.layerData = Object.assign(this.layerData || {}, { div: { w: n, h: n, data: planes, unit } });
            });
        }
        await this.divLoading;
    }

    async loadLayers(withSlope) {
        const read = (image) => {
            const canvas = document.createElement("canvas");
            canvas.width = image.width; canvas.height = image.height;
            const context = canvas.getContext("2d", { willReadFrequently: true });
            context.drawImage(image, 0, 0);
            return { w: image.width, h: image.height, data: context.getImageData(0, 0, image.width, image.height).data };
        };
        const prepare = (t) => { t.generateMipmaps = true; t.minFilter = THREE.LinearMipmapLinearFilter; t.needsUpdate = true; return t; };
        if (!this.lightLoading) {
            this.lightLoading = this.texture("pole_light.png", false).then((light) => {
                this.layerUniforms.lightMap.value = prepare(light);
                this.layerData = Object.assign(this.layerData || {}, { light: read(light.image) });
            });
        }
        await this.lightLoading;
        if (withSlope) {
            if (!this.slopeLoading) {
                this.slopeLoading = this.texture("pole_slope.jpg", false).then((slope) => {
                    this.layerUniforms.slopeMap.value = prepare(slope);
                    this.layerData.slope = read(slope.image);
                    if (this.route && this.route.points.length) this.rebuildRoute();
                });
            }
            await this.slopeLoading;
        }
        return this.layerData;
    }

    async setLayer(mode, labels) {
        const modes = { none: 0, sun: 1, earth: 2, slope: 3, artemis: 4, diviner: 5, ice: 6 };
        this.layerLabels = labels || this.layerLabels;
        if (this.polePromise) await this.polePromise;
        if (!this.layerUniforms) return false;
        if (mode !== "none") {
            try {
                await this.loadLayers(mode === "slope");
                if (mode === "artemis") await this.loadMl();
                if (mode === "diviner" || mode === "ice") await this.loadDiviner();
            } catch (error) { console.warn("pole layers", error); return false; }
        }
        this.layerMode = mode;
        this.layerUniforms.layerMode.value = modes[mode] || 0;
        if (mode === "none" && this.probeEl) this.probeEl.style.display = "none";
        this.dirty = true;
        return true;
    }

    async setNowMap(bytes, n, halfM) {
        if (this.polePromise) await this.polePromise;
        if (!this.layerUniforms) return false;
        if (!bytes || !n || !this.poleGrid) {
            this.nowData = null;
            this.layerUniforms.nowOn.value = 0;
            this.dirty = true;
            return true;
        }
        const rgba = new Uint8Array(4 * n * n);
        for (let r = 0; r < n; r++) {
            const src = (n - 1 - r) * n, dst = r * n;
            for (let c = 0; c < n; c++) {
                const i = 3 * (src + c), o = 4 * (dst + c);
                rgba[o] = bytes[i]; rgba[o + 1] = bytes[i + 1]; rgba[o + 2] = bytes[i + 2]; rgba[o + 3] = 255;
            }
        }
        this.nowData = { n, half: halfM, bytes: Uint8Array.from(bytes) };
        let texture = this.layerUniforms.nowMap.value;
        if (!texture || !texture.isNowMap || texture.image.width !== n) {
            texture = new THREE.DataTexture(rgba, n, n, THREE.RGBAFormat, THREE.UnsignedByteType);
            texture.minFilter = THREE.LinearFilter;
            texture.magFilter = THREE.LinearFilter;
            texture.isNowMap = true;
            this.layerUniforms.nowMap.value = texture;
        } else {
            texture.image.data.set(rgba);
        }
        texture.needsUpdate = true;
        this.layerUniforms.nowScale.value = this.poleGrid.half / halfM;
        this.layerUniforms.nowOn.value = 1;
        this.dirty = true;
        return true;
    }

    async setGrid(on) {
        if (this.polePromise) await this.polePromise;
        if (!this.layerUniforms) return false;
        if (on) {
            try { await this.loadLayers(false); } catch (error) { console.warn("pole layers", error); return false; }
        }
        this.gridOn = !!on;
        this.layerUniforms.gridOn.value = on ? 1 : 0;
        if (!on && (!this.layerMode || this.layerMode === "none") && this.probeEl) this.probeEl.style.display = "none";
        this.dirty = true;
        return true;
    }

    async probe(latDeg, lonDeg) {
        if (latDeg > -80) return null;
        if (this.polePromise) await this.polePromise;
        try { await this.loadLayers(false); } catch { return null; }
        return this.probeAt(latDeg, lonDeg);
    }

    probeAt(latDeg, lonDeg) {
        const d = this.layerData;
        if (!d || !d.light || !this.poleGrid) return null;
        const half = this.poleGrid.half;
        const colat = (90 + latDeg) * DEG;
        const rho = 2 * R * 1000 * Math.tan(colat / 2);
        const x = rho * Math.sin(lonDeg * DEG), y = rho * Math.cos(lonDeg * DEG);
        const u = (x + half) / (2 * half), v = (half - y) / (2 * half);
        if (u < 0 || u >= 1 || v < 0 || v >= 1) return null;
        const at = (img) => (Math.floor(v * img.h) * img.w + Math.floor(u * img.w)) * 4;
        const i = at(d.light);
        const slope = d.slope ? d.slope.data[at(d.slope)] / 4 : null;
        const mlValue = d.ml ? d.ml.data[Math.floor(v * d.ml.h) * d.ml.w + Math.floor(u * d.ml.w)] : 0;
        const ml = d.ml && mlValue > 0 ? mlValue / 255 : null;
        let maxK = null, ice = null;
        if (d.div) {
            const k = Math.floor(v * d.div.h) * d.div.w + Math.floor(u * d.div.w);
            const plane = d.div.w * d.div.h;
            const mx = d.div.data[plane + k], ic = d.div.data[2 * plane + k];
            if (mx > 0) { maxK = mx * d.div.unit; ice = ic === 255 ? -1 : ic / 50; }
        }
        let sunNow = null, earthNow = null;
        const nd = this.nowData;
        if (nd && this.layerUniforms.nowOn.value > 0.5) {
            const nu = (x + nd.half) / (2 * nd.half), nv = (nd.half - y) / (2 * nd.half);
            if (nu >= 0 && nu < 1 && nv >= 0 && nv < 1) {
                const k = 3 * (Math.floor(nv * nd.n) * nd.n + Math.floor(nu * nd.n));
                if (nd.bytes[k + 2] > 127) { sunNow = nd.bytes[k] > 127; earthNow = nd.bytes[k + 1] > 127; }
            }
        }
        return { x, y, sun: d.light.data[i] / 255, earth: d.light.data[i + 1] / 255, dark: d.light.data[i + 2] > 127, slope, ml, maxK, ice, sunNow, earthNow };
    }

    hover(clientX, clientY) {
        if (((!this.layerMode || this.layerMode === "none") && !this.gridOn) || this.surface || !this.pole) {
            if (this.probeEl) this.probeEl.style.display = "none";
            return;
        }
        const rect = this.canvas.getBoundingClientRect();
        const ndc = new THREE.Vector2((clientX - rect.left) / rect.width * 2 - 1, -((clientY - rect.top) / rect.height) * 2 + 1);
        const raycaster = new THREE.Raycaster();
        raycaster.setFromCamera(ndc, this.camera);
        const hit = raycaster.intersectObject(this.pole, false)[0];
        const value = hit ? (() => { const ll = toLatLon(hit.point); return this.probeAt(ll.lat, ll.lon); })() : null;
        if (!this.probeEl) {
            this.probeEl = document.createElement("div");
            this.probeEl.className = "scene-probe";
            this.overlay.appendChild(this.probeEl);
        }
        if (!value) { this.probeEl.style.display = "none"; return; }
        const l = this.layerLabels || {};
        const pct = (f) => Math.round(f * 100) + "%";
        const state = (on) => on === null || on === undefined ? null : (on ? (l.up || "up") : (l.down || "down"));
        const km = (m) => { const v = Math.floor(m / 1000); return `${v < 0 ? "\u2212" + -v : v}\u2026${v + 1 < 0 ? "\u2212" + -(v + 1) : v + 1}`; };
        const rows = [
            [l.cell || "Cell", this.gridOn ? `x ${km(value.x)}, y ${km(value.y)} ${l.km || "km"}` : null, false],
            [l.sunNow || "Sun now", state(value.sunNow), this.layerMode === "sun"],
            [l.earthNow || "Earth now", state(value.earthNow), this.layerMode === "earth"],
            [l.sun || "Sun", value.dark ? (l.dark || "never") : pct(value.sun), this.layerMode === "sun" && (value.sunNow === null || value.sunNow === undefined)],
            [l.earth || "Earth", pct(value.earth), this.layerMode === "earth" && (value.earthNow === null || value.earthNow === undefined)],
            [l.slope || "Slope", value.slope === null ? null : value.slope.toFixed(0) + "°", this.layerMode === "slope"],
            [l.artemis || "ML", value.ml === null || value.ml === undefined ? null : Math.round(value.ml * 100) + "", this.layerMode === "artemis"],
            [l.diviner || "Max", value.maxK === null || value.maxK === undefined ? null : Math.round(value.maxK) + " K", this.layerMode === "diviner"],
            [l.ice || "Ice", value.ice === null || value.ice === undefined ? null : (value.ice < 0 ? (l.noIce || "none") : value.ice.toFixed(2) + " " + (l.metre || "m")), this.layerMode === "ice"]
        ].filter((row) => row[1] !== null);
        this.probeEl.innerHTML = rows.map(([k, v, on]) => `<span class="${on ? "on" : ""}"><b>${k}</b> ${v}</span>`).join("");
        this.probeEl.style.display = "";
        this.probeEl.style.transform = `translate(${(clientX - rect.left + 16).toFixed(0)}px, ${(clientY - rect.top + 16).toFixed(0)}px)`;
    }

    buildEarth() {
        const scale = EARTH_SCENE_DIST / 384400;
        this.earthRadius = EARTH_R * scale;
        this.earth = new THREE.Group();
        const placeholder = new THREE.DataTexture(new Uint8Array([30, 60, 120, 255]), 1, 1);
        placeholder.needsUpdate = true;
        this.earthUniforms = {
            dayMap: { value: placeholder }, nightMap: { value: placeholder }, cloudMap: { value: placeholder },
            hasNight: { value: 0 }, hasClouds: { value: 0 }, sunDirection: { value: this.sunDir }
        };
        this.earthBody = new THREE.Mesh(new THREE.SphereGeometry(this.earthRadius, 128, 64),
            new THREE.ShaderMaterial({ uniforms: this.earthUniforms, vertexShader: EARTH_VERTEX, fragmentShader: EARTH_FRAGMENT }));
        this.earthBody.matrixAutoUpdate = false;
        this.earth.add(this.earthBody);
        const atmosphere = new THREE.Mesh(new THREE.SphereGeometry(this.earthRadius * 1.035, 96, 48), new THREE.ShaderMaterial({
            uniforms: { sunDirection: { value: this.sunDir }, strength: { value: 1.8 } },
            vertexShader: ATMOSPHERE_VERTEX, fragmentShader: ATMOSPHERE_FRAGMENT,
            side: THREE.BackSide, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending
        }));
        this.earth.add(atmosphere);
        this.scene.add(this.earth);
    }

    async loadEarthTextures() {
        const [day, night, clouds] = await Promise.all([
            this.optionalTexture(this.big ? "earth_day_4k.jpg" : "earth_day_2k.jpg"),
            this.optionalTexture(this.big ? "earth_night_4k.jpg" : "earth_night_2k.jpg"),
            this.optionalTexture("earth_clouds_2k.jpg", false)
        ]);
        if (this.disposed) return;
        if (day) this.earthUniforms.dayMap.value = day;
        if (night) { this.earthUniforms.nightMap.value = night; this.earthUniforms.hasNight.value = 1; }
        if (clouds) { this.earthUniforms.cloudMap.value = clouds; this.earthUniforms.hasClouds.value = 1; }
        this.dirty = true;
    }

    buildSun() {
        const disk = 2 * SUN_SCENE_DIST * Math.tan(0.2666 * DEG);
        this.sunDisk = new THREE.Sprite(new THREE.SpriteMaterial({
            map: radialTexture([[0, "rgba(255,255,252,1)"], [0.42, "rgba(255,252,240,1)"], [0.5, "rgba(255,240,210,0.9)"], [0.62, "rgba(255,220,170,0)"], [1, "rgba(255,220,170,0)"]]),
            blending: THREE.AdditiveBlending, depthWrite: false, toneMapped: false
        }));
        this.sunDisk.scale.setScalar(disk * 1.25);
        this.sunGlow = new THREE.Sprite(new THREE.SpriteMaterial({
            map: radialTexture([[0, "rgba(255,240,215,0.9)"], [0.04, "rgba(255,226,180,0.5)"], [0.16, "rgba(255,200,140,0.12)"], [0.45, "rgba(255,190,120,0.03)"], [1, "rgba(255,180,110,0)"]]),
            blending: THREE.AdditiveBlending, depthWrite: false, toneMapped: false
        }));
        this.sunGlow.scale.setScalar(disk * 40);
        this.sunGlowBase = disk * 40;
        this.scene.add(this.sunDisk, this.sunGlow);
    }

    buildComposer() {
        this.composer = new THREE.EffectComposer(this.renderer);
        this.composer.addPass(new THREE.RenderPass(this.scene, this.camera));
        this.bloom = new THREE.UnrealBloomPass(new THREE.Vector2(256, 256), 0.55, 0.5, 0.82);
        this.composer.addPass(this.bloom);
        this.composer.addPass(new THREE.OutputPass());
    }

    resize() {
        const parent = this.canvas.parentElement;
        const width = Math.max(1, parent.clientWidth), height = Math.max(1, parent.clientHeight);
        this.renderer.setSize(width, height, false);
        this.composer.setSize(width, height);
        this.camera.aspect = width / height;
        this.camera.updateProjectionMatrix();
        this.width = width;
        this.height = height;
        this.dirty = true;
    }

    setTime(utcIso, sun, earth) {
        this.time = new Date(utcIso);
        this.sunVector = me(sun[0], sun[1], sun[2]);
        this.earthVector = me(earth[0], earth[1], earth[2]);
        this.applyTime();
    }

    applyTime() {
        if (this.sunVector) this.sunDir.copy(this.sunVector).normalize();
        if (this.earthVector) this.earthDir.copy(this.earthVector).normalize();
        let earthPos = this.earthDir.clone().multiplyScalar(EARTH_SCENE_DIST);
        if (this.surface && this.earthVector) {
            const eye = this.surface.eye;
            earthPos = eye.clone().addScaledVector(this.earthVector.clone().sub(eye).normalize(), EARTH_SCENE_DIST);
        }
        this.earth.position.copy(earthPos);
        const sunFromEarth = this.sunVector ? this.sunVector.clone().sub(this.earthVector || new THREE.Vector3()).normalize() : this.sunDir.clone();
        this.earthUniforms.sunDirection.value = sunFromEarth;
        this.earth.children[1].material.uniforms.sunDirection.value = sunFromEarth;

        const j2me = moonFrameFromJ2000(this.time);
        const spin = new THREE.Matrix3().set(Math.cos(gmstRad(this.time)), -Math.sin(gmstRad(this.time)), 0, Math.sin(gmstRad(this.time)), Math.cos(gmstRad(this.time)), 0, 0, 0, 1);
        this.earthBody.matrix.copy(toThreeMatrix(j2me.clone().multiply(spin)));
        this.earthBody.matrixWorldNeedsUpdate = true;
        if (this.starSphere) {
            this.starSphere.matrix.copy(toThreeMatrix(j2me)).multiply(new THREE.Matrix4().makeScale(1, 1, -1));
            this.starSphere.matrixWorldNeedsUpdate = true;
        }

        const sunPos = this.surface ? this.surface.eye.clone().addScaledVector(this.sunDir, SUN_SCENE_DIST) : this.sunDir.clone().multiplyScalar(SUN_SCENE_DIST);
        this.sunDisk.position.copy(sunPos);
        this.sunGlow.position.copy(sunPos);
        const target = this.shadowTarget();
        this.sun.target.position.copy(target);
        this.sun.position.copy(target.clone().addScaledVector(this.sunDir, 4000));
        if (this.starSphere) this.starSphere.material.color.setScalar(this.surface && this.sunAboveHorizon() ? 0.2 : 0.61);
        this.renderer.toneMappingExposure = this.surface && !this.sunAboveHorizon() ? 2.6 : 1.15;
        this.sun.target.updateMatrixWorld();
        this.earthshine.position.copy(this.earthDir.clone().multiplyScalar(4000));
        const phase = (1 + sunFromEarth.dot(this.earthDir.clone().negate())) / 2;
        this.earthshine.intensity = 0.03 + 0.42 * phase;
        this.earthPhase = phase;
        this.dirty = true;
    }

    setSites(sites) {
        this.sites = sites.map((s) => ({ ...s, position: fromLatLon(s.lat, s.lon, R + 0.3) }));
        this.overlay.querySelectorAll(".scene-site").forEach((el) => el.remove());
        this.siteElements.clear();
        for (const site of this.sites) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "scene-site" + (site.kind ? " " + site.kind : "");
            button.innerHTML = '<span class="dot"></span><span class="name"></span>';
            button.querySelector(".name").textContent = site.name;
            button.addEventListener("click", (event) => { event.stopPropagation(); this.callbacks.site(site.id); });
            button.addEventListener("pointerdown", (event) => event.stopPropagation());
            this.overlay.appendChild(button);
            this.siteElements.set(site.id, button);
        }
        this.updateSelectionClasses();
        this.dirty = true;
    }

    setStations(stations) {
        if (!this.stationElements) this.stationElements = new Map();
        const r = this.earthRadius * 1.01;
        this.stations = (stations || []).map((s) => {
            let el = this.stationElements.get(s.id);
            if (!el) {
                el = document.createElement("div");
                el.className = "scene-station";
                el.innerHTML = '<span class="dot"></span><span class="name"></span>';
                this.overlay.appendChild(el);
                this.stationElements.set(s.id, el);
            }
            el.querySelector(".name").textContent = s.name;
            el.title = s.name;
            el.classList.toggle("up", !!s.up);
            const lat = s.lat * DEG, lon = s.lon * DEG;
            return { id: s.id, local: new THREE.Vector3(Math.cos(lat) * Math.cos(lon) * r, Math.sin(lat) * r, -Math.cos(lat) * Math.sin(lon) * r) };
        });
        this.dirty = true;
    }

    updateStations() {
        if (!this.stations || !this.stations.length || !this.earthBody) return;
        const hideAll = () => { for (const s of this.stations) this.stationElements.get(s.id).style.display = "none"; };
        if (this.surface) { hideAll(); return; }
        this.earth.updateMatrixWorld(true);
        const center = this.earth.position;
        const edge = this.project(center.clone().addScaledVector(new THREE.Vector3(0, 1, 0).applyQuaternion(this.camera.quaternion), this.earthRadius));
        const mid = this.project(center);
        const screenR = Math.hypot(edge.x - mid.x, edge.y - mid.y);
        if (mid.z >= 1 || screenR < 5) { hideAll(); return; }
        const cam = this.camera.position;
        for (const s of this.stations) {
            const el = this.stationElements.get(s.id);
            const world = s.local.clone().applyMatrix4(this.earthBody.matrixWorld);
            const normal = world.clone().sub(center).normalize();
            const facing = cam.clone().sub(world).normalize().dot(normal) > 0.05;
            const ray = world.clone().sub(cam);
            const t = Math.max(0, Math.min(1, -cam.dot(ray) / ray.lengthSq()));
            const hidden = cam.clone().addScaledVector(ray, t).length() < R;
            if (!facing || hidden) { el.style.display = "none"; continue; }
            const p = this.project(world);
            if (p.z >= 1 || p.x < -40 || p.x > this.width + 40 || p.y < -40 || p.y > this.height + 40) { el.style.display = "none"; continue; }
            el.style.display = "";
            el.classList.toggle("named", screenR > 40);
            el.style.transform = `translate(${p.x.toFixed(1)}px, ${p.y.toFixed(1)}px)`;
        }
    }

    async setRegions(regions, visible) {
        this.regions = regions || [];
        this.regionsVisible = !!visible;
        if (this.polePromise) await this.polePromise;
        if (this.regionPoints) {
            this.scene.remove(this.regionPoints);
            this.regionPoints.geometry.dispose();
            this.regionPoints = null;
        }
        this.overlay.querySelectorAll(".scene-region").forEach((el) => el.remove());
        this.regionElements = new Map();
        const positions = [], colors = [];
        const lift = (lat, lon) => {
            const h = this.poleHeightAt(lat, lon);
            return R + (h ? h.meters / 1000 : 0) + 1.2;
        };
        for (const region of this.regions) {
            const c = region.artemis3 ? [0.49, 0.88, 0.76] : [0.55, 0.62, 0.66];
            for (const site of region.sites) {
                const p = fromLatLon(site.lat, site.lon, lift(site.lat, site.lon));
                positions.push(p.x, p.y, p.z);
                colors.push(...c);
            }
            region.position = fromLatLon(region.lat, region.lon, lift(region.lat, region.lon));
            const button = document.createElement("button");
            button.type = "button";
            button.className = "scene-region" + (region.artemis3 ? " current" : "");
            button.textContent = region.name;
            button.addEventListener("click", (event) => { event.stopPropagation(); this.callbacks.region && this.callbacks.region(region.code); });
            button.addEventListener("pointerdown", (event) => event.stopPropagation());
            this.overlay.appendChild(button);
            this.regionElements.set(region.code, button);
        }
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
        geometry.setAttribute("color", new THREE.Float32BufferAttribute(colors, 3));
        const dot = radialTexture([[0, "rgba(255,255,255,1)"], [0.35, "rgba(255,255,255,0.95)"], [0.5, "rgba(255,255,255,0.25)"], [1, "rgba(255,255,255,0)"]], 64);
        this.regionPoints = new THREE.Points(geometry, new THREE.PointsMaterial({ size: 9, sizeAttenuation: false, vertexColors: true, map: dot, transparent: true, depthWrite: false, toneMapped: false }));
        this.regionPoints.visible = this.regionsVisible && !this.surface;
        this.scene.add(this.regionPoints);
        this.dirty = true;
    }

    showRegions(visible) {
        this.regionsVisible = !!visible;
        if (this.regionPoints) this.regionPoints.visible = this.regionsVisible && !this.surface;
        this.dirty = true;
    }

    setSelected(id, lat, lon) {
        this.selected = { id, lat, lon, position: fromLatLon(lat, lon, R + 0.3) };
        if (!this.customMarker) {
            this.customMarker = document.createElement("div");
            this.customMarker.className = "scene-custom";
            this.overlay.appendChild(this.customMarker);
        }
        this.updateSelectionClasses();
        this.dirty = true;
    }

    updateSelectionClasses() {
        for (const [id, el] of this.siteElements) el.classList.toggle("selected", !!this.selected && this.selected.id === id);
    }

    flyTo(lat, lon, range, tilt, heading, duration) {
        const from = { ...this.view };
        const to = { lat, lon, range: range ?? this.view.range, tilt: tilt ?? this.view.tilt, heading: heading ?? this.view.heading };
        const a = fromLatLon(from.lat, from.lon, 1), b = fromLatLon(to.lat, to.lon, 1);
        const angle = a.angleTo(b);
        const peak = Math.max(from.range, to.range, Math.min(9000, R * angle * 1.6));
        this.flight = { from, to, a, b, angle, peak, start: performance.now(), duration: duration ?? Math.min(4200, 1400 + angle * 1600 + Math.abs(Math.log(to.range / from.range)) * 300) };
        this.velocity.x = this.velocity.y = 0;
        this.dirty = true;
    }

    stepFlight(now) {
        const f = this.flight;
        const t = Math.min(1, (now - f.start) / f.duration);
        const k = easeInOut(t);
        const dir = f.angle > 1e-6 ? f.a.clone().multiplyScalar(Math.sin((1 - k) * f.angle) / Math.sin(f.angle)).add(f.b.clone().multiplyScalar(Math.sin(k * f.angle) / Math.sin(f.angle))) : f.b.clone();
        const ll = toLatLon(dir);
        const logR = Math.log(f.from.range) * (1 - k) + Math.log(f.to.range) * k;
        const lift = Math.sin(Math.PI * k) * Math.max(0, Math.log(f.peak) - Math.max(Math.log(f.from.range), Math.log(f.to.range)));
        let dh = ((f.to.heading - f.from.heading + 540) % 360) - 180;
        this.view = { lat: ll.lat, lon: ll.lon, range: Math.exp(logR + lift), tilt: f.from.tilt + (f.to.tilt - f.from.tilt) * k, heading: f.from.heading + dh * k };
        if (t >= 1) this.flight = null;
        this.dirty = true;
    }

    autoTilt(range) {
        if (range > 2500) return 0;
        if (range < 150) return 72;
        return 72 * (1 - (Math.log(range) - Math.log(150)) / (Math.log(2500) - Math.log(150)));
    }

    updateCamera() {
        const v = this.view;
        const { up, east, north } = localFrame(v.lat, v.lon);
        const fine = !!this.terrain && this.terrain.covers(v.lat, v.lon);
        if (v.range < MIN_RANGE && !fine) v.range = MIN_RANGE;
        const ground = fine ? this.terrain.heightAt(v.lat, v.lon) ?? (this.poleHeightAt(v.lat, v.lon)?.meters ?? 0) / 1000 : 0;
        const target = up.clone().multiplyScalar(R + ground);
        const h = v.heading * DEG, tilt = v.tilt * DEG;
        const forward = north.clone().multiplyScalar(Math.cos(h)).addScaledVector(east, Math.sin(h));
        const back = up.clone().multiplyScalar(Math.cos(tilt)).addScaledVector(forward, -Math.sin(tilt));
        const aspect = this.camera.aspect || 1;
        const portrait = aspect < 1 ? 1 + (Math.pow(1 / aspect, 0.85) - 1) * Math.min(1, Math.max(0, (v.range - 1500) / 2000)) : 1;
        const position = target.clone().addScaledVector(back, v.range * portrait);
        let minRadius = R + (v.lat < -74 ? 9 : 2.5);
        const reliefOn = !!this.relief && v.range < (this.reliefOn ? RELIEF_RANGE * 1.15 : RELIEF_RANGE);
        this.reliefOn = reliefOn;
        if (reliefOn) {
            const ll = toLatLon(position);
            const ground = this.relief.heightAt(ll.lat, ll.lon);
            if (ground !== null) this.reliefGround = { lat: ll.lat, lon: ll.lon, h: ground };
            const g = this.reliefGround;
            if (g && Math.abs(g.lat - ll.lat) < 1 && Math.abs(g.lon - ll.lon) < 1) minRadius = Math.max(minRadius, R + g.h + 1.5);
        }
        if (fine) {
            const ll = toLatLon(position);
            const floor = this.terrain.heightAt(ll.lat, ll.lon);
            if (floor !== null) minRadius = R + floor + 0.03;
        }
        if (position.length() < minRadius) position.setLength(minRadius);
        this.camera.position.copy(position);
        const cameraUp = tilt < 0.02 ? forward : up;
        this.camera.up.copy(cameraUp);
        this.camera.lookAt(target);
        const altitude = position.length() - R;
        this.camera.near = Math.max(0.002, altitude * 0.0005);
        this.camera.far = 400000;
        this.camera.fov = v.range > 9000 ? 32 : 40;
        this.camera.updateProjectionMatrix();
        this.camera.updateMatrixWorld();
        if (this.relief && this.relief.update(this.camera, this.height || 800, reliefOn)) this.dirty = true;
        if (this.poleDetail) {
            if (this.poleDetail.update(this.camera, this.height || 800, v.lat < -70 && v.range < 1500)) this.dirty = true;
            this.layerUniforms.detailOn.value = this.poleDetail.maskOn ? 1 : 0;
        }
        if (this.terrain) {
            const active = !this.surface && (!this.layerMode || this.layerMode === "none") && !this.gridOn && this.poleMode !== "photo";
            if (this.terrain.update(this.camera, this.height || 800, v.lat, v.lon, target, active)) this.dirty = true;
        }
        if (this.poleMode === "photo" && this.photoUniform.value > 0.5 && !this.surface) this.updatePhotoFocus(target, v.range);
        const shrink = reliefOn ? (R - 10) / R : 1;
        if (this.moon.scale.x !== shrink) {
            this.moon.scale.setScalar(shrink);
            this.moon.receiveShadow = !reliefOn;
            this.moonMaterial.needsUpdate = true;
        }
        const polarShadow = this.poleReady && v.lat < -55 && v.range < 5000;
        const reliefShadow = (reliefOn && !polarShadow && v.range < 1500) || (!!this.terrain && this.terrain.shown.length > 0);
        if (reliefShadow) this.setReliefShadow(target, v.range);
        else if (this.shadowMode === "relief") this.setPoleShadow();
        const useShadows = polarShadow || reliefShadow;
        if (this.renderer.shadowMap.enabled !== useShadows) {
            this.renderer.shadowMap.enabled = useShadows;
            this.renderer.shadowMap.needsUpdate = true;
            this.scene.traverse((o) => { if (o.material) o.material.needsUpdate = true; });
        }
        if (!this.sun.target.position.equals(this.shadowTarget())) this.applyTime();
    }

    shadowTarget() {
        if (this.surface) return this.surface.origin;
        if (this.shadowMode === "relief" && this.reliefTarget) return this.reliefTarget;
        return this.poleReady && this.view.lat < -60 ? this.poleCenter : new THREE.Vector3();
    }

    sunAboveHorizon() {
        return this.surface ? this.sunDir.dot(this.surface.frame.up) > 0 : true;
    }

    bindInput() {
        const el = this.canvas;
        let dragging = null;
        const pointers = new Map();
        el.addEventListener("pointerdown", (event) => {
            el.setPointerCapture(event.pointerId);
            pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
            dragging = { x: event.clientX, y: event.clientY, startX: event.clientX, startY: event.clientY, t: performance.now(), button: event.button, shift: event.shiftKey || event.ctrlKey, moved: 0 };
            this.flight = null;
            this.velocity.x = this.velocity.y = 0;
        });
        let hoverQueued = null;
        el.addEventListener("pointerleave", () => { if (this.probeEl) this.probeEl.style.display = "none"; });
        el.addEventListener("pointermove", (event) => {
            if (!dragging) {
                if (event.pointerType === "mouse" && ((this.layerMode && this.layerMode !== "none") || this.gridOn)) {
                    if (!hoverQueued) requestAnimationFrame(() => { const q = hoverQueued; hoverQueued = null; this.hover(q.x, q.y); });
                    hoverQueued = { x: event.clientX, y: event.clientY };
                }
                return;
            }
            if (this.probeEl) this.probeEl.style.display = "none";
            const prev = pointers.get(event.pointerId);
            pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
            if (pointers.size === 2) {
                const [p, q] = [...pointers.values()];
                const dist = Math.hypot(p.x - q.x, p.y - q.y);
                if (this.pinch && this.surface) {
                    this.surface.fov = Math.max(1.5, Math.min(80, this.surface.fov * this.pinch / dist));
                    this.dirty = true;
                } else if (this.pinch) this.zoom(this.pinch / dist);
                this.pinch = dist;
                return;
            }
            const dx = event.clientX - dragging.x, dy = event.clientY - dragging.y;
            dragging.x = event.clientX; dragging.y = event.clientY;
            dragging.moved += Math.abs(dx) + Math.abs(dy);
            if (this.surface) {
                const k = this.surface.fov / this.height;
                this.surface.heading = (this.surface.heading - dx * k + 360) % 360;
                this.surface.pitch = Math.max(-40, Math.min(85, this.surface.pitch + dy * k));
                this.dirty = true;
                return;
            }
            if (dragging.button === 2 || dragging.shift) {
                this.view.heading = (this.view.heading - dx * 0.3 + 360) % 360;
                this.view.tilt = Math.max(0, Math.min(85, this.view.tilt + dy * 0.25));
            } else {
                this.dragView(dx, dy);
                const now = performance.now(), dt = Math.max(now - dragging.t, 1);
                this.velocity.x = dx / dt * 16; this.velocity.y = dy / dt * 16;
                dragging.t = now;
            }
            this.dirty = true;
        });
        const end = (event) => {
            pointers.delete(event.pointerId);
            if (pointers.size < 2) this.pinch = null;
            if (!dragging) return;
            if (dragging.moved < 6 && event.type === "pointerup" && dragging.button === 0 && !this.surface) this.pick(event.clientX, event.clientY);
            if (performance.now() - dragging.t > 80) this.velocity.x = this.velocity.y = 0;
            if (pointers.size === 0) dragging = null;
        };
        el.addEventListener("pointerup", end);
        el.addEventListener("pointercancel", end);
        el.addEventListener("contextmenu", (event) => event.preventDefault());
        el.addEventListener("wheel", (event) => {
            event.preventDefault();
            this.flight = null;
            if (this.surface) {
                this.surface.fov = Math.max(1.5, Math.min(80, this.surface.fov * Math.exp(event.deltaY * 0.0012)));
                this.dirty = true;
                return;
            }
            this.zoom(Math.exp(event.deltaY * 0.0015));
        }, { passive: false });
        el.addEventListener("keydown", (event) => {
            if (this.surface) {
                const turn = { ArrowLeft: [-5, 0], ArrowRight: [5, 0], ArrowUp: [0, 3], ArrowDown: [0, -3] }[event.key];
                if (turn) {
                    this.surface.heading = (this.surface.heading + turn[0] + 360) % 360;
                    this.surface.pitch = Math.max(-40, Math.min(85, this.surface.pitch + turn[1]));
                    event.preventDefault();
                }
                if (event.key === "+" || event.key === "=") this.surface.fov = Math.max(1.5, this.surface.fov * 0.8);
                if (event.key === "-") this.surface.fov = Math.min(80, this.surface.fov * 1.25);
                this.dirty = true;
                return;
            }
            const step = 4 * Math.min(1, this.view.range / 4000);
            const keys = { ArrowLeft: [0, -step], ArrowRight: [0, step], ArrowUp: [step, 0], ArrowDown: [-step, 0] };
            if (keys[event.key]) { this.moveView(...keys[event.key]); event.preventDefault(); }
            if (event.key === "+" || event.key === "=") this.zoom(0.8);
            if (event.key === "-") this.zoom(1.25);
            this.dirty = true;
        });
    }

    dragView(dx, dy) {
        const altitude = Math.max(this.view.range * Math.cos(this.view.tilt * DEG), 5);
        const radPerPx = Math.min(0.5 * DEG, (altitude / R) * (this.camera.fov * DEG) / this.height);
        const { up, east, north } = localFrame(this.view.lat, this.view.lon);
        const h = this.view.heading * DEG;
        const forward = north.clone().multiplyScalar(Math.cos(h)).addScaledVector(east, Math.sin(h));
        const right = forward.clone().cross(up);
        const move = forward.clone().multiplyScalar(dy).addScaledVector(right, -dx);
        const length = move.length();
        if (length < 1e-9) return;
        const axis = up.clone().cross(move).normalize();
        const angle = length * radPerPx;
        const p = up.clone().applyAxisAngle(axis, angle);
        const f = forward.clone().applyAxisAngle(axis, angle);
        const ll = toLatLon(p);
        const lat = Math.max(-89.995, Math.min(89.995, ll.lat));
        const frame = localFrame(lat, ll.lon);
        this.view.lat = lat;
        this.view.lon = ll.lon;
        this.view.heading = (Math.atan2(f.dot(frame.east), f.dot(frame.north)) / DEG + 360) % 360;
    }

    moveView(dLat, dLon) {
        let lat = this.view.lat + dLat, lon = this.view.lon + dLon;
        if (lat > 89.9) lat = 89.9;
        if (lat < -89.9) lat = -89.9;
        lon = ((lon + 540) % 360) - 180;
        this.view.lat = lat; this.view.lon = lon;
    }

    zoom(factor) {
        const before = this.view.range;
        const min = this.terrain && this.terrain.covers(this.view.lat, this.view.lon) ? TERRAIN_MIN_RANGE : MIN_RANGE;
        this.view.range = Math.max(min, Math.min(MAX_RANGE, before * factor));
        if (!this.manualTilt) this.view.tilt = this.autoTilt(this.view.range);
        this.dirty = true;
    }

    pick(clientX, clientY) {
        const rect = this.canvas.getBoundingClientRect();
        const ndc = new THREE.Vector2((clientX - rect.left) / rect.width * 2 - 1, -((clientY - rect.top) / rect.height) * 2 + 1);
        const raycaster = new THREE.Raycaster();
        raycaster.setFromCamera(ndc, this.camera);
        const fine = this.terrain ? raycaster.intersectObjects(this.terrain.meshes(), false) : [];
        const targets = this.pole ? [this.pole] : [];
        const hits = fine.length ? fine : targets.length ? raycaster.intersectObjects(targets, false) : [];
        let point = hits.length ? hits[0].point : null;
        if (!point) {
            const o = raycaster.ray.origin, d = raycaster.ray.direction;
            const b = o.dot(d), c = o.lengthSq() - R * R, disc = b * b - c;
            if (disc < 0) return;
            const t = -b - Math.sqrt(disc);
            if (t < 0) return;
            point = o.clone().addScaledVector(d, t);
        }
        const ll = toLatLon(point);
        if (this.routeOn) {
            this.addWaypoint(ll.lat, ll.lon);
            return;
        }
        this.callbacks.point(ll.lat, ll.lon);
    }

    async setRouteMode(on) {
        if (this.polePromise) await this.polePromise;
        if (on) {
            try { await this.loadLayers(true); } catch (error) { console.warn("pole layers", error); return false; }
        }
        this.routeOn = !!on;
        if (!this.route) this.route = { points: [], samples: [] };
        this.dirty = true;
        return true;
    }

    addWaypoint(latDeg, lonDeg) {
        const h = this.poleHeightAt(latDeg, lonDeg);
        if (!h || latDeg > -80.2) return;
        this.route.points.push({ x: h.x, y: h.y });
        this.rebuildRoute();
    }

    undoWaypoint() {
        if (!this.route || !this.route.points.length) return;
        this.route.points.pop();
        this.rebuildRoute();
    }

    clearRoute() {
        if (!this.route) return;
        this.route.points = [];
        this.rebuildRoute();
    }

    rebuildRoute() {
        const pts = this.route.points;
        const samples = [];
        let total = 0;
        for (let i = 1; i < pts.length; i++) total += Math.hypot(pts[i].x - pts[i - 1].x, pts[i].y - pts[i - 1].y);
        const step = Math.max(100, total / 500);
        const add = (x, y, d, waypoint) => {
            const rho = Math.hypot(x, y) / 1000;
            const lat = -90 + 2 * Math.atan(rho / (2 * R)) / DEG;
            const lon = Math.atan2(x, y) / DEG;
            const h = this.poleHeightAt(lat, lon);
            const v = this.probeAt(lat, lon);
            if (!h) return;
            samples.push({ lat, lon, d, h: h.meters, waypoint, sun: v ? v.sun : 0, earth: v ? v.earth : 0, dark: v ? v.dark : false, slope: v && v.slope !== null ? v.slope : 0, position: fromLatLon(lat, lon, R + h.meters / 1000 + 0.02) });
        };
        let d = 0;
        if (pts.length) add(pts[0].x, pts[0].y, 0, true);
        for (let i = 1; i < pts.length; i++) {
            const a = pts[i - 1], b = pts[i];
            const len = Math.hypot(b.x - a.x, b.y - a.y);
            const n = Math.max(1, Math.ceil(len / step));
            for (let k = 1; k <= n; k++) add(a.x + (b.x - a.x) * k / n, a.y + (b.y - a.y) * k / n, d + len * k / n, k === n);
            d += len;
        }
        this.route.samples = samples;
        this.dirty = true;
        this.callbacks.route && this.callbacks.route({
            count: pts.length,
            distanceKm: samples.map((p) => +(p.d / 1000).toFixed(3)),
            elevationM: samples.map((p) => Math.round(p.h)),
            slopeDeg: samples.map((p) => +p.slope.toFixed(1)),
            sun: samples.map((p) => +p.sun.toFixed(3)),
            earth: samples.map((p) => +p.earth.toFixed(3)),
            dark: samples.map((p) => p.dark),
            waypoint: samples.map((p) => p.waypoint),
            points: samples.filter((p) => p.waypoint).map((p) => [+p.lat.toFixed(5), +p.lon.toFixed(5)])
        });
    }

    drawRoute() {
        const samples = this.route ? this.route.samples : [];
        const show = samples.length > 0 && !this.surface;
        if (!this.routeEl) {
            if (!show) return;
            this.routeEl = document.createElementNS("http://www.w3.org/2000/svg", "svg");
            this.routeEl.setAttribute("class", "scene-route");
            this.overlay.appendChild(this.routeEl);
        }
        this.routeEl.style.display = show ? "" : "none";
        if (!show) return;
        this.routeEl.setAttribute("viewBox", `0 0 ${this.width} ${this.height}`);
        const colorOf = (p) => p.slope >= 15 ? "#e0453a" : p.slope >= 10 ? "#f0b43c" : "#7ee08a";
        let body = "", glow = "", dots = "";
        let prev = null;
        for (const p of samples) {
            const q = this.visible(p.position) ? this.project(p.position) : null;
            if (q && prev) {
                const seg = `M${prev.x.toFixed(1)} ${prev.y.toFixed(1)}L${q.x.toFixed(1)} ${q.y.toFixed(1)}`;
                glow += seg;
                body += `<path d="${seg}" stroke="${colorOf(p)}"/>`;
            }
            if (q && p.waypoint) dots += `<circle cx="${q.x.toFixed(1)}" cy="${q.y.toFixed(1)}" r="5"/>`;
            prev = q;
        }
        this.routeEl.innerHTML = `<path class="halo" d="${glow}"/><g class="body">${body}</g><g class="dots">${dots}</g>`;
    }

    project(position) {
        const p = position.clone().project(this.camera);
        return { x: (p.x + 1) / 2 * this.width, y: (1 - p.y) / 2 * this.height, z: p.z };
    }

    visible(position) {
        const toCamera = this.camera.position.clone().sub(position);
        const normal = position.clone().normalize();
        return toCamera.dot(normal) > 0 && toCamera.normalize().dot(normal) > 0.02;
    }

    surfaceHit(px, py) {
        const ndc = new THREE.Vector2(px / this.width * 2 - 1, -(py / this.height) * 2 + 1);
        this.scaleRay = this.scaleRay || new THREE.Raycaster();
        this.scaleRay.setFromCamera(ndc, this.camera);
        const o = this.scaleRay.ray.origin, d = this.scaleRay.ray.direction;
        const b = o.dot(d), c = o.lengthSq() - R * R, disc = b * b - c;
        if (disc < 0) return null;
        const t = -b - Math.sqrt(disc);
        return t < 0 ? null : o.clone().addScaledVector(d, t);
    }

    setUnits(units) {
        this.units = Object.assign({ m: "m", km: "km" }, units || {});
        this.scaleKey = null;
        this.viewKey = null;
        this.dirty = true;
    }

    updateScale() {
        if (!this.scaleEl) {
            this.scaleEl = document.createElement("div");
            this.scaleEl.className = "scene-scale";
            this.scaleEl.setAttribute("aria-hidden", "true");
            this.scaleEl.innerHTML = '<span class="scale-label"></span><span class="scale-bar"></span>';
            this.overlay.appendChild(this.scaleEl);
        }
        const cx = this.width / 2, cy = this.height / 2;
        let kmPerPx = null;
        const a = this.surfaceHit(cx - 50, cy), b = this.surfaceHit(cx + 50, cy);
        if (a && b) {
            kmPerPx = R * a.angleTo(b) / 100;
        } else {
            const centre = this.surfaceHit(cx, cy);
            if (centre) kmPerPx = centre.distanceTo(this.camera.position) * 2 * Math.tan(this.camera.fov * DEG / 2) / this.height;
        }
        if (this.surface || !kmPerPx || !isFinite(kmPerPx)) {
            this.scaleEl.style.display = "none";
            return;
        }
        const maxKm = kmPerPx * 120;
        const power = Math.pow(10, Math.floor(Math.log10(maxKm)));
        const length = [5, 2, 1].map((k) => k * power).find((v) => v <= maxKm) || power;
        const width = Math.max(1, length / kmPerPx);
        const u = this.units || { m: "m", km: "km" };
        const label = length >= 1 ? `${+length.toPrecision(3)} ${u.km}` : `${Math.round(length * 1000)} ${u.m}`;
        const key = label + "|" + width.toFixed(0);
        this.scaleEl.style.display = "";
        if (key === this.scaleKey) return;
        this.scaleKey = key;
        this.scaleEl.querySelector(".scale-label").textContent = label;
        this.scaleEl.querySelector(".scale-bar").style.width = width.toFixed(0) + "px";
    }

    updateViewControl() {
        if (!this.poleGrid) return;
        if (!this.viewEl) {
            const el = document.createElement("div");
            el.className = "scene-view";
            el.setAttribute("role", "group");
            for (const mode of ["relief", "photo", "zone"]) {
                const button = document.createElement("button");
                button.type = "button";
                button.dataset.mode = mode;
                button.addEventListener("pointerdown", (event) => event.stopPropagation());
                button.addEventListener("click", (event) => {
                    event.stopPropagation();
                    if (mode === "zone") this.setZone(!this.zoneOn);
                    else this.setPoleMode(mode);
                    this.viewKey = null;
                    this.dirty = true;
                });
                el.appendChild(button);
            }
            this.overlay.appendChild(el);
            this.viewEl = el;
        }
        const show = !this.surface && this.poleReady && this.view.lat < -60 && this.view.range < 6000;
        this.viewEl.style.display = show ? "" : "none";
        this.updateZoneLabels(show && this.zoneOn);
        if (!show) return;
        const u = this.units || {};
        const mode = this.poleMode || "relief";
        const key = mode + "|" + !!this.zoneOn + "|" + (u.polePhoto || "");
        if (key === this.viewKey) return;
        this.viewKey = key;
        this.viewEl.setAttribute("aria-label", u.poleTitle || "Surface view");
        const text = { relief: [u.poleRelief || "Relief", u.poleReliefHint || ""], photo: [u.polePhoto || "Photo", u.polePhotoHint || ""], zone: [u.poleZone || "Zone", u.poleZoneHint || ""] };
        for (const button of this.viewEl.querySelectorAll("button")) {
            const m = button.dataset.mode;
            button.textContent = text[m][0];
            button.title = text[m][1];
            button.setAttribute("aria-pressed", String(m === "zone" ? !!this.zoneOn : m === mode));
        }
    }

    setZone(on) {
        this.zoneOn = !!on;
        if (this.poleGrid) {
            const radius = (lat) => 2 * R * 1000 * Math.tan((90 + lat) * DEG / 2) / (2 * this.poleGrid.half);
            this.layerUniforms.zoneR.value.set(radius(ZONES[0].lat), radius(ZONES[1].lat));
        }
        this.layerUniforms.zoneOn.value = this.zoneOn ? 1 : 0;
        this.dirty = true;
    }

    updateZoneLabels(show) {
        if (!this.zoneEls) {
            this.zoneEls = ZONES.map((zone) => {
                const el = document.createElement("div");
                el.className = "scene-zone";
                el.style.display = "none";
                this.overlay.appendChild(el);
                return el;
            });
        }
        const u = this.units || {};
        const lon = toLatLon(this.camera.position).lon;
        ZONES.forEach((zone, i) => {
            const el = this.zoneEls[i];
            const position = fromLatLon(zone.lat, lon, R + 0.5);
            const p = this.project(position);
            const visible = show && this.visible(position) && p.z < 1 && p.x > 0 && p.x < this.width && p.y > 0 && p.y < this.height;
            el.style.display = visible ? "" : "none";
            if (!visible) return;
            const diameter = Math.round(2 * R * (90 + zone.lat) * DEG);
            const label = (u[zone.key] || `${-zone.lat}° S · Ø {0} km`).replace("{0}", diameter);
            if (el.textContent !== label) el.textContent = label;
            el.style.transform = `translate(${p.x.toFixed(1)}px, ${p.y.toFixed(1)}px) translate(-50%, -130%)`;
        });
    }

    async wacTexture(level, half, size) {
        const tileM = 2 * WAC_ORIGIN_M / 2 ** level, scale = size / (2 * half);
        const canvas = document.createElement("canvas");
        canvas.width = size;
        canvas.height = size;
        const ctx = canvas.getContext("2d");
        const first = Math.max(0, Math.floor((WAC_ORIGIN_M - half) / tileM)), last = Math.min(2 ** level - 1, Math.floor((WAC_ORIGIN_M + half) / tileM));
        const jobs = [];
        for (let row = first; row <= last; row++) {
            for (let col = first; col <= last; col++) {
                jobs.push(new Promise((resolve) => {
                    const img = new Image();
                    img.crossOrigin = "anonymous";
                    img.onload = () => {
                        const x = (-WAC_ORIGIN_M + col * tileM + half) * scale, y = (half - (WAC_ORIGIN_M - row * tileM)) * scale, s = tileM * scale;
                        ctx.drawImage(img, x, y, s + 1, s + 1);
                        resolve(true);
                    };
                    img.onerror = () => resolve(false);
                    img.src = `${WAC_TILES}${level}/${row}/${col}.png`;
                }));
            }
        }
        if (!(await Promise.all(jobs)).some((ok) => ok)) throw new Error("wac tiles");
        const texture = new THREE.CanvasTexture(canvas);
        texture.colorSpace = THREE.SRGBColorSpace;
        texture.anisotropy = Math.min(8, this.renderer.capabilities.getMaxAnisotropy());
        return texture;
    }

    async trekTexture(root, level, col0, row0, span) {
        const canvas = document.createElement("canvas");
        canvas.width = span * 256;
        canvas.height = span * 256;
        const ctx = canvas.getContext("2d");
        const jobs = [];
        for (let r = 0; r < span; r++) {
            for (let c = 0; c < span; c++) {
                jobs.push(new Promise((resolve) => {
                    const img = new Image();
                    img.crossOrigin = "anonymous";
                    img.onload = () => {
                        ctx.drawImage(img, c * 256, r * 256);
                        resolve(true);
                    };
                    img.onerror = () => resolve(false);
                    img.src = `${root}${level}/${row0 + r}/${col0 + c}.png`;
                }));
            }
        }
        if (!(await Promise.all(jobs)).some((ok) => ok)) throw new Error("trek tiles");
        const texture = new THREE.CanvasTexture(canvas);
        texture.colorSpace = THREE.SRGBColorSpace;
        texture.anisotropy = Math.min(8, this.renderer.capabilities.getMaxAnisotropy());
        const tileM = 2 * WAC_ORIGIN_M / 2 ** level, half = this.poleGrid.half, size = span * tileM;
        const x0 = -WAC_ORIGIN_M + col0 * tileM, y1 = WAC_ORIGIN_M - row0 * tileM;
        const rect = new THREE.Vector4((x0 + half) / (2 * half), (y1 - size + half) / (2 * half), (x0 + size + half) / (2 * half), (y1 + half) / (2 * half));
        return { texture, rect };
    }

    loadPolePhoto() {
        if (!this.polePhotoJob) {
            this.polePhotoJob = (async () => {
                const tileM = 2 * WAC_ORIGIN_M / 2 ** NAC_BASE_LEVEL;
                const first = Math.floor((WAC_ORIGIN_M - NAC_EDGE_M) / tileM), span = Math.floor((WAC_ORIGIN_M + NAC_EDGE_M) / tileM) - first + 1;
                const [inner, outer, nac] = await Promise.all([
                    this.wacTexture(WAC_LEVEL, this.poleGrid.half, WAC_SIZE),
                    this.wacTexture(WAC_LEVEL - 1, WAC_CAP_M, 2 * WAC_SIZE),
                    this.trekTexture(NAC_TILES, NAC_BASE_LEVEL, first, first, span).catch((error) => { console.warn("nac", error); return null; })
                ]);
                this.photoMap.value = outer;
                if (nac) {
                    this.layerUniforms.nacMap.value = nac.texture;
                    this.layerUniforms.nacRect.value.copy(nac.rect);
                }
                this.layerUniforms.nacScale.value = 2 * this.poleGrid.half / NAC_EDGE_M;
                return inner;
            })();
            this.polePhotoJob.catch(() => { this.polePhotoJob = null; });
        }
        return this.polePhotoJob;
    }

    updatePhotoFocus(target, range) {
        const ll = toLatLon(target);
        const rho = 2 * R * 1000 * Math.tan((90 + ll.lat) * DEG / 2);
        const x = rho * Math.sin(ll.lon * DEG), y = rho * Math.cos(ll.lon * DEG);
        const mpp = range * 1000 * 2 * Math.tan(this.camera.fov * DEG / 2) / (this.height || 800);
        const level = Math.max(NAC_BASE_LEVEL, Math.min(NAC_MAX_LEVEL, Math.round(Math.log2(2 * WAC_ORIGIN_M / 256 / Math.max(mpp, 1)))));
        const tileM = 2 * WAC_ORIGIN_M / 2 ** level;
        const wanted = level > NAC_BASE_LEVEL && rho < NAC_EDGE_M + NAC_SPAN * tileM / 2;
        const focus = this.photoFocus;
        if (!wanted) {
            if (this.layerUniforms.focusOn.value) {
                this.layerUniforms.focusOn.value = 0;
                this.dirty = true;
            }
            return;
        }
        const col = Math.floor((x + WAC_ORIGIN_M) / tileM), row = Math.floor((WAC_ORIGIN_M - y) / tileM);
        if (focus && focus.level === level && Math.abs(focus.col - col) <= 2 && Math.abs(focus.row - row) <= 2) {
            if (!this.layerUniforms.focusOn.value) {
                this.layerUniforms.focusOn.value = 1;
                this.dirty = true;
            }
            return;
        }
        if (this.photoFocusJob) return;
        const half = Math.floor(NAC_SPAN / 2);
        this.photoFocusJob = this.trekTexture(NAC_TILES, level, col - half, row - half, NAC_SPAN).then(({ texture, rect }) => {
            if (this.disposed) { texture.dispose(); return; }
            if (this.focusTexture) this.focusTexture.dispose();
            this.focusTexture = texture;
            this.layerUniforms.focusMap.value = texture;
            this.layerUniforms.focusRect.value.copy(rect);
            this.layerUniforms.focusOn.value = 1;
            this.photoFocus = { level, col, row };
        }).catch(() => {
            this.photoFocus = { level, col, row };
        }).finally(() => {
            this.photoFocusJob = null;
            this.dirty = true;
        });
    }

    async setPoleMode(mode) {
        this.poleMode = mode === "photo" ? "photo" : "relief";
        if (this.poleMode !== "photo") this.photoUniform.value = 0;
        if (this.pole && !this.poleAlbedo) this.poleAlbedo = this.pole.material.map;
        let map = this.poleAlbedo;
        this.dirty = true;
        if (this.poleMode === "photo" && this.poleGrid) {
            try {
                const photo = await this.loadPolePhoto();
                if (this.poleMode !== "photo" || this.disposed) return;
                map = photo;
            } catch (error) {
                console.warn("pole photo", error);
            }
        }
        for (const material of [this.pole && this.pole.material, this.poleDetail && this.poleDetail.material]) {
            if (material && map) material.map = map;
        }
        this.photoUniform.value = this.poleMode === "photo" && map !== this.poleAlbedo ? 1 : 0;
        this.dirty = true;
    }

    updateOverlay() {
        this.updateScale();
        this.updateViewControl();
        const placed = [];
        const sorted = [...this.sites].sort((a, b) => (this.selected && b.id === this.selected.id) - (this.selected && a.id === this.selected.id));
        for (const site of sorted) {
            const el = this.siteElements.get(site.id);
            if (!el) continue;
            const show = this.visible(site.position);
            const p = this.project(site.position);
            const inside = p.x > -40 && p.x < this.width + 40 && p.y > -40 && p.y < this.height + 40 && p.z < 1;
            if (!show || !inside) { el.style.display = "none"; continue; }
            el.style.display = "";
            el.style.transform = `translate(${p.x.toFixed(1)}px, ${p.y.toFixed(1)}px)`;
            if (!el.labelWidth && !el.classList.contains("quiet")) el.labelWidth = el.offsetWidth;
            const w = el.labelWidth || 90;
            const free = (dy) => !placed.some((q) => p.x < q.x + q.w && q.x < p.x + w && Math.abs(q.y - (p.y + dy)) < 16);
            const selected = this.selected && this.selected.id === site.id;
            const dy = selected ? 0 : LABEL_SHIFTS.find(free);
            const shown = dy !== undefined;
            el.classList.toggle("quiet", !shown);
            el.classList.toggle("below", shown && dy > 0);
            el.classList.toggle("above", shown && dy < 0);
            el.style.setProperty("--label-dy", shown ? dy + "px" : "0px");
            if (shown) placed.push({ x: p.x, y: p.y + dy, w });
        }
        if (this.regionElements) {
            const regionPlaced = [];
            for (const region of this.regions) {
                const el = this.regionElements.get(region.code);
                const show = this.regionsVisible && region.position && this.visible(region.position) && this.view.range < 7000;
                if (!show) { el.style.display = "none"; continue; }
                const p = this.project(region.position);
                const crowded = regionPlaced.some((q) => Math.abs(q.x - p.x) < 120 && Math.abs(q.y - p.y) < 18);
                el.style.display = crowded && this.view.range > 350 ? "none" : "";
                el.style.transform = `translate(${p.x.toFixed(1)}px, ${p.y.toFixed(1)}px)`;
                if (!crowded) regionPlaced.push(p);
            }
        }
        if (this.customMarker) {
            const custom = this.selected && !this.siteElements.has(this.selected.id);
            if (custom && this.visible(this.selected.position)) {
                const p = this.project(this.selected.position);
                this.customMarker.style.display = "";
                this.customMarker.style.transform = `translate(${p.x.toFixed(1)}px, ${p.y.toFixed(1)}px)`;
            } else {
                this.customMarker.style.display = "none";
            }
        }
        this.updateStations();
        const sunScreen = this.project(this.sunDisk.position);
        const earthScreen = this.project(this.earth.position);
        this.callbacks.frame && this.callbacks.frame({ sun: sunScreen, earth: earthScreen, view: this.view });
    }

    loop(now) {
        if (this.disposed) return;
        if (this.flight) this.stepFlight(now);
        if (Math.abs(this.velocity.x || 0) + Math.abs(this.velocity.y || 0) > 0.05) {
            this.dragView(this.velocity.x, this.velocity.y);
            this.velocity.x *= 0.92; this.velocity.y *= 0.92;
            this.dirty = true;
        }
        if (this.surface && this.surface.turn) this.stepTurn(now);
        if (this.recording) this.stepRecording(now);
        if (this.dirty) {
            this.dirty = false;
            if (this.big && !this.colorUpgrade && (this.surface || this.view.range < 1500)) this.upgradeColor();
            if (this.surface) { this.hideRelief(); this.updateSurfaceCamera(); } else this.updateCamera();
            this.sunGlow.scale.setScalar(this.sunGlowBase * Math.max(0.12, Math.min(1, this.camera.fov / 40)));
            this.composer.render();
            if (this.surface) this.updateSurfaceOverlay(); else this.updateOverlay();
            this.drawRoute();
            if (this.recording) this.composeRecording();
        }
        if (this.surface && this.skyFov) this.updateSkyPan(this.surface.heading, this.skyFov);
        requestAnimationFrame(this.loop);
    }

    compose(ctx, w, h, title, lines) {
        const src = this.canvas;
        const k = w / Math.max(1, this.width);
        ctx.textAlign = "left";
        ctx.drawImage(src, 0, 0, w, h);
        const base = src.getBoundingClientRect();
        const font = getComputedStyle(this.canvas).fontFamily || "sans-serif";
        ctx.textBaseline = "middle";
        ctx.shadowColor = "rgba(0,0,0,0.9)";
        ctx.shadowBlur = 6 * k;
        const route = this.route && !this.surface ? this.route.samples : [];
        ctx.lineCap = "round"; ctx.lineJoin = "round";
        let prev = null;
        for (const p of route) {
            const q = this.visible(p.position) ? this.project(p.position) : null;
            if (q && prev) {
                ctx.strokeStyle = "rgba(0,0,0,0.6)"; ctx.lineWidth = 6 * k;
                ctx.beginPath(); ctx.moveTo(prev.x * k, prev.y * k); ctx.lineTo(q.x * k, q.y * k); ctx.stroke();
                ctx.strokeStyle = p.slope >= 15 ? "#e0453a" : p.slope >= 10 ? "#f0b43c" : "#7ee08a"; ctx.lineWidth = 3 * k;
                ctx.beginPath(); ctx.moveTo(prev.x * k, prev.y * k); ctx.lineTo(q.x * k, q.y * k); ctx.stroke();
            }
            if (q && p.waypoint) { ctx.fillStyle = "#ffffff"; ctx.beginPath(); ctx.arc(q.x * k, q.y * k, 5 * k, 0, Math.PI * 2); ctx.fill(); }
            prev = q;
        }
        for (const el of this.overlay.querySelectorAll(".scene-site, .scene-body")) {
            if (el.style.display === "none" || !el.offsetParent) continue;
            const r = el.getBoundingClientRect();
            if (r.right < base.left || r.left > base.right || r.bottom < base.top || r.top > base.bottom) continue;
            const parts = [...el.querySelectorAll(".name, .value")].filter((part) => getComputedStyle(part).display !== "none").map((part) => part.textContent.trim());
            const text = parts.join(" ");
            const dot = el.querySelector(".dot");
            if (dot) {
                const d = dot.getBoundingClientRect();
                ctx.fillStyle = el.classList.contains("selected") ? "#f2b84b" : "#ffffff";
                ctx.beginPath();
                ctx.arc((d.left + d.width / 2 - base.left) * k, (d.top + d.height / 2 - base.top) * k, 4.5 * k, 0, Math.PI * 2);
                ctx.fill();
            }
            if (!text) continue;
            ctx.fillStyle = el.classList.contains("scene-body") ? "#ffe3a8" : "#ffffff";
            ctx.font = `${13 * k}px ${font}`;
            ctx.fillText(text, (r.left - base.left + (dot ? 16 : 0)) * k, (r.top + r.height / 2 - base.top) * k);
        }
        ctx.shadowBlur = 0;
        const pad = 20 * k, lineH = 20 * k, boxH = pad * 2 + 28 * k + lines.length * lineH;
        const grad = ctx.createLinearGradient(0, h - boxH * 1.6, 0, h);
        grad.addColorStop(0, "rgba(0,0,0,0)");
        grad.addColorStop(1, "rgba(0,0,0,0.78)");
        ctx.fillStyle = grad;
        ctx.fillRect(0, h - boxH * 1.6, w, boxH * 1.6);
        ctx.textBaseline = "alphabetic";
        ctx.fillStyle = "#ffffff";
        ctx.font = `600 ${24 * k}px ${font}`;
        ctx.fillText(title, pad, h - pad - lines.length * lineH);
        ctx.font = `${14 * k}px ${font}`;
        ctx.fillStyle = "#c9d0dc";
        lines.forEach((line, i) => ctx.fillText(line, pad, h - pad - (lines.length - 1 - i) * lineH));
        ctx.font = `600 ${15 * k}px ${font}`;
        ctx.fillStyle = "#f2b84b";
        ctx.textAlign = "right";
        ctx.fillText("Shadowline", w - pad, h - pad);
        ctx.textAlign = "left";
    }

    renderNow() {
        if (this.surface) { this.hideRelief(); this.updateSurfaceCamera(); } else this.updateCamera();
        this.composer.render();
        if (this.surface) this.updateSurfaceOverlay(); else this.updateOverlay();
    }

    download(blob, fileName) {
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url; a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 4000);
    }

    async snapshot(title, lines, fileName) {
        this.renderNow();
        const out = document.createElement("canvas");
        out.width = this.canvas.width; out.height = this.canvas.height;
        this.compose(out.getContext("2d"), out.width, out.height, title, lines);
        const blob = await new Promise((resolve) => out.toBlob(resolve, "image/png"));
        if (!blob) return false;
        this.download(blob, fileName);
        return true;
    }

    timeLabel() {
        return this.time ? this.time.toISOString().slice(0, 16).replace("T", " ") + " UTC" : "";
    }

    async record(title, lines, fileName, seconds) {
        if (this.recording || typeof MediaRecorder === "undefined") return null;
        const types = ["video/mp4;codecs=avc1.640028", "video/mp4", "video/webm;codecs=vp9", "video/webm"];
        const type = types.find((t) => MediaRecorder.isTypeSupported(t));
        if (!type) return null;
        const scale = Math.min(1, 1920 / this.canvas.width);
        const out = document.createElement("canvas");
        out.width = Math.round(this.canvas.width * scale / 2) * 2;
        out.height = Math.round(this.canvas.height * scale / 2) * 2;
        out.style.cssText = "position:fixed;left:0;top:0;width:2px;height:2px;opacity:0.01;pointer-events:none;z-index:-1";
        document.body.appendChild(out);
        const ctx = out.getContext("2d");
        const stream = out.captureStream(0);
        const track = stream.getVideoTracks()[0];
        const recorder = new MediaRecorder(stream, { mimeType: type, videoBitsPerSecond: 12000000 });
        const chunks = [];
        recorder.ondataavailable = (event) => { if (event.data && event.data.size) chunks.push(event.data); };
        const stopped = new Promise((resolve) => { recorder.onstop = resolve; });
        this.recording = { ctx, track, w: out.width, h: out.height, title, lines, last: performance.now() };
        recorder.start(500);
        this.renderNow();
        this.composeRecording();
        this.dirty = true;
        await new Promise((resolve) => { this.recording.finish = resolve; this.recording.timer = setTimeout(resolve, Math.max(1, seconds) * 1000); });
        clearTimeout(this.recording.timer);
        recorder.stop();
        await stopped;
        stream.getTracks().forEach((track) => track.stop());
        out.remove();
        this.recording = null;
        const blob = new Blob(chunks, { type: type.split(";")[0] });
        const ext = type.startsWith("video/mp4") ? "mp4" : "webm";
        this.download(blob, fileName + "." + ext);
        return ext;
    }

    composeRecording() {
        const r = this.recording;
        this.compose(r.ctx, r.w, r.h, r.title, [...r.lines, this.timeLabel()]);
        if (r.track && r.track.requestFrame) r.track.requestFrame();
    }

    stopRecording() {
        if (this.recording && this.recording.finish) this.recording.finish();
    }

    stepRecording(now) {
        const r = this.recording;
        const dt = Math.min(0.1, (now - r.last) / 1000);
        r.last = now;
        if (this.surface) {
            if (!this.surface.turn) this.surface.heading = (this.surface.heading + 2.5 * dt + 360) % 360;
        } else if (!this.flight) {
            this.view.heading = (this.view.heading + 1.5 * dt + 360) % 360;
        }
        this.dirty = true;
    }

    upgradeColor() {
        this.colorUpgrade = this.texture("moon_color_8k.jpg").then((map) => {
            if (this.disposed) return;
            const old = this.moonMaterial.map;
            this.moonMaterial.map = map;
            this.moonMaterial.needsUpdate = true;
            if (this.surface) for (const o of this.surface.objects) if (o.material && o.material.map === old) { o.material.map = map; o.material.needsUpdate = true; }
            this.dirty = true;
        }).catch(() => null);
    }

    poleHeightAt(latDeg, lonDeg) {
        const g = this.poleGrid;
        if (!g || latDeg > -75) return null;
        const rho = 2 * R * Math.tan((90 + latDeg) * DEG / 2) * 1000;
        const x = rho * Math.sin(lonDeg * DEG), y = rho * Math.cos(lonDeg * DEG);
        const spacing = 2 * g.half / g.n;
        const fj = (x + g.half) / spacing - 0.5, fi = (g.half - y) / spacing - 0.5;
        const j0 = Math.floor(fj), i0 = Math.floor(fi);
        if (j0 < 0 || i0 < 0 || j0 >= g.n - 1 || i0 >= g.n - 1) return null;
        const aj = fj - j0, ai = fi - i0, h = g.heights, n = g.n;
        const v = (h[i0 * n + j0] * (1 - aj) + h[i0 * n + j0 + 1] * aj) * (1 - ai) + (h[(i0 + 1) * n + j0] * (1 - aj) + h[(i0 + 1) * n + j0 + 1] * aj) * ai;
        return { meters: v * g.unit, x, y };
    }

    setPoleHole(x, y, radius) {
        const g = this.poleGrid;
        if (!g) return;
        const n = g.n, spacing = 2 * g.half / n;
        if (radius <= 0) {
            this.pole.geometry.setIndex(new THREE.BufferAttribute(g.index, 1));
            return;
        }
        const index = new Uint32Array((n - 1) * (n - 1) * 6);
        let t = 0;
        for (let i = 0; i < n - 1; i++) {
            const ym = g.half - (i + 1) * spacing;
            for (let j = 0; j < n - 1; j++) {
                const xm = -g.half + (j + 1) * spacing;
                if (Math.hypot(xm - x, ym - y) < radius) continue;
                const a = i * n + j, b = a + 1, c = a + n, d = c + 1;
                index[t++] = a; index[t++] = c; index[t++] = b; index[t++] = b; index[t++] = c; index[t++] = d;
            }
        }
        this.pole.geometry.setIndex(new THREE.BufferAttribute(index.subarray(0, t), 1));
    }

    detailTexture() {
        if (this.detail) return this.detail;
        const N = 512, canvas = document.createElement("canvas");
        canvas.width = canvas.height = N;
        const g = canvas.getContext("2d");
        const height = new Float32Array(N * N);
        let seed = 11;
        const random = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
        for (let o = 0; o < 5; o++) {
            const f = 4 << o, amp = 1 / (1 << o), grid = [];
            for (let a = 0; a < f * f; a++) grid.push(random());
            const at = (a, b) => grid[(b % f) * f + (a % f)];
            for (let y = 0; y < N; y++) {
                for (let x = 0; x < N; x++) {
                    const fx = x / N * f, fy = y / N * f, x0 = fx | 0, y0 = fy | 0;
                    const ux = fx - x0, uy = fy - y0, sx = ux * ux * (3 - 2 * ux), sy = uy * uy * (3 - 2 * uy);
                    height[y * N + x] += amp * ((at(x0, y0) * (1 - sx) + at(x0 + 1, y0) * sx) * (1 - sy) + (at(x0, y0 + 1) * (1 - sx) + at(x0 + 1, y0 + 1) * sx) * sy);
                }
            }
        }
        for (let c = 0; c < 40; c++) {
            const cx = random() * N, cy = random() * N, cr = 3 + Math.pow(random(), 3) * 38;
            for (let y = -cr * 1.5; y < cr * 1.5; y++) {
                for (let x = -cr * 1.5; x < cr * 1.5; x++) {
                    const d = Math.hypot(x, y) / cr;
                    const px = ((cx + x) % N + N) % N | 0, py = ((cy + y) % N + N) % N | 0;
                    height[py * N + px] += d < 1 ? (d * d - 1) * 0.5 * cr / 20 : d < 1.5 ? Math.cos((d - 1) * Math.PI) * 0.12 * cr / 20 * (1.5 - d) * 2 : 0;
                }
            }
        }
        const image = g.createImageData(N, N);
        for (let y = 0; y < N; y++) {
            for (let x = 0; x < N; x++) {
                const hx = height[y * N + (x + 1) % N] - height[y * N + (x + N - 1) % N];
                const hy = height[((y + 1) % N) * N + x] - height[((y + N - 1) % N) * N + x];
                const nx = -hx * 3, ny = hy * 3, l = Math.hypot(nx, ny, 1), k = (y * N + x) * 4;
                image.data[k] = (nx / l * 0.5 + 0.5) * 255;
                image.data[k + 1] = (ny / l * 0.5 + 0.5) * 255;
                image.data[k + 2] = (1 / l * 0.5 + 0.5) * 255;
                image.data[k + 3] = 255;
            }
        }
        g.putImageData(image, 0, 0);
        this.detail = new THREE.CanvasTexture(canvas);
        this.detail.wrapS = this.detail.wrapT = THREE.RepeatWrapping;
        this.detail.anisotropy = Math.min(16, this.renderer.capabilities.getMaxAnisotropy());
        this.detail.channel = 1;
        return this.detail;
    }

    async standAt(siteId, latDeg, lonDeg, eyeMeters, labels, elevationMeters, terrainBytes, horizonValues) {
        this.leaveSurface(true);
        if (this.probeEl) this.probeEl.style.display = "none";
        if (this.polePromise) await this.polePromise;
        const frame = localFrame(latDeg, lonDeg);
        const pole = this.poleHeightAt(latDeg, lonDeg);
        const baseKm = pole ? pole.meters / 1000 : (elevationMeters || 0) / 1000;
        const origin = fromLatLon(latDeg, lonDeg, R + baseKm);
        const surface = { siteId, lat: latDeg, lon: lonDeg, frame, origin, eyeMeters: eyeMeters ?? 50, heading: 0, pitch: 4, fov: 62, objects: [], labels: labels || {} };
        surface.eye = origin.clone().addScaledVector(frame.up, surface.eyeMeters / 1000);
        let groundKm = baseKm;
        if (pole && !siteId && terrainBytes) {
            const head = new Uint8Array(terrainBytes).slice(0, 12);
            if (head.length < 12 || new DataView(head.buffer).getFloat32(4, true) > 500) terrainBytes = null;
        }
        if (siteId || terrainBytes) {
            try {
                const response = terrainBytes ? null : await fetch(`data/terrain/${encodeURIComponent(siteId)}.bin`);
                if (terrainBytes || response.ok) {
                    const buffer = terrainBytes ? new Uint8Array(terrainBytes).slice().buffer : await response.arrayBuffer();
                    const header = new DataView(buffer, 0, 12);
                    const n = header.getInt32(0, true), spacing = header.getFloat32(4, true) / 1000, unit = header.getFloat32(8, true) / 1000;
                    const heights = new Int16Array(buffer, 12, n * n);
                    const positions = new Float32Array(n * n * 3), uv1 = new Float32Array(n * n * 2), uvs = new Float32Array(n * n * 2);
                    const half = (n - 1) / 2;
                    const g = this.poleGrid;
                    const angle = pole ? Math.atan2(pole.x, pole.y) : 0;
                    const p = new THREE.Vector3();
                    for (let i = 0; i < n; i++) {
                        for (let j = 0; j < n; j++) {
                            const k = i * n + j, x = (j - half) * spacing, y = (half - i) * spacing;
                            const z = heights[k] * unit - (x * x + y * y) / (2 * R);
                            p.copy(origin).addScaledVector(frame.east, x).addScaledVector(frame.north, y).addScaledVector(frame.up, z);
                            positions.set([p.x, p.y, p.z], k * 3);
                            uv1.set([j * spacing / 0.6, i * spacing / 0.6], k * 2);
                            if (pole && g) {
                                const gx = pole.x / 1000 + x * Math.cos(angle) + y * Math.sin(angle);
                                const gy = pole.y / 1000 - x * Math.sin(angle) + y * Math.cos(angle);
                                uvs.set([(gx + g.half / 1000) / (2 * g.half / 1000), (gy + g.half / 1000) / (2 * g.half / 1000)], k * 2);
                            } else {
                                const ll = toLatLon(p);
                                let u = (ll.lon + 180) / 360;
                                if (j > 0 && Math.abs(u - uvs[(k - 1) * 2]) > 0.5) u += u < uvs[(k - 1) * 2] ? 1 : -1;
                                uvs.set([u, (ll.lat + 90) / 180], k * 2);
                            }
                        }
                    }
                    groundKm = baseKm + heights[(half | 0) * n + (half | 0)] * unit;
                    const index = new Uint32Array((n - 1) * (n - 1) * 6);
                    let t = 0;
                    for (let i = 0; i < n - 1; i++) {
                        for (let j = 0; j < n - 1; j++) {
                            const a = i * n + j, b = a + 1, c = a + n, d = c + 1;
                            index[t++] = a; index[t++] = c; index[t++] = b; index[t++] = b; index[t++] = c; index[t++] = d;
                        }
                    }
                    const geometry = new THREE.BufferGeometry();
                    geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
                    geometry.setAttribute("uv", new THREE.BufferAttribute(uvs, 2));
                    geometry.setAttribute("uv1", new THREE.BufferAttribute(uv1, 2));
                    geometry.setIndex(new THREE.BufferAttribute(index, 1));
                    geometry.computeVertexNormals();
                    const material = new THREE.MeshStandardMaterial({ roughness: 1, metalness: 0, color: pole ? 0xb9b3ab : 0xffffff, normalMap: this.detailTexture(), normalScale: new THREE.Vector2(0.9, 0.9) });
                    material.map = pole && this.pole ? this.pole.material.map : this.moonMaterial.map;
                    const near = new THREE.Mesh(geometry, material);
                    near.castShadow = true;
                    near.receiveShadow = true;
                    this.scene.add(near);
                    surface.objects.push(near);
                    const tileHalfKm = (n - 1) * spacing / 2;
                    if (pole) this.setPoleHole(pole.x, pole.y, tileHalfKm * 1000 - 2 * spacing * 1000);
                    surface.hole = { dir: frame.up.clone(), cos: Math.cos((tileHalfKm - 2 * spacing) / R) };
                    surface.tileHalfKm = tileHalfKm;
                    if (horizonValues && horizonValues.length) {
                        if (!pole) surface.horizon = Float32Array.from(horizonValues);
                    } else {
                        const horizonResponse = await fetch(`data/horizon/${encodeURIComponent(siteId)}.bin`);
                        if (horizonResponse.ok && !pole) surface.horizon = new Float32Array(await horizonResponse.arrayBuffer());
                    }
                }
            } catch (error) {
                console.warn("terrain", error);
            }
        }
        surface.eye = fromLatLon(latDeg, lonDeg, R + groundKm + surface.eyeMeters / 1000);
        if (surface.horizon) this.addHorizonRing(surface, surface.horizon);
        if (this.moonShader) {
            this.moonShader.uniforms.holeDir.value.copy(surface.hole ? surface.hole.dir : new THREE.Vector3(0, 0, 0));
            this.moonShader.uniforms.holeCos.value = surface.hole ? surface.hole.cos : 2.0;
        }
        const sunUp = this.sunDir.dot(frame.up) > -0.02;
        const earthLocal = this.earthVector ? this.earthVector.clone().sub(surface.eye).normalize() : this.earthDir;
        const earthUp = earthLocal.dot(frame.up) > -0.02;
        const look = earthUp ? earthLocal : sunUp ? this.sunDir : frame.north;
        surface.heading = (Math.atan2(look.dot(frame.east), look.dot(frame.north)) / DEG + 360) % 360;
        const lookUp = Math.asin(Math.max(-1, Math.min(1, look.dot(frame.up)))) / DEG;
        surface.pitch = Math.max(2, Math.min(24, lookUp + 2));
        if (lookUp > 30) surface.fov = Math.min(80, 62 + (lookUp - 24) * 1.2);
        this.surface = surface;
        this.renderer.shadowMap.enabled = true;
        const sc = this.sun.shadow.camera;
        sc.left = -120; sc.right = 120; sc.top = 120; sc.bottom = -120; sc.near = 3750; sc.far = 4250;
        sc.updateProjectionMatrix();
        this.sun.shadow.normalBias = 0.12;
        this.sun.shadow.bias = -0.0006;
        this.sun.shadow.map && this.sun.shadow.map.dispose();
        this.sun.shadow.map = null;
        this.sun.shadow.mapSize.set(4096, 4096);
        this.shadowMode = "surface";
        this.scene.traverse((o) => { if (o.material) o.material.needsUpdate = true; });
        this.buildCompass();
        this.overlay.querySelectorAll(".scene-site, .scene-custom, .scene-region").forEach((el) => el.style.display = "none");
        if (this.regionPoints) this.regionPoints.visible = false;
        this.velocity.x = this.velocity.y = 0;
        this.flight = null;
        this.applyTime();
        this.dirty = true;
    }

    addHorizonRing(surface, elevations) {
        const count = elevations.length, dist = Math.max(60, (surface.tileHalfKm || 0) * 1.6), positions = [], normals = [];
        const { up, east, north } = surface.frame;
        for (let i = 0; i <= count; i++) {
            const az = (i % count) / count * 360 * DEG;
            const dir = north.clone().multiplyScalar(Math.cos(az)).addScaledVector(east, Math.sin(az));
            const e = elevations[i % count] * DEG;
            const top = surface.eye.clone().addScaledVector(dir, dist).addScaledVector(up, dist * Math.tan(e));
            const bottom = surface.eye.clone().addScaledVector(dir, dist).addScaledVector(up, -dist * 0.06 - 5);
            positions.push(top.x, top.y, top.z, bottom.x, bottom.y, bottom.z);
            const n = dir.clone().negate().addScaledVector(up, 0.3).normalize();
            normals.push(n.x, n.y, n.z, n.x, n.y, n.z);
        }
        const index = [];
        for (let i = 0; i < count; i++) {
            const a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            index.push(a, b, c, c, b, d);
        }
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
        geometry.setAttribute("normal", new THREE.Float32BufferAttribute(normals, 3));
        geometry.setIndex(index);
        const ring = new THREE.Mesh(geometry, new THREE.MeshStandardMaterial({ color: 0x6f6b65, roughness: 1, side: THREE.DoubleSide }));
        this.scene.add(ring);
        surface.objects.push(ring);
    }

    leaveSurface(silent) {
        const surface = this.surface;
        if (!surface) return;
        for (const o of surface.objects) {
            this.scene.remove(o);
            o.geometry.dispose();
            if (o.material.map !== (this.pole && this.pole.material.map)) o.material.dispose();
        }
        this.setPoleHole(0, 0, 0);
        if (this.moonShader) this.moonShader.uniforms.holeCos.value = 2.0;
        this.surface = null;
        if (this.compass) { this.compass.remove(); this.compass = null; }
        this.overlay.querySelectorAll(".scene-body").forEach((el) => el.remove());
        const sc = this.sun.shadow.camera;
        sc.left = -345; sc.right = 345; sc.top = 60; sc.bottom = -60; sc.near = 10; sc.far = 8000;
        sc.updateProjectionMatrix();
        this.sun.shadow.normalBias = 0.4;
        this.sun.shadow.bias = -0.0002;
        this.sun.shadow.map && this.sun.shadow.map.dispose();
        this.sun.shadow.map = null;
        this.sun.shadow.mapSize.set(this.big ? 4096 : 2048, this.big ? 1024 : 512);
        this.shadowMode = "pole";
        this.overlay.querySelectorAll(".scene-site").forEach((el) => el.style.display = "");
        if (this.regionPoints) this.regionPoints.visible = this.regionsVisible;
        if (!silent) {
            this.view = { lat: surface.lat, lon: surface.lon, range: 120, tilt: 70, heading: surface.heading };
            this.flyTo(surface.lat, surface.lon, surface.lat < -70 ? 900 : 1800, 0, 0, 2200);
        }
        this.applyTime();
        this.dirty = true;
    }

    turnTo(target, fov) {
        if (!this.surface) return;
        const f = this.surface.frame;
        const look = target === "sun" ? this.sunDir : this.earth.position.clone().sub(this.surface.eye).normalize();
        const heading = (Math.atan2(look.dot(f.east), look.dot(f.north)) / DEG + 360) % 360;
        const pitch = Math.asin(Math.max(-1, Math.min(1, look.dot(f.up)))) / DEG;
        this.surface.turn = { from: { h: this.surface.heading, p: this.surface.pitch, f: this.surface.fov }, to: { h: heading, p: pitch, f: fov || (target === "earth" ? 4 : 50) }, start: performance.now(), duration: 1600 };
        this.dirty = true;
    }

    stepTurn(now) {
        const s = this.surface, t = s.turn;
        const k = easeInOut(Math.min(1, (now - t.start) / t.duration));
        const dh = ((t.to.h - t.from.h + 540) % 360) - 180;
        s.heading = (t.from.h + dh * k + 360) % 360;
        s.pitch = t.from.p + (t.to.p - t.from.p) * k;
        s.fov = Math.exp(Math.log(t.from.f) * (1 - k) + Math.log(t.to.f) * k);
        if (k >= 1) s.turn = null;
        this.dirty = true;
    }

    setEye(meters) {
        if (!this.surface) return;
        const s = this.surface;
        const ground = s.eye.length() - s.eyeMeters / 1000;
        s.eyeMeters = meters;
        s.eye = fromLatLon(s.lat, s.lon, ground + meters / 1000);
        this.applyTime();
        this.dirty = true;
    }

    updateSurfaceCamera() {
        const s = this.surface, f = s.frame;
        const h = s.heading * DEG, p = s.pitch * DEG;
        const dir = f.north.clone().multiplyScalar(Math.cos(h) * Math.cos(p)).addScaledVector(f.east, Math.sin(h) * Math.cos(p)).addScaledVector(f.up, Math.sin(p));
        this.camera.position.copy(s.eye);
        this.camera.up.copy(f.up);
        this.camera.lookAt(s.eye.clone().add(dir));
        this.camera.fov = s.fov;
        this.camera.near = 0.0005;
        this.camera.far = 400000;
        this.camera.updateProjectionMatrix();
        this.camera.updateMatrixWorld();
    }

    buildCompass() {
        if (this.compass) this.compass.remove();
        const el = document.createElement("div");
        el.className = "scene-compass";
        el.setAttribute("aria-hidden", "true");
        this.overlay.appendChild(el);
        this.compass = el;
        const names = this.surface.labels.directions || ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        this.compassTicks = [];
        for (let az = 0; az < 360; az += 15) {
            const tick = document.createElement("span");
            tick.className = az % 45 === 0 ? "major" : "minor";
            if (az % 45 === 0) tick.textContent = names[az / 45];
            el.appendChild(tick);
            this.compassTicks.push({ az, el: tick });
        }
        for (const body of ["sun", "earth"]) {
            const marker = document.createElement("span");
            marker.className = "body " + body;
            el.appendChild(marker);
            this.compassTicks.push({ body, el: marker });
            const label = document.createElement("div");
            label.className = "scene-body " + body;
            label.innerHTML = '<span class="name"></span><span class="value"></span>';
            label.querySelector(".name").textContent = this.surface.labels[body] || body;
            this.overlay.appendChild(label);
            this[body + "Label"] = label;
        }
    }

    updateSkyPan(heading, fov) {
        for (const svg of document.querySelectorAll("svg.sky.pan")) {
            const w = Number(svg.dataset.width);
            const pan = svg.querySelector(".sky-pan"), frame = svg.querySelector(".sky-fov");
            if (!w || !pan || !frame) continue;
            const t = `translate(${(w / 2 - heading / 360 * w).toFixed(1)} 0)`;
            if (pan.getAttribute("transform") !== t) pan.setAttribute("transform", t);
            const fw = Math.min(fov, 360) / 360 * w;
            const width = fw.toFixed(1);
            if (frame.getAttribute("width") !== width) {
                frame.setAttribute("width", width);
                frame.setAttribute("x", (w / 2 - fw / 2).toFixed(1));
            }
        }
    }

    updateSurfaceOverlay() {
        if (this.scaleEl) this.scaleEl.style.display = "none";
        this.updateStations();
        const s = this.surface, f = s.frame;
        const half = Math.atan(Math.tan(s.fov * DEG / 2) * this.camera.aspect) / DEG;
        const span = Math.min(180, half * 1.25);
        this.skyFov = half * 2;
        const xOf = (az) => {
            const d = ((az - s.heading + 540) % 360) - 180;
            return Math.abs(d) > span ? null : 50 + d / span * 50;
        };
        const azOf = (v) => (Math.atan2(v.dot(f.east), v.dot(f.north)) / DEG + 360) % 360;
        const elOf = (v) => Math.asin(Math.max(-1, Math.min(1, v.dot(f.up)))) / DEG;
        const bodies = { sun: this.sunDir, earth: this.earth.position.clone().sub(s.eye).normalize() };
        for (const t of this.compassTicks) {
            const az = t.body ? azOf(bodies[t.body]) : t.az;
            const x = xOf(az);
            t.el.style.display = x === null ? "none" : "";
            if (x !== null) t.el.style.left = x.toFixed(2) + "%";
        }
        for (const body of ["sun", "earth"]) {
            const label = this[body + "Label"];
            const dir = bodies[body];
            const p = this.project(s.eye.clone().addScaledVector(dir, 1000));
            const ahead = dir.dot(this.camera.getWorldDirection(new THREE.Vector3())) > 0;
            const show = ahead && p.x > -50 && p.x < this.width + 50 && p.y > -50 && p.y < this.height + 50;
            label.style.display = show ? "" : "none";
            if (show) {
                const offset = body === "earth" ? Math.max(14, this.height * 0.95 * (1.0 / s.fov)) : 26;
                label.style.transform = `translate(${p.x.toFixed(1)}px, ${(p.y + offset).toFixed(1)}px)`;
                label.querySelector(".value").textContent = elOf(dir).toFixed(1) + "°";
            }
        }
    }

    getView() {
        return { ...this.view };
    }

    setView(view) {
        Object.assign(this.view, view);
        this.dirty = true;
    }

    dispose() {
        if (current === this) current = null;
        this.disposed = true;
        this.resizeObserver && this.resizeObserver.disconnect();
        if (this.relief) this.relief.dispose();
        if (this.poleDetail) this.poleDetail.dispose();
        if (this.terrain) this.terrain.dispose();
        if (this.renderer) { this.renderer.dispose(); this.renderer.forceContextLoss(); }
        this.overlay.querySelectorAll(".scene-site, .scene-custom, .scene-probe, .scene-route, .scene-view, .scene-zone").forEach((el) => el.remove());
    }
}

let current = null;

export async function init(canvas, overlay, dotnet, options) {
    const callbacks = {
        site: (id) => dotnet && dotnet.invokeMethodAsync("OnSitePicked", id),
        point: (lat, lon) => dotnet && dotnet.invokeMethodAsync("OnPointPicked", lat, lon),
        region: (code) => dotnet && dotnet.invokeMethodAsync("OnRegionPicked", code),
        route: (route) => dotnet && dotnet.invokeMethodAsync("OnRouteChanged", route)
    };
    const scene = new MoonScene(canvas, overlay, callbacks, options);
    scene.setUnits(options && options.units);
    await scene.init();
    current = scene;
    window.__moonscene = scene;
    return scene;
}

export function setSites(sites) { current && current.setSites(sites); }
export function setStations(stations) { current && current.setStations(stations); }
export function setRegions(regions, visible) { return current ? current.setRegions(regions, visible) : null; }
export function showRegions(visible) { current && current.showRegions(visible); }
export function setSelected(id, lat, lon) { current && current.setSelected(id, lat, lon); }
export function setTime(utc, sun, earth) { current && current.setTime(utc, sun, earth); }
export function flyTo(lat, lon, range, tilt, heading, duration) { current && current.flyTo(lat, lon, range, tilt, heading, duration); }
export function getView() { return current ? current.getView() : null; }
export function setView(view) { current && current.setView(view); }
export function standAt(siteId, lat, lon, eye, labels, elevation, terrain, horizon) { return current ? current.standAt(siteId, lat, lon, eye, labels, elevation, terrain, horizon) : null; }
export function leaveSurface() { current && current.leaveSurface(false); }
export function turnTo(target, fov) { current && current.turnTo(target, fov); }
export function setEye(meters) { current && current.setEye(meters); }
export function setLayer(mode, labels) { return current ? current.setLayer(mode, labels) : false; }
export function setGrid(on) { return current ? current.setGrid(on) : false; }
export function setNowMap(bytes, n, halfM) { return current ? current.setNowMap(bytes, n, halfM) : false; }
export function probe(lat, lon) { return current ? current.probe(lat, lon) : null; }
export function setRouteMode(on) { return current ? current.setRouteMode(on) : false; }
export function undoWaypoint() { current && current.undoWaypoint(); }
export function clearRoute() { current && current.clearRoute(); }
export function record(title, lines, fileName, seconds) { return current ? current.record(title, lines, fileName, seconds) : null; }
export function stopRecording() { current && current.stopRecording(); }
export function snapshot(title, lines, fileName) { return current ? current.snapshot(title, lines, fileName) : false; }
export function setUnits(units) { current && current.setUnits(units); }
export function dispose() { if (current) { current.dispose(); current = null; } }
export const internals = { fromLatLon, toLatLon, moonFrameFromJ2000, gmstRad };
