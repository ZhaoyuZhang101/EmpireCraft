import json
import re
import sys
from pathlib import Path


def find_mod_root(start: Path) -> Path:
    current = start.resolve()
    for candidate in [current, *current.parents]:
        if (candidate / "Scripts" / "Regimes" / "RegimeSystem.cs").exists():
            return candidate
    return start.resolve()


def strip_json_comments(text: str) -> str:
    text = re.sub(r"//.*", "", text)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    return text


def collect_keys(configs_dir: Path) -> tuple[list[str], list[str], list[str]]:
    kingdom_keys: set[str] = set()
    city_keys: set[str] = set()
    army_entries: dict[str, int] = {}

    for file in configs_dir.rglob("SystemConfig.json"):
        content = strip_json_comments(file.read_text(encoding="utf-8-sig"))
        data = json.loads(content)
        for regime in data.values():
            bureau = regime.get("bureau_config", {})
            kingdom_keys.update((bureau.get("kingdoms") or {}).keys())
            city_keys.update((bureau.get("cities") or {}).keys())
            for key, value in (bureau.get("armies") or {}).items():
                office_type = 999999
                if isinstance(value, dict):
                    office_type = int(value.get("type", office_type))
                current = army_entries.get(key)
                army_entries[key] = office_type if current is None else min(current, office_type)

    army_keys = [key for key, _ in sorted(army_entries.items(), key=lambda item: (item[1], item[0]))]
    return sorted(kingdom_keys), sorted(city_keys), army_keys


def replace_enum_block(source: str, enum_name: str, members: list[str]) -> str:
    marker = f"public enum {enum_name}"
    start = source.find(marker)
    if start < 0:
        raise RuntimeError(f"未找到枚举 {enum_name}")

    brace_start = source.find("{", start)
    if brace_start < 0:
        raise RuntimeError(f"枚举 {enum_name} 缺少起始花括号")

    depth = 0
    brace_end = -1
    for index in range(brace_start, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                brace_end = index
                break

    if brace_end < 0:
        raise RuntimeError(f"枚举 {enum_name} 缺少结束花括号")

    block = "{\n" + "".join(f"    {member},\n" for member in members) + "}"
    return source[:brace_start] + block + source[brace_end + 1 :]


def main() -> int:
    base_dir = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else find_mod_root(Path(__file__).resolve().parent)
    configs_dir = base_dir / "Scripts" / "Regimes" / "Configs"
    regime_system = base_dir / "Scripts" / "Regimes" / "RegimeSystem.cs"

    if not configs_dir.exists():
        print(f"未找到配置目录: {configs_dir}", file=sys.stderr)
        return 1
    if not regime_system.exists():
        print(f"未找到 RegimeSystem.cs: {regime_system}", file=sys.stderr)
        return 1

    kingdom_keys, city_keys, army_keys = collect_keys(configs_dir)
    if not kingdom_keys and not city_keys and not army_keys:
        print("没有在配置中找到任何 kingdoms/cities/armies 类别 key。", file=sys.stderr)
        return 1

    source = regime_system.read_text(encoding="utf-8-sig")
    source = replace_enum_block(source, "KingdomType", kingdom_keys)
    source = replace_enum_block(source, "CityType", city_keys)
    source = replace_enum_block(source, "ArmyOfficialType", army_keys)
    regime_system.write_text(source, encoding="utf-8")

    print("同步完成:")
    print(f"KingdomType: {len(kingdom_keys)} 项")
    print(f"CityType: {len(city_keys)} 项")
    print(f"ArmyOfficialType: {len(army_keys)} 项")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
