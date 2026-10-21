"""Builds installer/payload.zip and the manual zip from the shareable pack (build/share/ar, made by
lo_mod.py language-clean from packs/ar: only translated lines, the pack's glyphs, language.ini) and the
pictures mod (build/share/ar-images, made by lo_mod.py texture-pack: the tutorial pictures in Arabic).
Refuses to build unless the pack's language.ini says direction=rtl and every file of both matches the copy
installed in the game folder (clean-1.0.2/mods/<id>, the one played): each kept text entry is the deployed
entry, every other file the same bytes, so the player gets what was tested in game.
  python -X utf8 installer/build_payload.py
Payload:
  ar/<pack path>      the pack, installed as <game>/mods/ar/<pack path>
  ar-images/<path>    the pictures mod, installed as <game>/mods/ar-images/<path>
  licenses/...        the font licences: Rubik (the window), Alyamama (the Arabic letters in the game fonts)
  manifest.json       version, MD5 and size of every file, by its path under mods/
Also writes RELEASE/<name> (Manual).zip: mods/ar/..., mods/ar-images/... + the manual install notes."""
import hashlib
import json
import os
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SHARE = os.path.join(ROOT, "build", "share")
DEPLOYED = os.path.join(ROOT, "clean-1.0.2", "mods")
PACKS = ("ar", "ar-images")
VERSION = "1.0.2"
NAME = "Lost Odyssey - Arabic v" + VERSION


def files(root):
    out = {}
    for dp, _, fs in os.walk(root):
        for f in fs:
            p = os.path.join(dp, f)
            out[os.path.relpath(p, root).replace("\\", "/")] = p
    return out


def main():
    ini = open(os.path.join(SHARE, "ar", "language.ini"), encoding="utf-8-sig").read()
    if "direction=rtl" not in ini.replace(" ", ""):
        sys.exit("language.ini has no direction=rtl: the recomp would draw the Arabic left to right")
    share, deployed = {}, {}
    for pack in PACKS:
        share.update({pack + "/" + rel: p for rel, p in files(os.path.join(SHARE, pack)).items()})
        deployed.update({pack + "/" + rel: p for rel, p in files(os.path.join(DEPLOYED, pack)).items()})
    bad = []
    for rel, p in share.items():
        if rel not in deployed:
            bad.append(rel + " (not deployed)")
        elif rel.startswith("ar/text/"):
            mine = json.load(open(p, encoding="utf-8"))
            live = json.load(open(deployed[rel], encoding="utf-8"))
            if any(live.get(k) != v for k, v in mine.items()):
                bad.append(rel)
        elif open(p, "rb").read() != open(deployed[rel], "rb").read():
            bad.append(rel)
    if bad:
        sys.exit(f"{len(bad)} file(s) differ from the pack in clean-1.0.2 (install and test first): {bad[:5]}")
    data = {rel: open(p, "rb").read() for rel, p in sorted(share.items())}
    manifest = {
        "version": VERSION,
        "files": {rel: hashlib.md5(b).hexdigest() for rel, b in data.items()},
        "sizes": {rel: len(b) for rel, b in data.items()},
    }
    out = os.path.join(HERE, "payload.zip")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for rel, b in data.items():
            z.writestr(rel, b)
        z.write(os.path.join(HERE, "ui", "Rubik-OFL.txt"), "licenses/Rubik-OFL.txt")
        z.write(os.path.join(HERE, "ui", "AlYamama-OFL.txt"), "licenses/AlYamama-OFL.txt")
        z.writestr("manifest.json", json.dumps(manifest, indent=1))
    print(f"{len(data)} file(s) ({sum(map(len, data.values())) / 1e6:.1f} MB), {os.path.getsize(out) / 1e6:.1f} MB -> {out}")

    rel_dir = os.path.join(ROOT, "RELEASE")
    os.makedirs(rel_dir, exist_ok=True)
    manual = os.path.join(rel_dir, NAME + " (Manual).zip")
    with zipfile.ZipFile(manual, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for rel, b in data.items():
            z.writestr("mods/" + rel, b)
        z.write(os.path.join(HERE, "INSTALL_MANUAL.txt"), "Arabic - INSTALL_MANUAL.txt")
        z.write(os.path.join(HERE, "ui", "AlYamama-OFL.txt"), "Arabic - AlYamama font licence (OFL).txt")
    print(f"manual zip {os.path.getsize(manual) / 1e6:.1f} MB -> {manual}")


if __name__ == "__main__":
    main()
