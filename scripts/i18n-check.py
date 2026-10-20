"""Checks the translations (src/Playout.App/i18n/*.json) against the app's texts.

Collects every English text the app shows (XAML literals, and the C# strings given to L.T / L.F / Plural) and
lists those without a translation in each language, and translations whose {0} placeholders differ.
Run after adding or changing texts:  python3 scripts/i18n-check.py
"""
import re, glob, json, sys, html
import os
root = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src')
keys = {}
def add(k, where):
    if re.search(r'[A-Za-zÀ-ÿ]', k): keys.setdefault(k, where)
for f in glob.glob(f'{root}/Playout.App/*.axaml'):
    if f.endswith('App.axaml'): continue
    s = open(f).read()
    for m in re.finditer(r'\s(Text|Content|Header|ToolTip\.Tip|PlaceholderText|Title|Watermark)="([^"]*)"', s):
        v = m.group(2)
        if v.startswith('{') and not v.startswith('{}'): continue
        if v.startswith('{}'): v = v[2:]
        add(html.unescape(v).replace('&#10;', '\n'), f.split('/')[-1])
def unescape(lit):
    return lit.encode('utf-8').decode('unicode_escape').encode('latin-1').decode('utf-8')
lit = r'"((?:[^"\\]|\\.)*)"'
for f in glob.glob(f'{root}/Playout.App/*.cs') + glob.glob(f'{root}/Playout.App/Controls/*.cs') + glob.glob(f'{root}/Playout.Engine/*.cs'):
    s = open(f).read()
    for m in re.finditer(r'L\.[TF]\(\s*' + lit, s): add(unescape(m.group(1)), f.split('/')[-1])
    for m in re.finditer(r'Plural\([^,]+,\s*' + lit + r',\s*' + lit, s):
        add(unescape(m.group(1)), f.split('/')[-1]); add(unescape(m.group(2)), f.split('/')[-1])
    for m in re.finditer(r'L\.F\(\s*\w[^"]*\?\s*' + lit + r'\s*:\s*' + lit, s):
        add(unescape(m.group(1)), f.split('/')[-1]); add(unescape(m.group(2)), f.split('/')[-1])
# the ternary inside L.F(automatic ? ... ) for the library scan messages
s = open(f'{root}/Playout.App/LibraryService.cs').read()
for k in re.findall(r'"(Library (?:auto )?scan (?:stopped|done): \{0\})"', s): add(k, 'LibraryService.cs')
# names translated where they are shown
for k in ['Red','Orange','Yellow','Green','Teal','Blue','Purple','Pink','Grey','Jingle','Music','Commercial','Microphone','News','Weather','Time','Bell','Effect','Heart',
          'Advert','Stream','Programme','playlist','cart','jingle','commercial','other','INTRO','OUTRO']:
    add(k, 'names')
add("Automatic (this computer's language)", 'Localization.cs')
for k in ['Date','Start','End','Duration','Seconds','Composer','Label','ISRC','Plays','Total time','Total seconds','Break','Type','Source','File']:
    add(k, 'Reports.cs')
ph = lambda s: sorted(re.findall(r'\{(\d+)(?::[^}]*)?\}', s))
problems = 0
for lang in ('pt', 'fr'):
    table = json.load(open(os.path.join(root, 'Playout.App', 'i18n', lang + '.json'), encoding='utf-8'))
    for k, where in keys.items():
        if k not in table:
            print(f'{lang}: no translation ({where}): {k!r}'); problems += 1
        elif ph(table[k]) != ph(k):
            print(f'{lang}: placeholders differ: {k!r} -> {table[k]!r}'); problems += 1
print(f'{len(keys)} texts, {problems} problems')
sys.exit(1 if problems else 0)
