import * as THREE from "../lib/three/three.min.js";

const DEG = Math.PI / 180;

function enu(e, n, u) {
    return new THREE.Vector3(e, u, -n);
}

function dir(azDeg, elDeg) {
    const a = azDeg * DEG;
    const e = elDeg * DEG;
    return enu(Math.sin(a) * Math.cos(e), Math.cos(a) * Math.cos(e), Math.sin(e));
}

function label(text, color) {
    const c = document.createElement("canvas");
    c.width = 128;
    c.height = 64;
    const g = c.getContext("2d");
    g.font = "600 40px Geologica, system-ui, sans-serif";
    g.fillStyle = color;
    g.textAlign = "center";
    g.textBaseline = "middle";
    g.fillText(text, 64, 32);
    const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: new THREE.CanvasTexture(c), depthTest: false, transparent: true }));
    sprite.scale.set(1.4, 0.7, 1);
    return sprite;
}

class LanderView {
    constructor(canvas) {
        this.canvas = canvas;
        this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
        this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
        this.scene = new THREE.Scene();
        this.camera = new THREE.PerspectiveCamera(38, 1, 0.1, 200);
        this.yaw = -35 * DEG;
        this.pitch = 24 * DEG;
        this.distance = 17;

        this.scene.add(new THREE.HemisphereLight(0x8899bb, 0x222222, 0.6));
        this.sunLight = new THREE.DirectionalLight(0xfff1d6, 2.2);
        this.scene.add(this.sunLight);
        this.scene.add(this.sunLight.target);

        const ground = new THREE.Mesh(new THREE.CircleGeometry(7, 64), new THREE.MeshStandardMaterial({ color: 0x5c5f66, roughness: 1 }));
        ground.rotation.x = -Math.PI / 2;
        this.scene.add(ground);
        const ring = new THREE.Mesh(new THREE.RingGeometry(6.95, 7.05, 96), new THREE.MeshBasicMaterial({ color: 0x7f8aa0, side: THREE.DoubleSide }));
        ring.rotation.x = -Math.PI / 2;
        ring.position.y = 0.01;
        this.scene.add(ring);
        for (const [t, az] of [["N", 0], ["E", 90], ["S", 180], ["W", 270]]) {
            const s = label(t, "#b3bccd");
            s.position.copy(dir(az, 0).multiplyScalar(7.8)).add(new THREE.Vector3(0, 0.3, 0));
            this.scene.add(s);
        }

        this.horizon = new THREE.Line(new THREE.BufferGeometry(), new THREE.LineBasicMaterial({ color: 0xc9a4ff }));
        this.scene.add(this.horizon);

        this.lander = new THREE.Group();
        const body = new THREE.Mesh(
            new THREE.BoxGeometry(1.6, 2.0, 1.6),
            [0, 1, 2, 3, 4, 5].map((k) => new THREE.MeshStandardMaterial({ color: k === 2 ? 0x2b73eb : 0xb9a77a, roughness: k === 2 ? 0.35 : 0.8, metalness: k === 2 ? 0.2 : 0.1 })),
        );
        body.position.y = 0;
        this.lander.add(body);
        this.panelGlow = body.material[2];
        const normal = new THREE.ArrowHelper(new THREE.Vector3(0, 1, 0), new THREE.Vector3(0, 1.0, 0), 2.2, 0x72d39a, 0.35, 0.2);
        this.lander.add(normal);
        this.scene.add(this.lander);

        this.sunDisc = new THREE.Mesh(new THREE.SphereGeometry(0.32, 24, 16), new THREE.MeshBasicMaterial({ color: 0xf2b84b }));
        this.scene.add(this.sunDisc);
        this.sunRay = new THREE.Line(new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3()]), new THREE.LineDashedMaterial({ color: 0xf2b84b, dashSize: 0.3, gapSize: 0.2 }));
        this.scene.add(this.sunRay);
        this.earthDisc = new THREE.Mesh(new THREE.SphereGeometry(0.28, 24, 16), new THREE.MeshBasicMaterial({ color: 0x4f9fe0 }));
        this.scene.add(this.earthDisc);

        this.drag = null;
        canvas.addEventListener("pointerdown", (e) => { this.drag = { x: e.clientX, y: e.clientY }; canvas.setPointerCapture(e.pointerId); });
        canvas.addEventListener("pointermove", (e) => {
            if (!this.drag) return;
            this.yaw -= (e.clientX - this.drag.x) * 0.008;
            this.pitch = Math.min(80 * DEG, Math.max(-5 * DEG, this.pitch + (e.clientY - this.drag.y) * 0.006));
            this.drag = { x: e.clientX, y: e.clientY };
            this.render();
        });
        canvas.addEventListener("pointerup", () => { this.drag = null; });
        canvas.addEventListener("wheel", (e) => { e.preventDefault(); this.distance = Math.min(45, Math.max(8, this.distance * (1 + Math.sign(e.deltaY) * 0.1))); this.render(); }, { passive: false });
        this.resizeObserver = new ResizeObserver(() => this.render());
        this.resizeObserver.observe(canvas);
    }

    setHorizon(samples) {
        const pts = [];
        for (let k = 0; k <= samples.length; k++) {
            const az = (k % samples.length) * 360 / samples.length;
            pts.push(dir(az, Math.max(samples[k % samples.length], -2)).multiplyScalar(7).add(new THREE.Vector3(0, 0.02, 0)));
        }
        this.horizon.geometry.dispose();
        this.horizon.geometry = new THREE.BufferGeometry().setFromPoints(pts);
        this.render();
    }

    update(state) {
        const x = enu(state.bodyX[0], state.bodyX[1], state.bodyX[2]);
        const y = enu(state.bodyY[0], state.bodyY[1], state.bodyY[2]);
        const z = enu(state.bodyZ[0], state.bodyZ[1], state.bodyZ[2]);
        const m = new THREE.Matrix4().makeBasis(x, z, y.clone().negate());
        this.lander.quaternion.setFromRotationMatrix(m);
        const lowest = Math.min(...[-1, 1].flatMap((a) => [-1, 1].flatMap((b) => [-1, 1].map((c) => x.clone().multiplyScalar(0.8 * a).add(y.clone().multiplyScalar(0.8 * b)).add(z.clone().multiplyScalar(1.0 * c)).y))));
        this.lander.position.set(0, -lowest, 0);
        const sun = dir(state.sunAz, state.sunEl);
        this.sunDisc.position.copy(sun.clone().multiplyScalar(7.5));
        this.sunDisc.visible = state.sunEl > -1;
        this.sunLight.position.copy(sun.clone().multiplyScalar(20));
        this.sunLight.intensity = state.sunVisible ? 3.2 : 0.0;
        this.sunRay.geometry.setFromPoints([this.lander.position.clone().add(z.clone().multiplyScalar(1.0)), this.sunDisc.position.clone()]);
        this.sunRay.computeLineDistances();
        this.sunRay.visible = state.sunEl > -1;
        this.earthDisc.position.copy(dir(state.earthAz, state.earthEl).multiplyScalar(7.5));
        this.earthDisc.visible = state.earthEl > -2;
        this.panelGlow.emissive = new THREE.Color(0x72d39a).multiplyScalar(Math.max(0, Math.min(1, state.light)) * 0.6);
        this.render();
    }

    render() {
        const w = this.canvas.clientWidth || 600;
        const h = this.canvas.clientHeight || 360;
        if (this.canvas.width !== Math.round(w * this.renderer.getPixelRatio()) || this.canvas.height !== Math.round(h * this.renderer.getPixelRatio())) {
            this.renderer.setSize(w, h, false);
            this.camera.aspect = w / h;
            this.camera.updateProjectionMatrix();
        }
        const c = Math.cos(this.pitch);
        this.camera.position.set(Math.sin(this.yaw) * c * this.distance, Math.sin(this.pitch) * this.distance + 1, Math.cos(this.yaw) * c * this.distance);
        this.camera.lookAt(0, 0.6, 0);
        this.renderer.render(this.scene, this.camera);
    }

    dispose() {
        this.resizeObserver.disconnect();
        this.renderer.dispose();
    }
}

export function create(canvas, horizon) {
    const view = new LanderView(canvas);
    if (horizon) view.setHorizon(horizon);
    return view;
}
