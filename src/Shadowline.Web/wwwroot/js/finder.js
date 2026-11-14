const stops = [[0, [28, 39, 64]], [0.45, [46, 74, 117]], [0.7, [181, 122, 46]], [1, [245, 197, 66]]];

function ramp(v) {
    for (let i = 1; i < stops.length; i++) {
        if (v <= stops[i][0]) {
            const [a, ca] = stops[i - 1];
            const [b, cb] = stops[i];
            const t = (v - a) / (b - a);
            return [0, 1, 2].map(k => Math.round(ca[k] + (cb[k] - ca[k]) * t));
        }
    }
    return stops[stops.length - 1][1];
}

export function draw(heat, lines, size, values, states, zones, selectedZone, selectedCell) {
    heat.width = size;
    heat.height = size;
    const ctx = heat.getContext("2d");
    const image = ctx.createImageData(size, size);
    const data = image.data;
    for (let i = 0; i < size * size; i++) {
        const o = i * 4;
        const state = states[i];
        if (state === 0) {
            data[o] = 6; data[o + 1] = 9; data[o + 2] = 20; data[o + 3] = 225;
        } else if (state === 1) {
            const c = ramp(values[i] / 255);
            data[o] = Math.round(c[0] * 0.6 + 2); data[o + 1] = Math.round(c[1] * 0.6 + 3); data[o + 2] = Math.round(c[2] * 0.6 + 8); data[o + 3] = 222;
        } else {
            const c = ramp(values[i] / 255);
            data[o] = c[0]; data[o + 1] = c[1]; data[o + 2] = c[2]; data[o + 3] = 250;
        }
    }
    ctx.putImageData(image, 0, 0);

    const scale = 4;
    lines.width = size * scale;
    lines.height = size * scale;
    const g = lines.getContext("2d");
    g.clearRect(0, 0, lines.width, lines.height);
    const zoneAt = (r, c) => (r < 0 || c < 0 || r >= size || c >= size) ? 255 : zones[r * size + c];
    for (const pass of [0, 1]) {
        g.beginPath();
        for (let r = 0; r < size; r++) {
            for (let c = 0; c < size; c++) {
                const z = zones[r * size + c];
                if (z === 255 || (pass === 1) !== (z === selectedZone)) {
                    continue;
                }
                const x = c * scale, y = r * scale;
                if (zoneAt(r - 1, c) !== z) { g.moveTo(x, y); g.lineTo(x + scale, y); }
                if (zoneAt(r + 1, c) !== z) { g.moveTo(x, y + scale); g.lineTo(x + scale, y + scale); }
                if (zoneAt(r, c - 1) !== z) { g.moveTo(x, y); g.lineTo(x, y + scale); }
                if (zoneAt(r, c + 1) !== z) { g.moveTo(x + scale, y); g.lineTo(x + scale, y + scale); }
            }
        }
        g.strokeStyle = pass === 1 ? "#f2a33a" : "rgba(242, 244, 248, 0.85)";
        g.lineWidth = pass === 1 ? 3 : 1.5;
        g.stroke();
    }
    if (selectedCell >= 0) {
        const r = Math.floor(selectedCell / size), c = selectedCell % size;
        g.strokeStyle = "#f2a33a";
        g.lineWidth = 2;
        g.beginPath();
        g.arc((c + 0.5) * scale, (r + 0.5) * scale, scale * 2.2, 0, Math.PI * 2);
        g.stroke();
    }
}

export function attach(element, dotnet, size) {
    let last = -1;
    let frame = 0;
    const cellOf = (event) => {
        const box = element.getBoundingClientRect();
        const c = Math.floor((event.clientX - box.left) / box.width * size);
        const r = Math.floor((event.clientY - box.top) / box.height * size);
        return (r < 0 || c < 0 || r >= size || c >= size) ? -1 : r * size + c;
    };
    const click = (event) => {
        const cell = cellOf(event);
        if (cell >= 0) {
            dotnet.invokeMethodAsync("OnCellClick", cell);
        }
    };
    const move = (event) => {
        const cell = cellOf(event);
        if (cell === last || frame) {
            return;
        }
        frame = requestAnimationFrame(() => {
            frame = 0;
            last = cell;
            dotnet.invokeMethodAsync("OnCellHover", cell);
        });
    };
    element.addEventListener("click", click);
    element.addEventListener("mousemove", move);
    return {
        dispose() {
            element.removeEventListener("click", click);
            element.removeEventListener("mousemove", move);
        }
    };
}
