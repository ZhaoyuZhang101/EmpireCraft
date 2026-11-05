"""把政体配置里新出现的键名追加进 Scripts/Regimes/RegimeSystem.cs 的枚举。

枚举的数值进存档，所以这里只在末尾追加，不排序、不删除、不改已有成员和注释：
  RegimeType        ← 各 SystemConfig.json 最外层的政体键名
  KingdomType       ← bureau_config.kingdoms 的键
  CityType          ← bureau_config.cities 的键
  ArmyOfficialType  ← bureau_config.armies 的键
default_kingdom、city_type、secede_member_types、direct_member_types 只检查不追加：它们指向的类别
应当在某个政体的 kingdoms / cities 里定义，指错了多半是笔误，追加进枚举只会把错误藏起来。

用法：python regime_enum_sync.py [模组根目录]
"""
import json
import re
import sys
from pathlib import Path

ENUMS = ["RegimeType", "KingdomType", "CityType", "ArmyOfficialType"]
IDENT = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")


def find_mod_root(start: Path) -> Path:
    current = start.resolve()
    for candidate in [current, *current.parents]:
        if (candidate / "Scripts" / "Regimes" / "RegimeSystem.cs").exists():
            return candidate
    return start.resolve()


def strip_json_comments(text: str) -> str:
    """去掉 // 与 /* */ 注释和尾逗号；跳过字符串，免得把 "http://" 当成注释。"""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            out.append(text[i:j + 1])
            i = j + 1
        elif text.startswith("//", i):
            e = text.find("\n", i)
            i = n if e < 0 else e
        elif text.startswith("/*", i):
            e = text.find("*/", i + 2)
            i = n if e < 0 else e + 2
        else:
            out.append(c)
            i += 1
    return re.sub(r",(\s*[}\]])", r"\1", "".join(out))


def collect(configs_dir: Path):
    found = {name: [] for name in ENUMS}
    refs = {"KingdomType": [], "CityType": []}
    problems = []

    def add(name, key, where):
        if not isinstance(key, str) or not IDENT.match(key):
            problems.append(f"{where}: {key!r} 不是合法的枚举名，跳过")
        elif key not in found[name]:
            found[name].append(key)

    for file in sorted(configs_dir.rglob("SystemConfig.json")):
        rel = file.relative_to(configs_dir).as_posix()
        try:
            data = json.loads(strip_json_comments(file.read_text(encoding="utf-8-sig")))
        except ValueError as error:
            raise RuntimeError(f"{rel} 读取失败：{error}") from error
        if not isinstance(data, dict):
            raise RuntimeError(f"{rel} 最外层不是对象")
        for regime_key, regime in data.items():
            add("RegimeType", regime_key, rel)
            if not isinstance(regime, dict):
                continue
            bureau = regime.get("bureau_config") or {}
            for scope, enum_name in (("kingdoms", "KingdomType"), ("cities", "CityType"), ("armies", "ArmyOfficialType")):
                for key, setting in (bureau.get(scope) or {}).items():
                    add(enum_name, key, f"{rel} {regime_key}.{scope}")
                    if scope == "kingdoms" and isinstance(setting, dict) and setting.get("city_type"):
                        refs["CityType"].append((setting["city_type"], f"{rel} {regime_key}.kingdoms.{key}.city_type"))
            if regime.get("default_kingdom"):
                refs["KingdomType"].append((regime["default_kingdom"], f"{rel} {regime_key}.default_kingdom"))
            for field in ("secede_member_types", "direct_member_types"):
                for value in regime.get(field) or []:
                    refs["KingdomType"].append((value, f"{rel} {regime_key}.{field}"))
    return found, refs, problems


def scan_enum(source: str, enum_name: str):
    """返回 (成员列表, 最后一个有效字符的位置, 右花括号位置)。跳过注释。"""
    m = re.search(r"\benum\s+" + enum_name + r"\b", source)
    if not m:
        raise RuntimeError(f"未找到枚举 {enum_name}")
    brace = source.find("{", m.end())
    if brace < 0:
        raise RuntimeError(f"枚举 {enum_name} 缺少起始花括号")
    i, depth, last_sig, body = brace + 1, 1, brace, []
    while i < len(source):
        if source.startswith("//", i):
            e = source.find("\n", i)
            i = len(source) if e < 0 else e
            continue
        if source.startswith("/*", i):
            e = source.find("*/", i + 2)
            i = len(source) if e < 0 else e + 2
            continue
        c = source[i]
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                members = [part.split("=")[0].strip() for part in "".join(body).split(",")]
                return [x for x in members if x], last_sig, i
        if not c.isspace():
            last_sig = i
        body.append(c)
        i += 1
    raise RuntimeError(f"枚举 {enum_name} 缺少结束花括号")


def append_members(source: str, enum_name: str, new_members, nl: str) -> str:
    if not new_members:
        return source
    members, last_sig, close = scan_enum(source, enum_name)
    # 缩进照第一个成员所在行
    indent = "    "
    if members:
        body_start = source.find("{", source.find("enum " + enum_name)) + 1
        line = re.search(r"^([ \t]*)" + re.escape(members[0]) + r"\b", source[body_start:close], re.M)
        if line:
            indent = line.group(1)
    comma = "," if source[last_sig] not in ",{" else ""
    line_start = source.rfind("\n", 0, close) + 1
    added = "".join(f"{indent}{name},{nl}" for name in new_members)
    if source[line_start:close].strip() == "":
        pos, text = line_start, added
    else:
        pos, text = close, nl + added
    return source[:last_sig + 1] + comma + source[last_sig + 1:pos] + text + source[pos:]


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

    found, refs, problems = collect(configs_dir)
    raw = regime_system.read_bytes()
    bom = raw.startswith(b"\xef\xbb\xbf")
    source = raw.decode("utf-8-sig")
    nl = "\r\n" if "\r\n" in source else "\n"

    lines = []
    updated = source
    existing_all = {}
    for name in ENUMS:
        existing, _, _ = scan_enum(updated, name)
        existing_all[name] = set(existing)
        new = [key for key in found[name] if key not in existing_all[name]]
        updated = append_members(updated, name, new, nl)
        existing_all[name].update(new)
        unused = [key for key in existing if key not in found[name]]
        line = f"{name}: 已有 {len(existing)} 项"
        line += f"，追加 {len(new)} 项({'、'.join(new)})" if new else "，无新增"
        if unused:
            line += f"；配置里没用到但保留(旧存档要用)：{'、'.join(unused)}"
        lines.append(line)

    for enum_name, items in refs.items():
        for value, where in items:
            if value not in existing_all[enum_name]:
                problems.append(f"{where} 指向的 {value} 不在 {enum_name} 里(也没有哪个政体定义它)，游戏读这个政体会出错")

    if updated != source:
        regime_system.write_bytes((b"\xef\xbb\xbf" if bom else b"") + updated.encode("utf-8"))
        print("同步完成，RegimeSystem.cs 已追加新成员，请重新编译模组:")
    else:
        print("同步完成，代码枚举已是最新，没有改动:")
    for line in lines:
        print(line)
    for problem in problems:
        print("注意：" + problem)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
