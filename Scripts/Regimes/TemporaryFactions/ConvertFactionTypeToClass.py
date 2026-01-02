#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
从脚本所在目录解析 TemporaryFactionType.cs 并在同目录生成 TemporaryFactions/TempFac_*.cs
无需任何命令行参数。
"""

import re
from pathlib import Path
EMPIRE_PRE = "TempFac"
EMPIRE_SUF = "Empire empire = GetEmpire();"
KINGDOM_PRE = "KingdomMind"
KINGDOM_SUF = "var kingdom = GetKingdom();"
TEMPLATE = """using NeoModLoader.services;
using EmpireCraft.Scripts.Layer;
namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.{class_type};

public class {TempFac}_{type_name} : TemporaryFaction
{{
    public override TemporaryFaction Clone(FixedFaction faction)
    {{
        var res = new {TempFac}_{type_name}();
        res.Init(faction);
        return res;
    }}
    
    public override void Execute()
    {{
        LogService.LogInfo($\"执行{{this.type}}\");
        {suf}
        FinishedAction();
        End();
    }}
    
    public override bool CheckCondition()
    {{
        {suf}
        return false;
    }}
}}
"""

def find_enum_file(root: Path) -> Path:
    """优先取同目录下 TemporaryFactionType.cs；否则递归向下搜索一次。"""
    direct = root / "TemporaryFactionType.cs"
    if direct.exists():
        return direct
    for p in root.rglob("TemporaryFactionType.cs"):
        return p
    raise FileNotFoundError("未找到 TemporaryFactionType.cs（请把它放到脚本同目录，或同目录的子目录中）")

def parse_enum(enum_path: Path) -> list[str]:
    text = enum_path.read_text(encoding="utf-8")
    m = re.search(r"public\s+enum\s+TemporaryFactionType\s*\s*\{(?P<body>.*?)\}", text, re.S)
    if not m:
        raise RuntimeError("没有在文件中找到 public enum TemporaryFactionType { ... }")
    body = re.sub(r"//.*", "", m.group("body"))  # 去掉行内注释
    body = re.sub(r"\[[^\]]*\]\s*", "", body)
    items = []
    for line in body.splitlines():
        line = line.strip().rstrip(",")
        if not line:
            continue
        name = line.split("=", 1)[0].strip()
        if name:
            items.append(name)
    # 去重保持顺序
    seen, ordered = set(), []
    for it in items:
        if it not in seen:
            ordered.append(it)
            seen.add(it)
    return ordered

def main():
    script_dir = Path(__file__).resolve().parent
    enum_file = find_enum_file(script_dir)
    out_dir = script_dir
    out_dir.mkdir(parents=True, exist_ok=True)

    items = parse_enum(enum_file)
    if not items:
        print("未解析到任何枚举项。")
        return

    created, skipped = 0, 0
    for name in items:
        if name.__contains__("国_"):
            pre = KINGDOM_PRE
            name = name.replace("国_", "")
            out_path = out_dir /"KingdomMinds"/ f"{pre}_{name}.cs"
            class_type = "KingdomMinds"
            suffix = KINGDOM_SUF
        else:
            pre = EMPIRE_PRE
            out_path = out_dir /"Claims"/ f"{pre}_{name}.cs"
            class_type = "Claims"
            suffix = EMPIRE_SUF
        
        if out_path.exists():
            print(f"跳过（已存在）：{out_path.name}")
            skipped += 1
            continue
        out_path.write_text(TEMPLATE.format(class_type=class_type, TempFac=pre, type_name=name, suf=suffix), encoding="utf-8")
        print(f"已生成：{out_path.name}")
        created += 1

    print(f"完成。新建 {created} 个，跳过 {skipped} 个。")
    print(f"输出目录：{out_dir}")

if __name__ == "__main__":
    main()