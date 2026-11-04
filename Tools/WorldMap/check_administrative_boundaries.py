"""Validate dated administrative polygons and their preset references, read-only."""
import json
import pathlib
import sys

from check_scenario import check_boundary, errors


def check(root):
    path = root / "WorldMaps/administrative_boundaries.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    keys = set()
    areas = list(data["boundaries"])
    ming = root / "WorldMaps/administrative_boundaries_ming.json"
    if ming.exists():
        areas.extend(json.loads(ming.read_text(encoding="utf-8"))["boundaries"])
    for area in areas:
        key = (area["era"], area["polity"], area["division"])
        if key in keys:
            errors.append("Repeated area: " + str(key))
        keys.add(key)
        era = json.loads((root / "WorldMaps/eras" / (area["era"] + ".json")).read_text(encoding="utf-8"))
        polity = next((p for p in era["polities"] if p["id"] == area["polity"]), None)
        if polity is None or area["division"] not in ({"_core"} | {d["name"].split("|")[0] for d in polity.get("divisions", [])}):
            errors.append("Unknown administrative reference: " + str(key))
        if area["year"] != era["year"] or not area.get("source") or not area.get("precision"):
            errors.append("Missing/mismatched period or provenance: " + str(key))
        check_boundary(area.get("polygons"), str(key))
    if not data.get("attribution") or not data.get("license") or not data.get("source_sha256"):
        errors.append("Missing attribution, license or source checksum")
    if errors:
        raise SystemExit("\n".join(errors))
    print(f"ok: {len(keys)} dated historical administrative areas, references, rings and provenance")


if __name__ == "__main__":
    check(pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "."))
