export function download(name, text, type) {
    const blob = new Blob([text], { type: type || "text/plain" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = name;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 4000);
}

export function print() {
    document.querySelectorAll("details.inc-more").forEach((d) => { d.open = true; });
    window.print();
}
