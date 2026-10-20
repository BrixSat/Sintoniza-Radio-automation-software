#!/usr/bin/env python3
"""Puts the user manual together: the screenshots (tools/ManualShots) with numbered callouts, the text, and a PDF.

    python3 docs/manual/build.py <shots folder> <output.pdf> [en|pt]

The text is in text_<language>.py (CHAPTERS, WORDS). A figure names a screenshot and the controls to point at, in
order; each gets a number on the picture and the same number in the list under it."""
import base64, datetime, html, io, json, os, re, shutil, subprocess, sys, tempfile
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SHOTS, PDF = sys.argv[1], sys.argv[2]
CALLOUTS = json.load(open(os.path.join(SHOTS, "callouts.json")))

ACCENT = "#F5A524"

# ───────────────────────── Text ─────────────────────────
# In text_<language>.py: CHAPTERS and WORDS.
LANG = sys.argv[3] if len(sys.argv) > 3 else "en"
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.dont_write_bytecode = True # no __pycache__ in the project
TEXT = __import__(f"text_{LANG}")
CHAPTERS, WORDS = TEXT.CHAPTERS, TEXT.WORDS

# ───────────────────────── Pictures ─────────────────────────

def font(size):
    for f in ["/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf", "DejaVuSans-Bold.ttf"]:
        try: return ImageFont.truetype(f, size)
        except OSError: pass
    return ImageFont.load_default()

def figure(shot, marks, zoom=False):
    """The screenshot with a frame and a number on each control (in order), as a data URI; with zoom, only the part
    around those controls."""
    im = Image.open(os.path.join(SHOTS, shot + ".png")).convert("RGB")
    rects = CALLOUTS.get(shot, {})
    r = 17
    # Room above the picture for the numbers of controls at its top edge.
    pad = 2 * r + 10
    canvas = Image.new("RGB", (im.width, im.height + pad), "#ffffff")
    canvas.paste(im, (0, pad))
    im = canvas
    rects = {k: [v[0], v[1] + pad, v[2], v[3]] for k, v in rects.items()}
    draw = ImageDraw.Draw(im, "RGBA")
    f = font(20)
    for n, (name, _) in enumerate(marks, 1):
        if name not in rects: continue
        x, y, w, h = rects[name][:4]
        draw.rounded_rectangle([x - 2, y - 2, x + w + 2, y + h + 2], radius=6, outline=ACCENT + "FF", width=3)
        if h < 70:
            # A small control: the number just above it (below, if it is at the top), not over its label.
            cx = x + min(w / 2, 26)
            cy = y - r - 5 if y - 2 * r - 6 >= 0 else y + h + r + 5
        else:
            # A large panel: in its top-left corner.
            cx, cy = x + r + 4, y + r + 4
        cx = min(max(cx, r + 2), im.width - r - 2)
        cy = min(max(cy, r + 2), im.height - r - 2)
        draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=ACCENT + "FF", outline="#111318", width=3)
        t = str(n)
        bw = draw.textlength(t, font=f)
        draw.text((cx - bw / 2, cy - 12), t, fill="#111318", font=f)
    if zoom:
        boxes = [rects[n][:4] for n, _ in marks if n in rects]
        if boxes:
            m = 40
            left = max(0, min(b[0] for b in boxes) - m); top = max(0, min(b[1] for b in boxes) - m - 2 * 17 - 10)
            right = min(im.width, max(b[0] + b[2] for b in boxes) + m); bottom = min(im.height, max(b[1] + b[3] for b in boxes) + m)
            im = im.crop((int(left), int(top), int(right), int(bottom)))
    buf = io.BytesIO()
    im.save(buf, "PNG", optimize=True)
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode()

def icon(png):
    data = open(os.path.join(ROOT, "packaging", "linux", png), "rb").read()
    return "data:image/png;base64," + base64.b64encode(data).decode()

# ───────────────────────── Pages ─────────────────────────

def block(b):
    kind = b[0]
    if kind == "p": return f"<p>{b[1]}</p>"
    if kind == "h2": return f"<h2>{b[1]}</h2>"
    if kind == "h3": return f"<h3>{b[1]}</h3>"
    if kind == "pre": return f"<pre>{html.escape(b[1])}</pre>"
    if kind == "tip": return f'<div class="note tip"><span>{WORDS["tip"]}</span>{b[1]}</div>'
    if kind == "warn": return f'<div class="note warn"><span>{WORDS["important"]}</span>{b[1]}</div>'
    if kind == "steps": return "<ol class='steps'>" + "".join(f"<li>{s}</li>" for s in b[1]) + "</ol>"
    if kind == "list": return "<ul>" + "".join(f"<li>{s}</li>" for s in b[1]) + "</ul>"
    if kind == "table":
        head = "".join(f"<th>{h}</th>" for h in b[1])
        rows = "".join("<tr>" + "".join(f"<td>{c}</td>" for c in r) + "</tr>" for r in b[2])
        return f"<table><thead><tr>{head}</tr></thead><tbody>{rows}</tbody></table>"
    if kind == "icons":
        return "<div class='programs'>" + "".join(
            f"<div class='program'><img src='{icon(p)}'><div><b>{n}</b><p>{t}</p></div></div>" for p, n, t in b[1]) + "</div>"
    if kind == "fig":
        _, shot, caption, marks, *zoom = b
        legend = "".join(f"<li><span class='num'>{i}</span><span>{text}</span></li>" for i, (_, text) in enumerate(marks, 1)
                         if _ in CALLOUTS.get(shot, {}))
        return (f"<figure{' class=zoom' if zoom else ''}><img src='{figure(shot, marks, bool(zoom))}'><figcaption>{caption}</figcaption></figure>"
                + (f"<ol class='legend'>{legend}</ol>" if legend else ""))
    raise ValueError(kind)

CSS = """
@page { size: A4; margin: 16mm 15mm 18mm 15mm;
  @bottom-center { content: counter(page); font: 9pt 'DejaVu Sans', sans-serif; color: #888; } }
* { box-sizing: border-box; }
body { font: 10.3pt/1.5 'Inter', 'DejaVu Sans', 'Segoe UI', sans-serif; color: #1c2230; }
h1 { font-size: 24pt; margin: 0 0 4mm; color: #111318; page-break-before: always; border-bottom: 3px solid """ + ACCENT + """; padding-bottom: 2mm; }
h1 .n { color: """ + ACCENT + """; margin-right: 3mm; }
h2 { font-size: 14.5pt; margin: 7mm 0 2mm; color: #111318; page-break-after: avoid; }
h3 { font-size: 11.5pt; margin: 5mm 0 1mm; page-break-after: avoid; }
p { margin: 1.5mm 0; }
figure { margin: 4mm 0 2mm; page-break-inside: avoid; }
figure img { width: 100%; border-radius: 6px; box-shadow: 0 1px 6px rgba(0,0,0,.25); }
figure.zoom img { width: auto; max-width: 100%; max-height: 90mm; }
figcaption { font-size: 9pt; color: #5a6475; margin-top: 1.5mm; font-style: italic; }
ol.legend { list-style: none; padding: 0; margin: 2mm 0 4mm; columns: 2; column-gap: 7mm; font-size: 9.3pt; }
ol.legend li { break-inside: avoid; margin: 0 0 1.6mm; display: flex; gap: 2mm; }
.num { flex: none; display: inline-block; width: 5.2mm; height: 5.2mm; border-radius: 50%; background: """ + ACCENT + """;
  color: #111318; font-weight: 700; font-size: 8.5pt; text-align: center; line-height: 5.2mm; }
ol.steps { padding-left: 6mm; } ol.steps li { margin: 1mm 0; }
ul { padding-left: 6mm; } ul li { margin: 1mm 0; }
.note { border-left: 4px solid; padding: 2.5mm 4mm; margin: 4mm 0; border-radius: 4px; page-break-inside: avoid; }
.note span { display: block; font-weight: 700; font-size: 8.5pt; text-transform: uppercase; letter-spacing: .08em; margin-bottom: 1mm; }
.tip { background: #EEF6FF; border-color: #3B82F6; } .tip span { color: #2563EB; }
.warn { background: #FFF6E5; border-color: """ + ACCENT + """; } .warn span { color: #B7791F; }
table { width: 100%; border-collapse: collapse; margin: 3mm 0; font-size: 9.5pt; page-break-inside: auto; }
th { text-align: left; background: #1c2230; color: white; padding: 2mm 3mm; }
td { border-bottom: 1px solid #dde2ea; padding: 2mm 3mm; vertical-align: top; }
tr { page-break-inside: avoid; }
pre { page-break-inside: avoid; background: #111318; color: #e7e9ee; padding: 3mm 4mm; border-radius: 6px; font-size: 8.8pt; white-space: pre-wrap; }
code { background: #eef0f4; padding: 0 1mm; border-radius: 3px; }
.programs { display: grid; gap: 3mm; margin: 4mm 0; }
.program { display: flex; gap: 4mm; align-items: center; background: #f4f6f9; border-radius: 8px; padding: 3mm 4mm; page-break-inside: avoid; }
.program img { width: 16mm; height: 16mm; }
.program p { margin: .5mm 0 0; font-size: 9.5pt; color: #444c5c; }
.cover { height: 250mm; display: flex; flex-direction: column; justify-content: center; align-items: center; text-align: center; }
.cover img.logo { width: 46mm; margin-bottom: 8mm; }
.cover .title { font-size: 30pt; font-weight: 800; color: #111318; }
.cover .sub { font-size: 14pt; color: #5a6475; margin-top: 3mm; }
.cover .shot { width: 150mm; margin-top: 12mm; border-radius: 8px; box-shadow: 0 4px 18px rgba(0,0,0,.3); }
.cover .meta { margin-top: 10mm; color: #8a93a3; font-size: 9.5pt; }
.toc { page-break-before: always; }
.toc ol { font-size: 12pt; line-height: 2.1; padding-left: 8mm; }
.toc ol ol { font-size: 10pt; line-height: 1.7; color: #5a6475; }
"""

def build():
    version = subprocess.run(["git", "-C", ROOT, "describe", "--tags", "--always"], capture_output=True, text=True).stdout.strip()
    date = WORDS.get("format_date", lambda d: d.strftime(WORDS["date"]))(datetime.date.today())
    cover_shot = "data:image/png;base64," + base64.b64encode(open(os.path.join(SHOTS, "main.png"), "rb").read()).decode()
    parts = [f"""<div class="cover"><img class="logo" src="{icon('radio-playout.png')}">
      <div class="title">{WORDS["title"]}</div><div class="sub">{WORDS["subtitle"]}</div>
      <img class="shot" src="{cover_shot}">
      <div class="meta">{WORDS["version"]} {html.escape(version)} · {date} · cesararaujo.net</div></div>"""]
    toc = []
    for i, (title, anchor, blocks) in enumerate(CHAPTERS, 1):
        subs = [b[1] for b in blocks if b[0] == "h2"]
        toc.append(f"<li><a href='#{anchor}'>{title}</a>" + (f"<ol type='a'>{''.join(f'<li>{s}</li>' for s in subs)}</ol>" if subs else "") + "</li>")
    parts.append(f"<div class='toc'><h2>{WORDS['contents']}</h2><ol>{''.join(toc)}</ol></div>")
    for i, (title, anchor, blocks) in enumerate(CHAPTERS, 1):
        parts.append(f"<h1 id='{anchor}'><span class='n'>{i}</span>{title}</h1>" + "".join(block(b) for b in blocks))
    page = f"<!doctype html><html lang='{LANG}'><head><meta charset='utf-8'><title>Sintoniza — {WORDS['subtitle']}</title><style>{CSS}</style></head><body>{''.join(parts)}</body></html>"
    with tempfile.TemporaryDirectory() as tmp:
        src = os.path.join(tmp, "manual.html")
        open(src, "w", encoding="utf-8").write(page)
        shutil.copy(src, os.path.splitext(PDF)[0] + ".html") if os.environ.get("KEEP_HTML") else None
        chrome = shutil.which("chromium") or shutil.which("chromium-browser") or shutil.which("google-chrome")
        if not chrome: sys.exit("Chromium (or Google Chrome) is needed to print the PDF.")
        subprocess.run([chrome, "--headless", "--no-sandbox", "--disable-gpu", "--no-pdf-header-footer",
                        f"--user-data-dir={os.path.join(tmp, 'profile')}", f"--print-to-pdf={os.path.abspath(PDF)}", "file://" + src],
                       check=True, capture_output=True)
    print("→", PDF)

build()
