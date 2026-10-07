"""Check literal sprite paths, transparent icons, and culture coverage against this installed game.
Requires Pillow and UnityPy. Run: python Tools/IconForge/audit_icons.py
"""
from pathlib import Path
import re
import sys
import UnityPy
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
GAME = ROOT.parent.parent / "worldbox_Data"
manager = UnityPy.load(str(GAME / "globalgamemanagers"))
resource = next(obj.read() for obj in manager.objects if obj.type.name == "ResourceManager")
native = {path.lower() for path, _ in resource.m_Container}
paths = {}
for source in (ROOT / "Scripts").rglob("*.cs"):
    for line_no, line in enumerate(source.read_text("utf-8-sig").splitlines(), 1):
        if line.lstrip().startswith("//") or "HiResIconFilter" in source.name:
            continue
        for path in re.findall(r'"((?:ui/|plots/|civ/icons/|Tab)[^"\r\n]+)"', line):
            if any(char in path for char in "{$+<") or path.endswith("/"):
                continue
            path = path.removesuffix(".png")
            paths.setdefault(path, []).append(f"{source.relative_to(ROOT)}:{line_no}")
missing = []
for path, sources in sorted(paths.items()):
    if (ROOT / "GameResources" / (path + ".png")).is_file() or path.lower() in native:
        continue
    # These original sliced sprites are held by prefabs, and are now resolved by exact name.
    if path in ("ui/special/windowInnerSliced", "ui/special/special_buttonRed"):
        continue
    missing.append((path, sources))
blank = []
count = 0
for base in (ROOT / "GameResources/ui/icons", ROOT / "GameResources/civ/icons"):
    for file in base.rglob("*.png"):
        count += 1
        with Image.open(file) as image:
            if not image.convert("RGBA").getchannel("A").getbbox():
                blank.append(str(file.relative_to(ROOT)))
for file in (ROOT / "GameResources").glob("Tab*.png"):
    count += 1
    with Image.open(file) as image:
        if not image.convert("RGBA").getchannel("A").getbbox():
            blank.append(str(file.relative_to(ROOT)))
cultures = list((ROOT / "Locales/Cultures").glob("Culture_*"))
for folder in cultures:
    name = folder.name.removeprefix("Culture_")
    if not (folder / "icon.png").is_file() and not (ROOT / f"GameResources/ui/icons/cultures/{name}.png").is_file():
        missing.append((f"culture:{name}", [str(folder.relative_to(ROOT))]))
print(f"Checked {len(paths)} literal paths, {count} mod icons, {len(cultures)} cultures and {len(native)} native resource paths.")
for path, sources in missing:
    print("MISSING", path, ", ".join(sources))
for path in blank:
    print("BLANK", path)
print(f"Missing: {len(missing)}; blank icons: {len(blank)}")
sys.exit(bool(missing or blank))
