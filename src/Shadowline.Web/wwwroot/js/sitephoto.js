const R = 1737.4;
const DEG = Math.PI / 180;
const TILES = "https://trek.nasa.gov/tiles/Moon/SP/LRO_NAC_AvgMosaic_SPole855_1mp/1.0.0/default/default028mm/";
const ORIGIN_M = 1095930;
const LEVEL = 11;
const EDGE_M = 136000;
const SIZE_M = 5120;
const PIXEL_M = 5;
const BAR_M = 1000;

function polar(latDeg, lonDeg) {
    const rho = 2 * R * 1000 * Math.tan((90 + latDeg) * DEG / 2);
    return [rho * Math.sin(lonDeg * DEG), rho * Math.cos(lonDeg * DEG)];
}

function image(url) {
    return new Promise((resolve) => {
        const img = new Image();
        img.crossOrigin = "anonymous";
        img.onload = () => resolve(img);
        img.onerror = () => resolve(null);
        img.src = url;
    });
}

export async function render(latDeg, lonDeg, barLabel) {
    const [x, y] = polar(latDeg, lonDeg);
    if (Math.hypot(x, y) + SIZE_M / 2 > EDGE_M) return null;
    const half = SIZE_M / 2;
    const left = x - half, top = y + half;
    const tileM = 2 * ORIGIN_M / 2 ** LEVEL;
    const c0 = Math.floor((left + ORIGIN_M) / tileM), c1 = Math.floor((x + half + ORIGIN_M) / tileM);
    const r0 = Math.floor((ORIGIN_M - top) / tileM), r1 = Math.floor((ORIGIN_M - (y - half)) / tileM);
    const jobs = [];
    for (let r = r0; r <= r1; r++) {
        for (let c = c0; c <= c1; c++) jobs.push(image(`${TILES}${LEVEL}/${r}/${c}.png`).then((img) => ({ img, r, c })));
    }
    const tiles = await Promise.all(jobs);
    if (!tiles.some((t) => t.img)) return null;
    const n = SIZE_M / PIXEL_M;
    const canvas = document.createElement("canvas");
    canvas.width = n;
    canvas.height = n;
    const ctx = canvas.getContext("2d");
    ctx.fillStyle = "#000000";
    ctx.fillRect(0, 0, n, n);
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = "high";
    const s = tileM / PIXEL_M;
    for (const { img, r, c } of tiles) {
        if (img) ctx.drawImage(img, (-ORIGIN_M + c * tileM - left) / PIXEL_M, (top - (ORIGIN_M - r * tileM)) / PIXEL_M, s + 0.5, s + 0.5);
    }
    const mid = n / 2;
    ctx.strokeStyle = "#f2a33a";
    ctx.lineWidth = 3;
    ctx.beginPath();
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        ctx.moveTo(mid + dx * 10, mid + dy * 10);
        ctx.lineTo(mid + dx * 34, mid + dy * 34);
    }
    ctx.stroke();
    const bar = BAR_M / PIXEL_M, x0 = 24, base = n - 28;
    ctx.fillStyle = "rgba(0, 0, 0, 0.55)";
    ctx.fillRect(x0 - 10, base - 34, bar + 20, 46);
    ctx.strokeStyle = "#ffffff";
    ctx.lineWidth = 3;
    ctx.beginPath();
    ctx.moveTo(x0, base - 8);
    ctx.lineTo(x0, base);
    ctx.lineTo(x0 + bar, base);
    ctx.lineTo(x0 + bar, base - 8);
    ctx.stroke();
    ctx.fillStyle = "#ffffff";
    ctx.font = "20px sans-serif";
    ctx.textAlign = "center";
    ctx.fillText(barLabel, x0 + bar / 2, base - 14);
    return { url: canvas.toDataURL("image/jpeg", 0.9), photoM: PIXEL_M, sizeKm: SIZE_M / 1000 };
}
