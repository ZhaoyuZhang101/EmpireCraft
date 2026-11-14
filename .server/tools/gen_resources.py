# 从原版资源表(AssetRipper 导出的 ResourceLibrary.cs)抽出服务器要的几项，生成 Shim/World/Resources.g.cs。
# 只抄名字和数(哪种货、是不是吃的、能顶多少饱、价钱、仓储上限……)，不抄原版代码。
#   python tools/gen_resources.py <ResourceLibrary.cs>
import io, os, re, sys

KEEP = {"type", "food", "maximum", "money_cost", "restore_happiness", "restore_health", "restore_mana", "restore_nutrition",
        "restore_stamina", "supply_bound_give", "supply_bound_take", "supply_give"}
src = io.open(sys.argv[1], encoding="utf-8").read()
body = src[src.index("public override void init()"):]
assets, order, current = {}, [], None


def value(text):
    text = text.strip().rstrip(",;").strip()
    if text.startswith("ResType."):
        return "ResType." + text.split(".", 1)[1]
    if text in ("true", "false"):
        return text
    if re.fullmatch(r"-?\d+(\.\d+)?f?", text):
        return text
    return None


for statement in re.split(r";\s*\n", body):
    statement = statement.strip()
    clone = re.search(r'clone\("([^"]+)",\s*"([^"]+)"\)', statement)
    added = re.search(r"add\(new ResourceAsset\s*\{(.*)\}\s*\)", statement, re.S)
    if clone:
        new_id, template = clone.groups()
        assets[new_id] = dict(assets.get(template, {}))
        current = new_id
        order.append(new_id)
        continue
    if added:
        fields = {}
        for line in added.group(1).split("\n"):
            match = re.match(r"\s*(\w+)\s*=\s*(.+)", line)
            if match and match.group(1) in KEEP | {"id"}:
                if match.group(1) == "id":
                    fields["id"] = match.group(2).strip().rstrip(",").strip('"')
                else:
                    v = value(match.group(2))
                    if v is not None:
                        fields[match.group(1)] = v
        new_id = fields.pop("id")
        assets[new_id] = fields
        current = new_id
        order.append(new_id)
        continue
    for line in statement.split("\n"):
        match = re.match(r"\s*t\.(\w+)\s*=\s*(.+)", line)
        if match and current and match.group(1) in KEEP:
            v = value(match.group(2))
            if v is not None:
                assets[current][match.group(1)] = v

lines = ["// 生成的：tools/gen_resources.py 从原版资源表抽的名字和数(只有数，没有原版代码)。别手改，重跑生成。",
         "// 服务器里原版资源表是空的：模组问\"面包是不是吃的\"问不出来，城里存着粮也当没有，人就饿死了", "internal static partial class ShimResources", "{",
         "    internal static void Init()", "    {"]
for asset_id in order:
    if asset_id.startswith("$"):
        continue
    fields = ", ".join("%s = %s" % (k, v) for k, v in sorted(assets[asset_id].items()))
    lines.append('        Add(new ResourceAsset { id = "%s"%s });' % (asset_id, (", " + fields) if fields else ""))
lines += ["    }", "}", ""]
out = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Shim", "World", "Resources.g.cs")
io.open(out, "w", encoding="utf-8").write("\n".join(lines))
print("%d resources -> %s" % (len([a for a in order if not a.startswith("$")]), out))
foods = [a for a in order if assets[a].get("type") == "ResType.Food"]
print("food:", ", ".join(foods))
