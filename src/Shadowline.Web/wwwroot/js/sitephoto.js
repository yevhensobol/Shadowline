const R = 1737.4;
const DEG = Math.PI / 180;
const BASE = "data/sdem/";
const SIZE_M = 5120;
const PIXEL_M = 5;
const BAR_M = 1000;

let index = null;

function loadIndex() {
    if (!index) {
        index = fetch(BASE + "index.json").then((response) => {
            if (!response.ok) throw new Error("sdem index " + response.status);
            return response.json();
        });
        index.catch(() => { index = null; });
    }
    return index;
}

function polar(latDeg, lonDeg) {
    const rho = 2 * R * 1000 * Math.tan((90 + latDeg) * DEG / 2);
    return [rho * Math.sin(lonDeg * DEG), rho * Math.cos(lonDeg * DEG)];
}

function image(url) {
    return new Promise((resolve) => {
        const img = new Image();
        img.onload = () => resolve(img);
        img.onerror = () => resolve(null);
        img.src = url;
    });
}

export async function render(latDeg, lonDeg, barLabel) {
    if (latDeg > -75) return null;
    const data = await loadIndex();
    const [x, y] = polar(latDeg, lonDeg);
    const region = data.regions.find((r) => x >= r.bounds[0] && x <= r.bounds[2] && y >= r.bounds[1] && y <= r.bounds[3]);
    if (!region) return null;
    const half = SIZE_M / 2;
    const box = [x - half, y - half, x + half, y + half];
    const tiles = region.tiles.filter((t) => t[1] === 5 && t[4] > box[0] && t[2] < box[2] && t[5] > box[1] && t[3] < box[3]);
    if (!tiles.length) return null;
    const images = await Promise.all(tiles.map((t) => image(BASE + region.id + "/" + t[0] + ".jpg")));
    if (!images.some((img) => img)) return null;
    const n = SIZE_M / PIXEL_M;
    const canvas = document.createElement("canvas");
    canvas.width = n;
    canvas.height = n;
    const ctx = canvas.getContext("2d");
    ctx.fillStyle = "#000000";
    ctx.fillRect(0, 0, n, n);
    ctx.imageSmoothingEnabled = true;
    tiles.forEach((t, i) => {
        const img = images[i];
        if (!img) return;
        ctx.drawImage(img, (t[2] - box[0]) / PIXEL_M, (box[3] - t[5]) / PIXEL_M, (t[4] - t[2]) / PIXEL_M, (t[5] - t[3]) / PIXEL_M);
    });
    const c = n / 2;
    ctx.strokeStyle = "#f2a33a";
    ctx.lineWidth = 3;
    ctx.beginPath();
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        ctx.moveTo(c + dx * 10, c + dy * 10);
        ctx.lineTo(c + dx * 34, c + dy * 34);
    }
    ctx.stroke();
    const bar = BAR_M / PIXEL_M, left = 24, base = n - 28;
    ctx.fillStyle = "rgba(0, 0, 0, 0.55)";
    ctx.fillRect(left - 10, base - 34, bar + 20, 46);
    ctx.strokeStyle = "#ffffff";
    ctx.lineWidth = 3;
    ctx.beginPath();
    ctx.moveTo(left, base - 8);
    ctx.lineTo(left, base);
    ctx.lineTo(left + bar, base);
    ctx.lineTo(left + bar, base - 8);
    ctx.stroke();
    ctx.fillStyle = "#ffffff";
    ctx.font = "20px sans-serif";
    ctx.textAlign = "center";
    ctx.fillText(barLabel, left + bar / 2, base - 14);
    return {
        url: canvas.toDataURL("image/jpeg", 0.9),
        region: region.name,
        photoM: PIXEL_M,
        nativeM: region.photoM || PIXEL_M,
        sizeKm: SIZE_M / 1000
    };
}
