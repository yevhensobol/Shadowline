import argparse
import urllib.request
from pathlib import Path

BASE = "https://naif.jpl.nasa.gov/pub/naif/generic_kernels"
KERNELS = {
    "naif0012.tls": f"{BASE}/lsk/naif0012.tls",
    "de440s.bsp": f"{BASE}/spk/planets/de440s.bsp",
    "moon_pa_de440_200625.bpc": f"{BASE}/pck/moon_pa_de440_200625.bpc",
    "moon_de440_250416.tf": f"{BASE}/fk/satellites/moon_de440_250416.tf",
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dest", required=True)
    args = parser.parse_args()

    dest = Path(args.dest)
    dest.mkdir(parents=True, exist_ok=True)
    for name, url in KERNELS.items():
        target = dest / name
        if target.exists():
            print(f"skip {name}")
            continue
        print(f"get {url}")
        urllib.request.urlretrieve(url, target)
        print(f"saved {target} {target.stat().st_size} bytes")


if __name__ == "__main__":
    main()
