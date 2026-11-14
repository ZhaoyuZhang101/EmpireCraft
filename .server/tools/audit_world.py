# 两份存档之间核对：各国换了君主的，新君同故君是什么关系(子、孙、兄弟、同族、外姓)；族谱添了多少人；国库、城库、个人的钱。
#   python tools/audit_world.py <前一份存档文件夹> <后一份存档文件夹>
import collections, io, json, os, sys


def load(folder):
    world = json.load(io.open(os.path.join(folder, "world.json"), encoding="utf-8"))
    mod = json.load(io.open(os.path.join(folder, "EmpireCraftModData.json"), encoding="utf-8"))
    people = {}
    for clan in mod.get("specificClans") or []:
        for person in (clan.get("_cache") or {}).values():
            people[person["id"]] = person
    by_actor = {person["actor_id"]: person for person in people.values() if person.get("actor_id", -1) >= 0}
    return world, mod, people, by_actor


def name(text):
    return (text or "").replace(" ", "")


before_world, before_mod, before_people, before_actor = load(sys.argv[1])
after_world, after_mod, after_people, after_actor = load(sys.argv[2])
years = (after_world["world_time"] - before_world["world_time"]) / 60
print("相隔 %.1f 年(%d 年 → %d 年)" % (years, before_world["world_time"] // 60 + 1, after_world["world_time"] // 60 + 1))

kinds = collections.Counter()
examples = collections.defaultdict(list)
after_kings = {k["d"]["id"]: k for k in after_world["kingdoms"]}
for kingdom in before_world["kingdoms"]:
    kid = kingdom["d"]["id"]
    later = after_kings.get(kid)
    if later is None:
        kinds["亡国"] += 1
        continue
    old, new = kingdom.get("king"), later.get("king")
    if old is None or old == new:
        continue
    if new is None:
        kinds["君位空着"] += 1
        examples["君位空着"].append(name(later["d"]["name"]))
        continue
    old_person = before_actor.get(old) or after_actor.get(old)
    new_person = after_actor.get(new)
    if old_person is None or new_person is None:
        kind = "族谱对不上"
    else:
        all_people = dict(before_people)
        all_people.update(after_people)
        parents = {new_person.get("father"), new_person.get("mother")}
        grand = set()
        for parent in parents:
            p = all_people.get(parent)
            if p:
                grand |= {p.get("father"), p.get("mother")}
        old_parents = {old_person.get("father"), old_person.get("mother")} - {None, -1}
        if old_person["id"] in parents:
            kind = "子嗣"
        elif old_person["id"] in grand:
            kind = "孙辈"
        elif old_parents & (parents - {None, -1}):
            kind = "兄弟姐妹"
        elif new_person.get("specific_clan_id") == old_person.get("specific_clan_id"):
            kind = "同族旁支"
        else:
            kind = "外姓"
    kinds[kind] += 1
    if len(examples[kind]) < 4:
        examples[kind].append("%s：%s → %s" % (name(later["d"]["name"]), name(old_person and old_person.get("name")), name(new_person and new_person.get("name"))))
print("换了君主的 %d 国：%s" % (sum(v for k, v in kinds.items() if k != "亡国"), dict(kinds)))
for kind, rows in examples.items():
    for row in rows:
        print("   [%s] %s" % (kind, row))

born = [p for p in after_people.values() if p["id"] not in before_people]
alive = [p for p in born if p.get("is_alive")]
print("族谱新添 %d 人(在世 %d，其中已落成实体 %d)" % (len(born), len(alive), sum(1 for p in alive if p.get("actor_id", -1) >= 0)))


def money(mod, key):
    rows = mod.get(key) or []
    values = [row.get("Money") or 0 for row in rows]
    return len(values), sum(values), sum(1 for v in values if v > 0)


for key, label in (("kingdomExtraData", "国库"), ("cityExtraData", "城库")):
    n0, s0, p0 = money(before_mod, key)
    n1, s1, p1 = money(after_mod, key)
    print("%s：合计 %d → %d；有钱的 %d/%d → %d/%d" % (label, s0, s1, p0, n0, p1, n1))
coins0 = sum((u["d"].get("money") or 0) for u in before_world["units"])
coins1 = sum((u["d"].get("money") or 0) for u in after_world["units"])
print("人物身上的钱：合计 %d → %d(人 %d → %d)" % (coins0, coins1, len(before_world["units"]), len(after_world["units"])))
