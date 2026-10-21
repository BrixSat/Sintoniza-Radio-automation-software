#!/usr/bin/env python3
"""Makes packaging/macos/*.icns from packaging/icon*.svg (needs inkscape). Run again after changing an icon.

An .icns is a header and PNG images of standard sizes, each under a four-letter type."""
import os, struct, subprocess, tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SIZES = [("icp4", 16), ("icp5", 32), ("icp6", 64), ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024)]

def make(svg, icns):
    chunks = b""
    with tempfile.TemporaryDirectory() as tmp:
        for kind, size in SIZES:
            png = os.path.join(tmp, f"{size}.png")
            subprocess.run(["inkscape", svg, "-w", str(size), "-h", str(size), "-o", png], check=True, capture_output=True)
            data = open(png, "rb").read()
            chunks += kind.encode() + struct.pack(">I", len(data) + 8) + data
    with open(icns, "wb") as f:
        f.write(b"icns" + struct.pack(">I", len(chunks) + 8) + chunks)
    print(icns)

make(os.path.join(ROOT, "packaging", "icon.svg"), os.path.join(ROOT, "packaging", "macos", "icon.icns"))
make(os.path.join(ROOT, "packaging", "icon-schedule.svg"), os.path.join(ROOT, "packaging", "macos", "schedule.icns"))
make(os.path.join(ROOT, "packaging", "icon-import.svg"), os.path.join(ROOT, "packaging", "macos", "import.icns"))
make(os.path.join(ROOT, "packaging", "icon-rec.svg"), os.path.join(ROOT, "packaging", "macos", "rec.icns"))
