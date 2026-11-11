# 诉求目标规则的一致性检查：ClaimRules.json 各决议 target 里用到的名字、_target_vocabulary 名字表、
# 游戏代码 Scripts/GeneralSystems/ClaimTargetRules.cs 里认的名字、三份语言文件里"没过哪条"的文字，要对得上。
# 用法：python Tools/check_claim_targets.py   (在模组根目录；有问题退出码 1)
import json, os, re, sys

sys.stdout.reconfigure(encoding='utf-8')
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
rules = json.load(open(os.path.join(root, 'ClaimRules.json'), encoding='utf-8-sig'))
code = open(os.path.join(root, 'Scripts', 'GeneralSystems', 'ClaimTargetRules.cs'), encoding='utf-8-sig').read()
problems = []


def method_cases(name):
    """方法体里的 case "xxx": 名字"""
    start = code.index(name)
    brace = code.index('{', start)
    depth, k = 0, brace
    while True:
        if code[k] == '{':
            depth += 1
        elif code[k] == '}':
            depth -= 1
            if depth == 0:
                break
        k += 1
    return set(re.findall(r'case "([a-z_]+)"', code[brace:k]))


# 候选来源：国家的在 KingdomPool 的 case 里(另加默认的 foreign)；城、人物的写在各自的 Collect 里；
# 法理头衔、宗教只有决议自带的候选(builtin，见 CollectOthers)
in_code = {
    ('pools', 'kingdom'): method_cases('KingdomPool(Context c, string pool)') | {'foreign'},
    ('pools', 'city'): {'core_cities', 'builtin'},
    ('pools', 'actor'): {'heir_candidates', 'emperor_siblings', 'builtin'},
    ('pools', 'title'): {'builtin'},
    ('pools', 'religion'): {'builtin'},
    ('rules', 'kingdom'): method_cases('bool PassKingdom('),
    ('rules', 'city'): method_cases('bool PassCity('),
    ('rules', 'actor'): method_cases('bool PassActor('),
    ('rules', 'title'): set(),
    ('rules', 'religion'): set(),
    ('prefer', 'kingdom'): method_cases('float ScoreKingdom('),
    ('prefer', 'city'): {'population'},
    ('prefer', 'actor'): method_cases('void CollectActors('),
    ('prefer', 'title'): set(),
    ('prefer', 'religion'): set(),
}
for pool, marker in (('core_cities', 'spec.Pool != "core_cities"'), ('heir_candidates', 'spec.Pool != "heir_candidates"'),
                     ('emperor_siblings', 'spec.Pool == "emperor_siblings"'), ('builtin', 'spec.Pool == "builtin"')):
    if marker not in code:
        problems.append(f'代码里找不到候选来源 {pool} 的判断({marker})，检查脚本要跟着改')
NUMBERS = ('our_power_min', 'their_power_min', 'army_share_min', 'army_share_max', 'min_cities')
for name in NUMBERS:
    if f'entry.Value<float?>("{name}")' not in code and f'entry.Value<int?>("{name}")' not in code:
        problems.append(f'代码不读数值门槛 {name}')
    if name not in (rules.get('_target_vocabulary') or {}).get('numbers', {}):
        problems.append(f'名字表(_target_vocabulary.numbers)里没有 {name}')
vocabulary = rules.get('_target_vocabulary') or {}
for (section, kind), names in in_code.items():
    listed = set((vocabulary.get(section) or {}).get(kind) or {})
    for name in sorted(names - listed):
        problems.append(f'代码认 {section}.{kind}.{name}，名字表(_target_vocabulary)里没有')
    for name in sorted(listed - names):
        problems.append(f'名字表里有 {section}.{kind}.{name}，代码不认')

kinds = set(vocabulary.get('kinds') or {})
claims_dir = os.path.join(root, 'Scripts', 'Regimes', 'TemporaryFactions', 'Claims')


def claim_code(claim):
    path = os.path.join(claims_dir, f'TempFac_{claim}.cs')
    return open(path, encoding='utf-8-sig').read() if os.path.exists(path) else ''


# 代码里写死的 claim_target_ 语言键(拼出来的 claim_target_fail_<规则> 另查)
used_keys = set()
sources = [os.path.join(root, 'Scripts', 'GeneralSystems', 'ClaimTargetRules.cs'),
           os.path.join(root, 'Scripts', 'UI', 'Windows', 'ClaimTargetWindow.cs')]
sources += [os.path.join(claims_dir, name) for name in os.listdir(claims_dir) if name.endswith('.cs')]
for source in sources:
    for key in re.findall(r'"(claim_target_[a-z0-9_]+)"', open(source, encoding='utf-8-sig').read()):
        if key != 'claim_target_fail_':
            used_keys.add(key)
used_rules = set()
count = 0
for claim, body in rules.items():
    if claim.startswith('_') or not isinstance(body, dict) or 'target' not in body:
        continue
    target = body['target']
    entries = target if isinstance(target, list) else [target]
    for entry in entries:
        count += 1
        kind = entry.get('kind', 'kingdom')
        if kind not in kinds:
            problems.append(f'{claim}: kind {kind} 不认识')
            continue
        if entry.get('pool') not in in_code[('pools', kind)]:
            problems.append(f'{claim}: pool {entry.get("pool")} 不认识')
        for key in ('rules', 'auto_rules'):
            for name in entry.get(key, []):
                used_rules.add(name)
                if name not in in_code[('rules', kind)]:
                    problems.append(f'{claim}: {key} 里的 {name} 不认识')
        for name in (entry.get('prefer') or {}):
            if name not in in_code[('prefer', kind)]:
                problems.append(f'{claim}: prefer 里的 {name} 不认识')
        if kind != 'kingdom':
            for key in NUMBERS:
                if key in entry:
                    problems.append(f'{claim}: {key} 只对国家有用')
        if entry.get('pool') == 'builtin' and 'BuiltinCandidates()' not in claim_code(claim):
            problems.append(f'{claim}: 候选来源是 builtin，决议代码里却没有 BuiltinCandidates')

# 写了 target 的决议、代码里只照 target 挑目标的决议(CheckCondition 走 PickRuleTarget)、名字表的 required 要是同一批：
# 代码走规则而数据没写 = 这条决议永远提不出来；数据写了而代码没接 = 神力能选、选了不算
with_target = {claim for claim, body in rules.items() if not claim.startswith('_') and isinstance(body, dict) and 'target' in body}
uses_rules = {name[len('TempFac_'):-3] for name in os.listdir(claims_dir)
              if name.endswith('.cs') and 'PickRuleTarget()' in open(os.path.join(claims_dir, name), encoding='utf-8-sig').read()}
required = set(vocabulary.get('required') or [])
for claim in sorted(uses_rules - with_target):
    problems.append(f'{claim}: 代码只照目标规则挑目标，ClaimRules.json 里却没有 target')
for claim in sorted(with_target - uses_rules):
    problems.append(f'{claim}: 写了 target，决议代码却没接上(CheckCondition 里没有 PickRuleTarget)')
for claim in sorted(uses_rules ^ required):
    problems.append(f'{claim}: 名字表的 required 与代码对不上')

all_rules = in_code[('rules', 'kingdom')] | in_code[('rules', 'city')] | in_code[('rules', 'actor')]
for locale in ('cz', 'ch', 'en'):
    text = json.load(open(os.path.join(root, 'Locales', locale + '.json'), encoding='utf-8-sig'))
    for name in sorted(all_rules):
        if 'claim_target_fail_' + name not in text:
            problems.append(f'Locales/{locale}.json 缺 claim_target_fail_{name}')
    for key in ('claim_target_fail_our_power', 'claim_target_fail_their_power', 'claim_target_fail_min_cities',
                'claim_target_fail_army_share_min', 'claim_target_fail_army_share_max'):
        if key not in text:
            problems.append(f'Locales/{locale}.json 缺 {key}')
    # 窗口、引擎、各决议代码里用到的 claim_target_ 文字
    for key in sorted(used_keys):
        if key not in text:
            problems.append(f'Locales/{locale}.json 缺 {key}')

print(f'目标规则 {count} 条({len(with_target)} 条决议)，代码认的规则 {len(all_rules)} 个，语言键 {len(used_keys)} 个')
for problem in problems:
    print('  ✗', problem)
print('没有问题' if not problems else f'{len(problems)} 处问题')
sys.exit(1 if problems else 0)
