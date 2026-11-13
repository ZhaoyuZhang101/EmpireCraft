// 剧本编辑器的数据一侧：照游戏里 Scripts/GeneralSystems/WorldMap/ScenarioEditing.cs 的规矩改一份剧本(scenario.json)，
// 两边存出来的文件互通。剧本对象就是 scenario.json 解析出来的那份(键名同文件：城 x/y/r/k，国家 c/cap/r/of/emp……)，
// 这里不认识的键原样留着，保存时一并写回。
//   · 还用着内置城表的剧本头一回改城或法理时，把内置城表(地区、城、天下名、通用帝国法理)整套拷进剧本(ensureOwnWorld)；
//   · 归属照开局的规矩算(ownerOf)：先各国点名的城与都城(写在前面的国家优先)，再各国认领的地区(几国都认领归最近的都城)；
//   · 刷的地块、地形、开发度编辑时放 Map 里，存盘、存撤销快照前才写回剧本(flushCells)，格子编号同 ScenarioCells.Pack；
//   · 撤销：每步改动前存一份快照(至多 30 份)，拖着刷、拖着划是一笔，只存一份。
(function () {
  const LAND_STEP = 0.2, TERRAIN_STEP = 0.1, UNDO_LIMIT = 30, NEAR_CITY_DEGREES = 6;
  const RELATION_KINDS = ["war", "truce", "alliance", "marriage", "pact", "protect", "tribute", "vassal", "union", "puppet", "friend", "feud"];
  const WAR_GOALS = ["夺城", "收复", "争法理", "劫掠", "侵略", "争夺资源", "吞并", "迫使附庸", "保护国", "租借"];
  const MUTUAL = new Set(["truce", "alliance", "marriage", "pact", "friend", "feud"]);
  const DECLINE_KEYS = ["age", "strain", "graft", "annexation", "bloat", "military", "factionalism", "levies"];
  const FRAMES = [
    { id: "eurasia", west: -25, east: 153, top: 77.5, sizes: [[16, 7], [32, 15], [40, 18]] },
    { id: "oldworld", west: -26, east: 155, centerLat: 21, sizes: [[14, 9], [28, 18], [34, 22]] },
    { id: "eastasia", west: 62, east: 152, top: 62, sizes: [[14, 10], [28, 20], [32, 23]] },
    { id: "westworld", west: -26, east: 64, centerLat: 21, sizes: [[10, 13], [20, 25], [24, 30]] }
  ];

  const S = {
    era: null, folder: "", dirty: false, revision: 0, builtin: null, cultureKeys: [], naming: {},
    land: new Map(), terrain: new Map(), develop: new Map(), landBase: new Map(),
    landRevision: 0, terrainRevision: 0, developRevision: 0, structureRevision: 0
  };
  let undos = [], stroke = false, strokeSaved = false, cellsDirty = false, owners = null, ownersRevision = -1;

  const clone = value => JSON.parse(JSON.stringify(value));
  const first = text => String(text ?? "").split("|")[0];
  const nameOf = item => item ? (item.zh || item.en || item.tw || "") : "";

  // ---------- 格子(同 ScenarioCells) ----------
  const pack = (i, j) => (j + 2000) * 10000 + (i + 5000);
  const unpack = key => [key % 10000 - 5000, Math.floor(key / 10000) - 2000];
  const cellKey = (lon, lat, step) => pack(Math.floor(lon / step), Math.floor(lat / step));

  function toMap(list) {
    const map = new Map();
    for (const paint of list || [])
      if (paint && Array.isArray(paint.cells) && paint.key) for (const cell of paint.cells) map.set(cell, paint.key);
    return map;
  }

  function fromMap(map) {
    if (!map || map.size === 0) return null;
    const groups = new Map();
    for (const [cell, key] of map) {
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(cell);
    }
    return [...groups.keys()].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0))
      .map(key => ({ key, cells: groups.get(key).sort((a, b) => a - b) }));
  }

  function loadCells() {
    S.land = toMap(S.era?.paint);
    S.terrain = toMap(S.era?.terrain);
    S.develop = toMap(S.era?.develop);
    cellsDirty = false;
    S.landRevision++; S.terrainRevision++; S.developRevision++;
  }

  function flushCells() {
    if (!cellsDirty || !S.era) return;
    for (const [field, map] of [["paint", S.land], ["terrain", S.terrain], ["develop", S.develop]]) {
      const list = fromMap(map);
      if (list) S.era[field] = list; else delete S.era[field];
    }
    cellsDirty = false;
  }

  // ---------- 打开、新建、保存 ----------
  function normalize(era) {
    for (const key of ["polities", "relations", "cores", "walls", "passes", "events"]) if (!Array.isArray(era[key])) era[key] = [];
    for (const key of ["ranks", "culture"]) if (!era[key] || typeof era[key] !== "object") era[key] = {};
    if (ownWorld(era) && !Array.isArray(era.regions)) era.regions = [];
    for (const item of era.events) {
      if (!Array.isArray(item.when)) item.when = [];
      if (!Array.isArray(item.then)) item.then = [];
    }
    return era;
  }

  function ownWorld(era = S.era) {
    return !!era && Array.isArray(era.cities) && (era.cities.length > 0 || era.map != null || era.own_cities === true);
  }

  function open(text, folder) {
    const era = normalize(JSON.parse(text));
    // 导入时的原图归属单放(几万格，不进撤销快照)，存盘时才写回
    S.landBase = toMap(era.paint_base || (era.exact ? era.paint : null));
    delete era.paint_base;
    S.era = era;
    S.folder = folder;
    S.dirty = false;
    undos = [];
    stroke = strokeSaved = false;
    owners = null;
    loadCells();
    S.revision++; S.structureRevision++;
  }

  function close() {
    S.era = null; S.folder = ""; S.dirty = false; undos = [];
    S.land = new Map(); S.terrain = new Map(); S.develop = new Map(); S.landBase = new Map();
    owners = null;
  }

  // 空白剧本(同 ScenarioEditing.CreateBlank)：地球地图上一座城也没有，留着内置的地区与帝国法理
  function blankText(folder, frame) {
    const era = {
      id: folder, zh: "空白剧本", tw: "空白剧本", en: "空白剧本", year: 1, focus: "east", order: 100, own_cities: true,
      cities: [], regions: clone(S.builtin.regions), tianxia: clone(S.builtin.tianxia), cores: clone(S.builtin.cores)
    };
    if (frame) era.frame = frame;
    return stringify(era);
  }

  // 预设剧本存为自定剧本(同 WorldMapData.CopyAsCustom)：改编号、剧本名加「·自定」，加作者一栏，记下从哪个预设来
  function copyPresetText(text, presetId, folder) {
    const era = JSON.parse(text);
    era.id = folder;
    for (const key of ["zh", "tw", "en"]) if (era[key] != null) era[key] = era[key] + "·自定";
    era.author = "";
    era.base = presetId;
    return stringify(era);
  }

  // 写文件的文本：不写 null(同游戏的 NullValueHandling.Ignore；字典字段读到 null 进游戏会出错)
  function serialize() {
    flushCells();
    const era = S.era;
    const base = era.exact && !era.base_stale && S.landBase.size > 0 ? fromMap(S.landBase) : null;
    if (base) era.paint_base = base;
    const text = stringify(era);
    delete era.paint_base;
    return text;
  }

  // 缩进两格；数字、短字符串的数组写在一行(刷的格子几万个，一个一行太长)
  function stringify(value) {
    const out = [];
    const flat = array => array.every(item => item === null || typeof item !== "object");
    const write = (item, indent) => {
      if (item === null || typeof item !== "object") { out.push(JSON.stringify(item)); return; }
      const pad = "  ".repeat(indent + 1), end = "  ".repeat(indent);
      if (Array.isArray(item)) {
        if (item.length === 0) { out.push("[]"); return; }
        if (flat(item)) { out.push("[" + item.map(entry => JSON.stringify(entry)).join(", ") + "]"); return; }
        out.push("[\n");
        item.forEach((entry, index) => { out.push(pad); write(entry, indent + 1); out.push(index < item.length - 1 ? ",\n" : "\n"); });
        out.push(end + "]");
        return;
      }
      const keys = Object.keys(item).filter(key => item[key] !== null && item[key] !== undefined);
      if (keys.length === 0) { out.push("{}"); return; }
      out.push("{\n");
      keys.forEach((key, index) => {
        out.push(pad + JSON.stringify(key) + ": ");
        write(item[key], indent + 1);
        out.push(index < keys.length - 1 ? ",\n" : "\n");
      });
      out.push(end + "}");
    };
    write(value, 0);
    return out.join("");
  }

  function saved() { S.dirty = false; }

  // ---------- 撤销 ----------
  function changed(structure = true) {
    S.dirty = true;
    S.revision++;
    if (structure) S.structureRevision++;
    owners = null;
  }

  function beginStroke() { stroke = true; strokeSaved = false; }
  function endStroke() { stroke = false; }

  function before() {
    if (!S.era) return;
    if (stroke) {
      if (strokeSaved) return;
      strokeSaved = true;
    }
    flushCells();
    undos.push(JSON.stringify(S.era));
    while (undos.length > UNDO_LIMIT) undos.shift();
  }

  function undo() {
    if (!S.era || undos.length === 0) return false;
    S.era = normalize(JSON.parse(undos.pop()));
    loadCells();
    changed();
    return true;
  }

  // 表单上的零碎改动都走这里：先存快照，改完记脏
  function mutate(change) {
    if (!S.era) return;
    before();
    change(S.era);
    changed();
  }

  // ---------- 查 ----------
  const cities = () => ownWorld() ? S.era.cities : S.builtin.cities;
  const regions = () => ownWorld() ? (S.era.regions || []) : S.builtin.regions;
  const tianxia = () => ownWorld() ? (S.era.tianxia || (S.era.tianxia = {})) : S.builtin.tianxia;
  function cores() {
    if (!S.era) return [];
    if (ownWorld()) return S.era.cores;
    const ids = new Set(S.era.cores.map(core => core.id));
    return S.era.cores.concat(S.builtin.cores.filter(core => !ids.has(core.id)));
  }
  const city = id => id ? cities().find(item => item.id === id) || null : null;
  const region = id => id ? regions().find(item => item.id === id) || null : null;
  const core = id => id ? cores().find(item => item.id === id) || null : null;
  const polity = id => id ? S.era?.polities.find(item => item.id === id) || null : null;
  const coreOfRegion = id => id ? cores().find(item => item.r?.includes(id)) || null : null;
  const validCulture = key => !!key && (S.cultureKeys.length === 0 || S.cultureKeys.includes(key));

  const active = item => !!item && !!S.era && (item.from ?? -Infinity) <= S.era.year && S.era.year <= (item.to ?? Infinity);
  const rankOf = item => item ? (S.era?.ranks?.[item.id] ?? item.k ?? 2) : 2;

  // 旧称 "到某年为止|简体|英文|繁体"，按年份从早到晚排
  function nameIn(item, year) {
    for (const entry of item?.alt || []) {
      const parts = String(entry).split("|");
      const until = parseInt(parts[0], 10);
      if (parts.length < 3 || Number.isNaN(until) || year > until) continue;
      return parts[1];
    }
    return nameOf(item);
  }
  const cityName = item => item ? (S.era ? nameIn(item, S.era.year) : item.zh) : "";
  const coreName = item => item ? (S.era ? nameIn(item, S.era.year) : nameOf(item)) : "";
  const regionTitle = item => item ? (item.t ? first(item.t) : nameOf(item)) : "";
  const regionCulture = item => item ? (S.era?.culture?.[item.id] ?? item.c ?? "") : "";
  const cityCulture = id => regionCulture(region(city(id)?.r));
  const tianxiaName = id => !id ? "自成一个天下" : first(tianxia()[id] ?? id);

  function nearest(lon, lat, filter) {
    let best = null, bestDistance = Infinity;
    const scale = Math.cos(lat * Math.PI / 180);
    for (const item of cities()) {
      if (filter && !filter(item)) continue;
      const dx = (item.x - lon) * scale, dy = item.y - lat, distance = dx * dx + dy * dy;
      if (distance >= bestDistance) continue;
      bestDistance = distance;
      best = item;
    }
    return best;
  }

  function computeOwners() {
    owners = new Map();
    ownersRevision = S.revision;
    const byId = new Map();
    for (const item of cities()) if (active(item) && !byId.has(item.id)) byId.set(item.id, item);
    for (const item of S.era.polities)
      for (const id of [...(item.cities || []), item.cap])
        if (id && byId.has(id) && !owners.has(id)) owners.set(id, item);
    const independent = new Set(S.era.independent || []);
    for (const item of byId.values()) {
      if (owners.has(item.id)) continue;
      let best = null, bestDistance = Infinity;
      for (const candidate of S.era.polities) {
        const seat = byId.get(candidate.cap || "");
        if (!candidate.r || !candidate.r.includes(item.r) || independent.has(item.id) || !seat) continue;
        const dx = item.x - seat.x, dy = item.y - seat.y, distance = dx * dx + dy * dy;
        if (distance >= bestDistance) continue;
        bestDistance = distance;
        best = candidate;
      }
      if (best) owners.set(item.id, best);
    }
  }

  function ownerOf(item) {
    if (!item || !S.era) return null;
    if (!owners || ownersRevision !== S.revision) computeOwners();
    return owners.get(item.id) || null;
  }

  function cityCount(item) {
    if (!item) return 0;
    if (!owners || ownersRevision !== S.revision) computeOwners();
    let count = 0;
    for (const owner of owners.values()) if (owner === item) count++;
    return count;
  }

  // ---------- 起名(同 ScenarioNaming：按文化的城名规则拼词库，三种写法一起拼；起不出来返回 null) ----------
  function composeName(culture) {
    const rule = S.naming?.[culture];
    if (!rule) return null;
    let zh = "", en = "", tw = "";
    for (let i = 0; i < rule.rule.length; i++) {
      const token = rule.rule[i];
      if (token === "space") { en += " "; continue; }
      if (token === "coin_flip" || !rule.groups[token]) continue;
      // 后面跟着 coin_flip 的这一组只有一半机会用上
      if (rule.rule[i + 1] === "coin_flip" && Math.random() < 0.5) continue;
      const words = rule.groups[token];
      if (words.length === 0) continue;
      const word = words[Math.floor(Math.random() * words.length)];
      zh += word[0]; en += word[1]; tw += word[2];
    }
    return zh.length > 0 ? { zh, en, tw } : null;
  }

  function freshName(culture, taken, shape = name => name) {
    for (let attempt = 0; attempt < 24; attempt++) {
      const name = composeName(culture);
      if (!name) return null;
      const shaped = shape(name);
      if (!taken(shaped)) return shaped;
    }
    return null;
  }

  // ---------- 城 ----------
  function ensureOwnWorld() {
    if (!S.era || ownWorld()) return;
    S.era.regions = clone(S.builtin.regions);
    S.era.cities = clone(S.builtin.cities);
    S.era.tianxia = clone(S.builtin.tianxia);
    const ids = new Set(S.era.cores.map(item => item.id));
    S.era.cores.push(...clone(S.builtin.cores).filter(item => !ids.has(item.id)));
    owners = null;
  }

  function freshId(prefix, list, start) {
    let n = start;
    while (list.some(item => item.id === prefix + n)) n++;
    return prefix + n;
  }

  // 新城的地区：几度内有城跟它；再远按内置城表看这里属哪个地区，文化才对得上
  function regionForNewCity(near, lon, lat) {
    const scale = Math.cos(lat * Math.PI / 180);
    if (near) {
      const dx = (near.x - lon) * scale, dy = near.y - lat;
      if (dx * dx + dy * dy <= NEAR_CITY_DEGREES * NEAR_CITY_DEGREES) return near.r;
    }
    if (!S.era.map) {
      let builtin = null, best = Infinity;
      for (const item of S.builtin.cities) {
        const dx = (item.x - lon) * scale, dy = item.y - lat, distance = dx * dx + dy * dy;
        if (distance >= best) continue;
        best = distance;
        builtin = item;
      }
      if (builtin && region(builtin.r)) return builtin.r;
    }
    return near?.r ?? regions()[0]?.id ?? "";
  }

  // 在经纬度处立一座城；culture 给了而所在王国法理不是这种文化：几度内有这种文化的城就跟它的法理，没有就另立一个法理
  function addCity(lon, lat, culture) {
    if (!S.era) return null;
    before();
    ensureOwnWorld();
    const near = nearest(lon, lat, null);
    let regionId = regionForNewCity(near, lon, lat);
    const wanted = validCulture(culture) && !!culture;
    let own = false;
    if (wanted && regionId && regionCulture(region(regionId)) !== culture) {
      const kin = nearest(lon, lat, item => regionCulture(region(item.r)) === culture);
      const scale = Math.cos(lat * Math.PI / 180);
      const dx = kin ? (kin.x - lon) * scale : 0, dy = kin ? kin.y - lat : 0;
      if (kin && dx * dx + dy * dy <= NEAR_CITY_DEGREES * NEAR_CITY_DEGREES) regionId = kin.r;
      else own = true;
    }
    if (!regionId) {
      const created = { id: "r1", zh: "新地区", tw: "新地区", en: "新地区", c: wanted ? culture : "Huaxia" };
      (S.era.regions ||= []).push(created);
      regionId = created.id;
    }
    const id = freshId("c", S.era.cities, S.era.cities.length + 1);
    // 按所在地区的文化起名，起不出来才叫"新城 N"
    const plain = "新城" + id.slice(1);
    const name = freshName(own ? culture : regions().find(item => item.id === regionId)?.c, named => S.era.cities.some(item => item.zh === named.zh))
      || { zh: plain, en: plain, tw: plain };
    if (own) {
      regionId = freshId("r", S.era.regions, S.era.regions.length + 1);
      S.era.regions.push({ id: regionId, zh: name.zh, tw: name.tw, en: name.en, t: name.zh, cap: id, c: culture });
    }
    S.era.cities.push({ id, zh: name.zh, tw: name.tw, en: name.en, x: round(lon, 3), y: round(lat, 3), r: regionId, k: 2 });
    changed();
    return id;
  }

  const round = (value, digits) => Math.round(value * 10 ** digits) / 10 ** digits;

  function moveCity(id, lon, lat) {
    if (!city(id)) return;
    before();
    ensureOwnWorld();
    const item = city(id);
    item.x = round(lon, 3);
    item.y = round(lat, 3);
    changed();
  }

  // 改名：简体照写；繁体、英文原来和简体同名(或没写)的跟着改，另有写法的留着。城改名连旧称一并清掉(不然按年份还取旧称)
  function applyName(item, name) {
    const old = item.zh || "";
    item.zh = name;
    for (const key of ["tw", "en"]) if (!item[key] || item[key] === old) item[key] = name;
  }

  function renameCity(id, name) {
    name = String(name || "").trim();
    if (!name || !city(id) || cityName(city(id)) === name) return;
    before();
    ensureOwnWorld();
    const item = city(id);
    applyName(item, name);
    if (item.alt) item.alt = [];
    changed();
  }

  function setRank(id, rank) {
    const item = city(id);
    if (!item || rankOf(item) === rank) return;
    before();
    S.era.ranks[id] = rank;
    changed();
  }

  function setCitySpecies(id, species) {
    if (!city(id)) return;
    before();
    if (!species) { if (S.era.species) { delete S.era.species[id]; if (!Object.keys(S.era.species).length) delete S.era.species; } }
    else (S.era.species ||= {})[id] = species;
    changed();
  }

  // 改这座城的文化：王国法理里只有它一座城就改这个法理的文化；还有别的城就先以它另立一个王国法理再改
  function setCityCulture(id, culture) {
    const item = city(id);
    if (!item || !region(item.r) || !validCulture(culture) || cityCulture(id) === culture) return;
    const outer = stroke;
    if (!outer) beginStroke();
    try {
      if (regionCityCount(item.r) > 1) newRegion(id);
      else { before(); ensureOwnWorld(); }
      const target = region(city(id)?.r);
      if (!target) return;
      target.c = culture;
      delete S.era.culture[target.id];
      changed();
    } finally {
      if (!outer) endStroke();
    }
  }

  const steps = item => [...(item?.when || []), ...(item?.then || [])];

  // 删国、删城、删法理：事件里点名它的项清空，不留失效的编号；返回清了几项
  function forgetInEvents({ polity: polityId, city: cityId, region: regionId }) {
    let cleared = 0;
    for (const item of S.era.events)
      for (const step of steps(item)) {
        if (polityId != null && step.a === polityId) { step.a = ""; cleared++; }
        if (polityId != null && step.b === polityId) { step.b = ""; cleared++; }
        if (cityId != null && step.city === cityId) { step.city = ""; cleared++; }
        if (cityId != null && step.cities?.includes(cityId)) { step.cities = step.cities.filter(entry => entry !== cityId); cleared++; }
        if (regionId != null && step.region === regionId) { step.region = ""; cleared++; }
      }
    return cleared;
  }

  const eventsNote = cleared => cleared > 0 ? `；事件里 ${cleared} 处引用已清空，到事件里补上` : "";
  const without = (list, value) => { if (list) { const at = list.indexOf(value); if (at >= 0) list.splice(at, 1); } };

  function leaveDivisions(item, cityId) {
    for (const division of item?.divisions || []) without(division?.cities, cityId);
  }

  function dropLandKey(key) {
    let dropped = false;
    for (const [cell, value] of S.land) if (value === key) { S.land.delete(cell); dropped = true; }
    if (dropped) { cellsDirty = true; S.landRevision++; }
  }

  // 删城：连同各国点名的城、法理首都、事件里点名这座城的项一并去掉；是都城的，本国另一座最大的城接着当都城
  function deleteCity(id) {
    if (!city(id)) return null;
    before();
    const heirs = new Map();
    for (const item of S.era.polities.filter(entry => entry.cap === id)) {
      const heir = cities().filter(entry => entry.id !== id && active(entry) && ownerOf(entry) === item).sort((a, b) => rankOf(a) - rankOf(b))[0];
      if (heir) heirs.set(item, heir.id);
    }
    ensureOwnWorld();
    S.era.cities = S.era.cities.filter(item => item.id !== id);
    dropLandKey("c:" + id);
    for (const item of S.era.polities) {
      without(item.cities, id);
      if (item.cap === id) item.cap = heirs.get(item) || "";
      leaveDivisions(item, id);
    }
    for (const item of S.era.regions) if (item.cap === id) item.cap = "";
    for (const item of S.era.cores) if (item.cap === id) item.cap = "";
    delete S.era.ranks[id];
    if (S.era.species) delete S.era.species[id];
    without(S.era.independent, id);
    const forgotten = forgetInEvents({ city: id });
    changed();
    return "城已删(可撤销)" + eventsNote(forgotten);
  }

  // 清空全部城(连同国家、关系)：地区、帝国法理留着，放新城时还能接着用
  function clearAllCities() {
    if (!S.era) return null;
    before();
    ensureOwnWorld();
    S.era.cities = [];
    S.era.own_cities = true;
    S.era.exact = false;
    S.era.base_stale = true;
    delete S.era.independent;
    S.era.ranks = {};
    delete S.era.species;
    S.era.polities = [];
    S.era.relations = [];
    for (const item of S.era.regions || []) item.cap = "";
    for (const item of S.era.cores) item.cap = "";
    for (const item of S.era.events)
      for (const step of steps(item)) { step.a = step.b = step.city = ""; delete step.cities; }
    for (const [cell, value] of S.land) if (value[0] === "c" || value[0] === "p") S.land.delete(cell);
    cellsDirty = true;
    S.landRevision++;
    changed();
    return "全部城和国家已清空(可撤销)";
  }

  // ---------- 国家 ----------
  const capitalHolder = (cityId, except) => S.era.polities.find(item => item !== except && item.cap === cityId) || null;
  const locked = (item, holder) => `「${cityName(item)}」是${nameOf(holder)}的都城，先给${nameOf(holder)}另立都城`;

  // 把城划给某国(写进那国点名的城，别国点名的去掉)；返回提示，划不了时说原因
  function assign(cityId, target) {
    const item = city(cityId);
    if (!item || !target || !S.era) return null;
    const holder = capitalHolder(cityId, target);
    if (holder) return locked(item, holder);
    if (ownerOf(item) === target && (target.cities || []).includes(cityId)) return null;
    before();
    for (const other of S.era.polities) {
      without(other.cities, cityId);
      if (other !== target) leaveDivisions(other, cityId);
    }
    (target.cities ||= []).push(cityId);
    without(S.era.independent, cityId);
    changed();
    return `「${cityName(item)}」划给${nameOf(target)}`;
  }

  // 让城独立：所在地区被哪国认领也不归它(记进 independent)；是都城的先给那国另立都城
  function makeIndependent(cityId) {
    const item = city(cityId);
    if (!item || !S.era) return null;
    const holder = capitalHolder(cityId, null);
    if (holder) return locked(item, holder);
    if (!ownerOf(item)) return `「${cityName(item)}」本来就独立`;
    before();
    for (const other of S.era.polities) { without(other.cities, cityId); leaveDivisions(other, cityId); }
    if (S.era.polities.some(entry => entry.r?.includes(item.r))) {
      S.era.independent ||= [];
      if (!S.era.independent.includes(cityId)) S.era.independent.push(cityId);
    }
    changed();
    return `「${cityName(item)}」独立`;
  }

  // 认领王国法理：里面没被别国点名的城都归这国；再点一次不再认领
  function toggleClaim(target, regionId) {
    const item = region(regionId);
    if (!target || !item || !S.era) return null;
    before();
    target.r ||= [];
    const had = target.r.includes(regionId);
    if (had) without(target.r, regionId); else target.r.push(regionId);
    changed();
    return had ? `${nameOf(target)} 不再认领 ${regionTitle(item)}` : `${nameOf(target)} 认领 ${regionTitle(item)}：里面没被别国点名的城都归它`;
  }

  // 改城的地区不该连带改国家：写明归原来那国(原来独立的记成独立)
  function keepOwner(item, previous) {
    owners = null;
    const after = ownerOf(item);
    if (after !== previous) {
      if (previous) (previous.cities ||= []).push(item.id);
      else (S.era.independent ||= []).push(item.id);
    }
    owners = null;
  }

  // 以这座城为都城立一个新国家(文化取城所在地区的)
  function newPolity(cityId) {
    const item = city(cityId);
    if (!item || !S.era) return { polity: null, message: null };
    const holder = capitalHolder(cityId, null);
    if (holder) return { polity: null, message: locked(item, holder) };
    before();
    for (const other of S.era.polities) without(other.cities, cityId);
    without(S.era.independent, cityId);
    const id = freshId("p", S.era.polities, S.era.polities.length + 1);
    const name = cityName(item), culture = regionCulture(region(item.r));
    const created = { id, zh: name, tw: name, en: name, c: validCulture(culture) ? culture : "Huaxia", cap: cityId };
    S.era.polities.push(created);
    changed();
    return { polity: created, message: `立了国家「${name}」，右栏改名、定文化` };
  }

  function deletePolity(target) {
    if (!target || !S.era || !S.era.polities.includes(target)) return null;
    before();
    S.era.polities = S.era.polities.filter(item => item.id !== target.id);
    S.era.relations = S.era.relations.filter(item => item.a !== target.id && item.b !== target.id);
    dropLandKey("p:" + target.id);
    for (const other of S.era.polities) if (other.of === target.id) other.of = "";
    const forgotten = forgetInEvents({ polity: target.id });
    changed();
    return "国家已删(可撤销)" + eventsNote(forgotten);
  }

  // 立某城为某国都城(先划进来；原来的都城留在本国点名的城里)
  function setCapital(target, cityId) {
    const item = city(cityId);
    if (!target || !item) return null;
    const holder = capitalHolder(cityId, target);
    if (holder) return locked(item, holder);
    if (target.cap === cityId) return null;
    before();
    for (const other of S.era.polities) without(other.cities, cityId);
    without(S.era.independent, cityId);
    target.cities ||= [];
    if (target.cap && target.cap !== cityId && !target.cities.includes(target.cap)) target.cities.push(target.cap);
    target.cap = cityId;
    for (const other of S.era.polities) leaveDivisions(other, cityId);
    changed();
    return `${nameOf(target)}以「${cityName(item)}」为都城`;
  }

  function renamePolity(target, name) {
    name = String(name || "").trim();
    if (!target || !name || target.zh === name) return;
    before();
    applyName(target, name);
    changed();
  }

  // 国祚与积弊：空的删掉这一项(照默认)，国祚 0~1000 的整数年，其余 0~100
  function setDecline(target, key, text) {
    if (!target || !DECLINE_KEYS.includes(key)) return;
    const trimmed = String(text ?? "").trim();
    let value = null;
    if (trimmed.length > 0) {
      const parsed = Number(trimmed);
      if (!Number.isFinite(parsed)) return;
      value = key === "age" ? Math.min(1000, Math.max(0, Math.round(parsed))) : Math.min(100, Math.max(0, parsed));
    }
    const old = target.decline && key in target.decline ? target.decline[key] : null;
    if (old === value) return;
    before();
    target.decline ||= {};
    if (value === null) delete target.decline[key]; else target.decline[key] = value;
    if (!Object.keys(target.decline).length) delete target.decline;
    changed();
  }

  // ---------- 行政区 ----------
  const division = (target, index) => target?.divisions && index >= 0 && index < target.divisions.length ? target.divisions[index] : null;

  function divisionOf(target, cityId) {
    if (!target?.divisions || !cityId || target.cap === cityId) return -1;
    return target.divisions.findIndex(item => item?.cities?.includes(cityId));
  }

  const seatsOf = (target, index) => index < 0 ? target?.seats : division(target, index)?.seats;
  const divisionName = (target, index) => index < 0 ? `${nameOf(target)}京畿` : first(division(target, index)?.name) || "?";
  const parts = name => `${name}|${name}|${name}`;

  function newDivision(target) {
    if (!target || !S.era) return -1;
    before();
    target.divisions ||= [];
    target.divisions.push({ name: parts("新行政区" + (target.divisions.length + 1)), cities: [] });
    changed();
    return target.divisions.length - 1;
  }

  function renameDivision(target, index, name) {
    const item = division(target, index);
    const text = String(name || "").trim();
    if (!item || !text || item.name === text || first(item.name) === text) return;
    before();
    item.name = text.includes("|") ? text : parts(text);
    changed();
  }

  function deleteDivision(target, index) {
    if (!division(target, index)) return;
    before();
    target.divisions.splice(index, 1);
    changed();
  }

  // 把城划进这国的某行政区(index 为 -1 是放回京畿)
  function assignDivision(target, index, cityId) {
    const item = city(cityId);
    if (!target || !item || !S.era) return null;
    if (ownerOf(item) !== target) return `${cityName(item)} 开局不归${nameOf(target)}，划不进它的行政区`;
    if (target.cap === cityId) return `${cityName(item)}是都城，留在京畿，划不进行政区`;
    if (index >= 0 && !division(target, index)) return null;
    if (divisionOf(target, cityId) === index) return null;
    before();
    for (const other of S.era.polities) leaveDivisions(other, cityId);
    if (index >= 0) (target.divisions[index].cities ||= []).push(cityId);
    changed();
    return `${cityName(item)} 划进 ${divisionName(target, index)}`;
  }

  function addSeat(target, index, lon, lat) {
    if (!target || index >= 0 && !division(target, index)) return;
    before();
    const point = [round(lon, 2), round(lat, 2)];
    if (index < 0) (target.seats ||= []).push(point);
    else (target.divisions[index].seats ||= []).push(point);
    changed();
  }

  function removeSeat(target, index, lon, lat, reach) {
    const seats = seatsOf(target, index);
    if (!seats) return false;
    let found = -1, best = reach * reach;
    seats.forEach((seat, i) => {
      if (!seat || seat.length < 2) return;
      const dx = seat[0] - lon, dy = seat[1] - lat, distance = dx * dx + dy * dy;
      if (distance > best) return;
      best = distance;
      found = i;
    });
    if (found < 0) return false;
    before();
    seats.splice(found, 1);
    if (seats.length === 0) { if (index < 0) delete target.seats; else delete target.divisions[index].seats; }
    changed();
    return true;
  }

  function clearSeats(target, index) {
    if (!(seatsOf(target, index)?.length > 0)) return;
    before();
    if (index < 0) delete target.seats; else delete target.divisions[index].seats;
    changed();
  }

  // ---------- 王国法理(地区) ----------
  const regionCityCount = id => cities().filter(item => active(item) && item.r === id).length;

  // 以选中的城另立一个王国法理(名、法理名都取城名，文化同原地区)，城划进去、作法理首都
  function newRegion(cityId) {
    let item = city(cityId);
    if (!item) return null;
    const owner = ownerOf(item);
    before();
    ensureOwnWorld();
    item = city(cityId);
    const old = region(item.r);
    const id = freshId("r", S.era.regions, S.era.regions.length + 1);
    const name = cityName(item), culture = regionCulture(old);
    S.era.regions.push({ id, zh: name, tw: name, en: name, c: validCulture(culture) ? culture : "Huaxia", t: name, cap: cityId });
    item.r = id;
    without(item.share, id);
    keepOwner(item, owner);
    changed();
    return id;
  }

  function assignRegion(cityId, regionId) {
    let item = city(cityId);
    if (!item || !region(regionId) || item.r === regionId) return null;
    const owner = ownerOf(item);
    before();
    ensureOwnWorld();
    item = city(cityId);
    for (const other of S.era.regions) if (other.cap === cityId) other.cap = "";
    item.r = regionId;
    without(item.share, regionId);
    keepOwner(item, owner);
    changed();
    return `「${cityName(item)}」划进王国法理「${regionTitle(region(regionId))}」`;
  }

  function renameRegion(id, name) {
    name = String(name || "").trim();
    if (!name || !region(id) || region(id).zh === name) return;
    before();
    ensureOwnWorld();
    applyName(region(id), name);
    changed();
  }

  function setRegionTitle(id, title) {
    title = String(title || "").trim();
    if (!region(id) || title === regionTitle(region(id))) return;
    before();
    ensureOwnWorld();
    if (title) region(id).t = title; else delete region(id).t;
    changed();
  }

  function setRegionCulture(id, culture) {
    if (!region(id) || !validCulture(culture) || regionCulture(region(id)) === culture) return;
    before();
    ensureOwnWorld();
    region(id).c = culture;
    delete S.era.culture[id];
    changed();
  }

  function setRegionCapital(regionId, cityId) {
    const item = city(cityId);
    if (!item || !region(regionId)) return null;
    if (item.r !== regionId) return `「${cityName(item)}」不在王国法理「${regionTitle(region(regionId))}」里`;
    before();
    ensureOwnWorld();
    region(regionId).cap = cityId;
    changed();
    return `王国法理「${regionTitle(region(regionId))}」以「${cityName(item)}」为首都`;
  }

  function regionCapitalName(item) {
    if (!item) return "";
    const chosen = city(item.cap);
    if (chosen && chosen.r === item.id) return cityName(chosen);
    const largest = cities().filter(entry => active(entry) && entry.r === item.id).sort((a, b) => rankOf(a) - rankOf(b))[0];
    return largest ? `${cityName(largest)}(自动)` : "";
  }

  // 城兼属某王国法理(至多两个)：再点一次去掉
  function toggleShare(cityId, regionId) {
    let item = city(cityId);
    const target = region(regionId);
    if (!item || !target) return null;
    if (item.r === regionId) return "城本来就在这个王国法理里";
    const has = item.share?.includes(regionId) === true;
    if (!has && item.share?.length >= 2) return "一座城至多兼属两个王国法理";
    before();
    ensureOwnWorld();
    item = city(cityId);
    item.share ||= [];
    if (has) without(item.share, regionId); else item.share.push(regionId);
    changed();
    return has ? `「${cityName(item)}」不再兼属王国法理「${regionTitle(target)}」` : `「${cityName(item)}」兼属王国法理「${regionTitle(target)}」`;
  }

  // 删王国法理：里面没城了才删(连同各国认领、帝国法理收的、城兼属的一并去掉)
  function deleteRegion(id) {
    if (!region(id)) return null;
    if (cities().some(item => item.r === id)) return "里面还有城，先把城划到别的王国法理";
    before();
    ensureOwnWorld();
    S.era.regions = S.era.regions.filter(item => item.id !== id);
    for (const item of S.era.polities) without(item.r, id);
    for (const item of S.era.cores) { without(item.r, id); without(item.share, id); }
    for (const item of S.era.cities) without(item.share, id);
    delete S.era.culture[id];
    dropLandKey("r:" + id);
    const forgotten = forgetInEvents({ region: id });
    changed();
    return "王国法理已删" + eventsNote(forgotten);
  }

  // ---------- 帝国法理与天下 ----------
  // 以某王国法理另立一个帝国法理(名取法理名，文化同地区，首都取那个法理的首都，天下同原来所在的)
  function newCore(regionId) {
    let item = region(regionId);
    if (!item) return null;
    before();
    const group = coreOfRegion(regionId)?.tx || "";
    ensureOwnWorld();
    item = region(regionId);
    for (const other of S.era.cores) without(other.r, regionId);
    const id = freshId("k", S.era.cores, S.era.cores.length + 1);
    const name = regionTitle(item);
    const seat = city(item.cap) || cities().filter(entry => entry.r === regionId).sort((a, b) => rankOf(a) - rankOf(b))[0];
    S.era.cores.push({ id, zh: name, tw: name, en: name, c: regionCulture(item), cap: seat?.id || "", r: [regionId], tx: group });
    changed();
    return id;
  }

  function addRegionToCore(coreId, regionId) {
    if (!core(coreId) || !region(regionId) || coreOfRegion(regionId)?.id === coreId) return null;
    before();
    ensureOwnWorld();
    for (const other of S.era.cores) without(other.r, regionId);
    const target = core(coreId);
    (target.r ||= []).push(regionId);
    changed();
    return `王国法理「${regionTitle(region(regionId))}」收进帝国法理「${coreName(target)}」`;
  }

  function removeRegionFromCore(regionId) {
    const owner = coreOfRegion(regionId);
    if (!owner) return null;
    before();
    ensureOwnWorld();
    for (const other of S.era.cores) without(other.r, regionId);
    changed();
    return `王国法理「${regionTitle(region(regionId)) || regionId}」移出帝国法理「${coreName(owner)}」`;
  }

  function renameCore(id, name) {
    name = String(name || "").trim();
    if (!name || !core(id) || coreName(core(id)) === name) return;
    before();
    ensureOwnWorld();
    const item = core(id);
    applyName(item, name);
    item.alt = [];
    changed();
  }

  function setCoreCapital(coreId, cityId) {
    const target = core(coreId), item = city(cityId);
    if (!target || !item) return null;
    if (!target.r?.includes(item.r)) return `「${cityName(item)}」不在帝国法理「${coreName(target)}」收的王国法理里`;
    before();
    ensureOwnWorld();
    core(coreId).cap = cityId;
    changed();
    return `帝国法理「${coreName(core(coreId))}」以「${cityName(item)}」为首都`;
  }

  function coreCapitalName(item) {
    if (!item) return "";
    const chosen = city(item.cap);
    if (chosen && item.r?.includes(chosen.r)) return cityName(chosen);
    const largest = cities().filter(entry => active(entry) && item.r?.includes(entry.r)).sort((a, b) => rankOf(a) - rankOf(b))[0];
    return largest ? `${cityName(largest)}(自动)` : "";
  }

  function setCoreTianxia(coreId, group) {
    if (!core(coreId) || (core(coreId).tx || "") === (group || "")) return;
    before();
    ensureOwnWorld();
    core(coreId).tx = group || "";
    changed();
  }

  function newTianxia(coreId) {
    if (!core(coreId)) return null;
    before();
    ensureOwnWorld();
    const target = core(coreId), table = tianxia();
    let n = Object.keys(table).length + 1;
    while (("tx" + n) in table) n++;
    table["tx" + n] = coreName(target);
    target.tx = "tx" + n;
    changed();
    return "tx" + n;
  }

  function renameTianxia(id, name) {
    name = String(name || "").trim();
    if (!id || !name || tianxiaName(id) === name) return;
    before();
    ensureOwnWorld();
    tianxia()[id] = name;
    changed();
  }

  function deleteCore(coreId) {
    if (!core(coreId)) return;
    before();
    ensureOwnWorld();
    S.era.cores = S.era.cores.filter(item => item.id !== coreId);
    changed();
  }

  // ---------- 关系 ----------
  const RELATION_TEXT = {
    war: "{0} 向 {1} 宣战", truce: "{0} 与 {1} 停战", alliance: "{0} 与 {1} 结盟", marriage: "{0} 与 {1} 联姻", pact: "{0} 与 {1} 通好",
    protect: "{0} 保护 {1}", tribute: "{0} 向 {1} 称臣纳贡", vassal: "{0} 立 {1} 为附庸", union: "{1} 并入 {0} 的市场",
    puppet: "{0} 立 {1} 为傀儡", friend: "{0} 与 {1} 有旧谊", feud: "{0} 与 {1} 有宿怨"
  };
  const relationSentence = (kind, a, b) => (RELATION_TEXT[kind] || `{0} ${kind} {1}`).replace("{0}", a).replace("{1}", b);
  const inEmpire = item => !!item.emp || !!item.of;

  // 这条关系开局做不做得成(编辑器看得出来的那几条)；做得成返回 null
  function relationProblem(a, b, kind) {
    if (!a || !b) return null;
    if (kind === "tribute") {
      if (!b.emp) return `${nameOf(b)} 开局不称帝，受不了朝贡`;
      return a.emp || a.of ? `${nameOf(a)} 自己是帝国或帝国成员，不称臣` : null;
    }
    if (kind === "pact") return inEmpire(a) && inEmpire(b) ? null : "通好要两边都是帝国(或帝国成员)";
    return null;
  }

  // 两国之间加一条关系(已有同种的就去掉)；a 是发起的一方(宣战方、保护方、称臣方、立附庸方、领市场方)
  function toggleRelation(a, b, kind, goal) {
    if (!a || !b || a === b || !S.era || !kind) return null;
    const mutual = MUTUAL.has(kind);
    const existing = S.era.relations.find(item => item.kind === kind &&
      (item.a === a.id && item.b === b.id || mutual && item.a === b.id && item.b === a.id));
    before();
    if (existing) {
      without(S.era.relations, existing);
      changed();
      return "去掉：" + relationSentence(kind, nameOf(a), nameOf(b));
    }
    const created = { a: a.id, b: b.id, kind };
    if (kind === "war") created.goal = goal || WAR_GOALS[0];
    S.era.relations.push(created);
    changed();
    const problem = relationProblem(a, b, kind);
    return "加上：" + relationSentence(kind, nameOf(a), nameOf(b)) + (problem ? "  " + problem : "");
  }

  function relationText(item) {
    const a = polity(item.a), b = polity(item.b);
    let text = relationSentence(item.kind, nameOf(a) || item.a, nameOf(b) || item.b);
    if (item.kind === "war" && item.goal) text += "·" + item.goal;
    return text;
  }

  // ---------- 长城、关隘、部落圈 ----------
  function newWall() {
    if (!S.era) return -1;
    before();
    S.era.walls.push({ name: "长城" + (S.era.walls.length + 1), pts: [] });
    changed();
    return S.era.walls.length - 1;
  }

  function addWallPoint(index, lon, lat) {
    const wall = S.era?.walls[index];
    if (!wall) return;
    before();
    (wall.pts ||= []).push([round(lon, 2), round(lat, 2)]);
    changed();
  }

  function removeWallPoint(index) {
    const wall = S.era?.walls[index];
    if (!(wall?.pts?.length > 0)) return false;
    before();
    wall.pts.pop();
    changed();
    return true;
  }

  function addPass(lon, lat) {
    if (!S.era) return -1;
    before();
    // 按最近的城所在地区的文化起名；附近没城用第一个地区的文化
    const culture = regions().find(item => item.id === (nearest(lon, lat, null)?.r ?? regions()[0]?.id))?.c;
    const name = freshName(culture, named => S.era.passes.some(pass => pass.name === named), named => `${named.zh}关|${named.en} Pass|${named.tw}關`)
      || "关" + (S.era.passes.length + 1);
    S.era.passes.push({ name, at: [round(lon, 2), round(lat, 2)] });
    changed();
    return S.era.passes.length - 1;
  }

  // 画一个部落圈：文化取圈心最近那城所在法理的文化
  function addTribes(lon, lat, radius) {
    if (!S.era || radius <= 0) return -1;
    const near = nearest(lon, lat, active);
    let culture = near ? regionCulture(region(near.r)) : "";
    if (!validCulture(culture)) culture = S.cultureKeys[0] || "Huaxia";
    before();
    (S.era.tribes ||= []).push({ at: [round(lon, 2), round(lat, 2)], radius: round(radius, 1), culture, count: 0 });
    changed();
    return S.era.tribes.length - 1;
  }

  function tribesAt(lon, lat) {
    let found = -1, best = Infinity;
    (S.era?.tribes || []).forEach((item, i) => {
      if (!item?.at || item.at.length < 2) return;
      const dx = lon - item.at[0], dy = lat - item.at[1];
      if (dx * dx + dy * dy > item.radius * item.radius || item.radius >= best) return;
      best = item.radius;
      found = i;
    });
    return found;
  }

  // ---------- 导入地图 ----------
  // 换成导入地图：城、地区、帝国法理、天下、国家、关系、长城关隘、事件都清掉(地球上的城与经纬度用不上)，留一个空地区；
  // 换回地球：城表回到内置的，国家等同样清掉。都能撤销(同 ScenarioEditing.SetImport)
  function setImport(on) {
    if (!S.era || on === !!S.era.map) return;
    before();
    const era = S.era;
    if (on) era.map = { mode: "color", sea: 0.4 }; else delete era.map;
    era.polities = []; era.relations = []; era.walls = []; era.passes = []; era.events = [];
    era.ranks = {}; era.culture = {}; era.cores = [];
    for (const key of ["species", "paint", "terrain", "develop", "tribes", "size", "independent", "own_cities", "exact", "paint_base"]) delete era[key];
    era.base_stale = true;
    loadCells();
    if (on) {
      era.regions = [{ id: "r1", zh: "新地区", tw: "新地区", en: "新地区", c: "Huaxia" }];
      era.cities = [];
      era.tianxia = {};
    } else {
      delete era.regions; delete era.cities; delete era.tianxia;
    }
    S.landBase = new Map();
    changed();
  }

  // ---------- 笔刷 ----------
  const restoresBase = () => S.era?.exact === true && S.era.base_stale !== true && S.landBase.size > 0;

  // 擦掉这一格回到什么：导入时的归属(归的城还在才算)
  function baseValue(cell, alive) {
    const key = S.landBase.get(cell);
    if (key === undefined) return null;
    return key.startsWith("c:") && !alive.has(key.slice(2)) ? null : key;
  }

  function paintCells(map, cells, key) {
    if (!S.era || !cells || cells.length === 0) return 0;
    const erase = !key;
    const changes = cells.filter(cell => erase ? map.has(cell) : map.get(cell) !== key);
    if (changes.length === 0) return 0;
    before();
    for (const cell of changes) if (erase) map.delete(cell); else map.set(cell, key);
    cellsDirty = true;
    changed(false);
    return changes.length;
  }

  // 刷一笔：key 为空就擦掉(照原图分地的剧本擦掉是回到导入时的样子)；返回改了几格
  function paintLand(cells, key) {
    if (!key && restoresBase()) {
      const alive = new Set(cities().map(item => item.id));
      const changes = [];
      for (const cell of cells) {
        const value = baseValue(cell, alive), current = S.land.get(cell) ?? null;
        if (current !== value) changes.push([cell, value]);
      }
      if (changes.length === 0) return 0;
      before();
      for (const [cell, value] of changes) if (value === null) S.land.delete(cell); else S.land.set(cell, value);
      cellsDirty = true;
      S.landRevision++;
      changed(false);
      return changes.length;
    }
    const count = paintCells(S.land, cells, key);
    if (count > 0) S.landRevision++;
    return count;
  }

  function paintTerrain(cells, type) {
    const count = paintCells(S.terrain, cells, type);
    if (count > 0) S.terrainRevision++;
    return count;
  }

  function paintDevelop(cells, value) {
    const count = paintCells(S.develop, cells, value);
    if (count > 0) S.developRevision++;
    return count;
  }

  function clearPaint(which) {
    const map = S[which];
    if (!map || map.size === 0) return;
    if (which === "land" && restoresBase()) { paintLand([...new Set([...S.land.keys(), ...S.landBase.keys()])], ""); return; }
    before();
    map.clear();
    cellsDirty = true;
    S[which + "Revision"]++;
    changed(false);
  }

  const landCount = key => { let count = 0; for (const value of S.land.values()) if (value === key) count++; return count; };

  // ---------- 事件 ----------
  const CONDITION_KINDS = ["holds_city", "holds_region", "gone", "alive", "empire", "at_war", "strain_above", "before", "dead"];
  const EFFECT_KINDS = ["relation", "proclaim", "rename", "move_capital", "culture", "adopt_huaxia", "rebel", "cede", "dies", "heir",
    "grant_title", "submit", "rally", "puppet", "ideology", "exile", "banner_city", "banner_enclosure", "banner_law"];

  // 这一种条件、结果要填哪几项(游戏里编辑器给的那几项，加上文件里能写的其余项)
  function slotsOf(step, effect) {
    if (!effect) {
      switch (step.kind) {
        case "holds_city": return ["a", "city"];
        case "holds_region": return ["a", "region"];
        case "at_war": return ["a", "b"];
        case "strain_above": return ["a", "value"];
        case "before": return ["value"];
        case "dead": return ["person"];
        default: return ["a"];
      }
    }
    switch (step.kind) {
      case "relation": return step.relation === "war" ? ["a", "b", "relation", "goal", "city"]
        : step.relation === "friend" || step.relation === "feud" ? ["a", "b", "relation", "value"] : ["a", "b", "relation"];
      case "move_capital": return ["a", "city"];
      case "culture": return ["a", "culture"];
      case "proclaim": case "rename": case "adopt_huaxia": return ["a", "name"];
      case "rebel": return ["a", "city", "cities", "name", "person", "b", "roving", "hero"];
      case "cede": return ["a", "b", "city", "cities", "region", "all", "person"];
      case "dies": return ["a", "person", "name"];
      case "heir": return ["a", "person", "city"];
      case "grant_title": return ["a", "person", "name"];
      case "submit": return ["a", "b", "c"];
      case "rally": return ["a", "b"];
      case "puppet": return ["a", "b", "city", "cities", "region", "name", "person", "culture"];
      case "ideology": return ["a", "name", "party", "one_party"];
      case "exile": return ["a", "b", "c", "region"];
      case "banner_city": case "banner_enclosure": return ["a", "city", "cities"];
      default: return ["a"];
    }
  }

  // 事件里用得上的国：剧本国，加上前面起兵、扶植傀儡立的新国(b)
  function eventPolityIds() {
    const ids = S.era.polities.map(item => item.id);
    for (const item of S.era.events)
      for (const step of item.then || [])
        if ((step.kind === "rebel" || step.kind === "puppet") && step.b && !ids.includes(step.b)) ids.push(step.b);
    return ids;
  }

  // 事件能点名的人物：剧本写了名字的君主、宗室、名臣("简体姓+名"，同 ScenarioEventSystem.NameKeys)
  function knownPeople() {
    const known = new Set();
    const add = (family, given) => {
      const fam = String(family ?? "").split("|"), giv = String(given ?? "").split("|");
      for (let i = 0; i < Math.max(fam.length, giv.length); i++) {
        const f = (i < fam.length ? fam[i] : fam[0]).trim(), g = (i < giv.length ? giv[i] : giv[0]).trim();
        if (g) known.add(!f ? g : i === 1 ? f + " " + g : f + g);
      }
    };
    for (const item of S.era?.polities || []) {
      if (item.ruler) add(item.ruler.fam, item.ruler.giv);
      for (const person of item.kin || []) add(person.fam ?? item.ruler?.fam, person.giv);
      for (const person of item.kin || []) add(person.fam, person.giv);
      for (const person of item.famous || []) add(person.fam, person.giv);
    }
    return known;
  }

  function unknownPeople(names) {
    const known = knownPeople();
    return String(names || "").split(/[、,，]/).map(name => name.trim()).filter(name => name && !known.has(name));
  }

  // ---------- 颜色(同 ScenarioEditing.ColorOfKey：同一剧本里稳定，网页和游戏里一个颜色) ----------
  function hsv(h, s, v) {
    const i = Math.floor(h * 6), f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
    const [r, g, b] = [[v, t, p], [q, v, p], [p, v, t], [p, q, v], [t, p, v], [v, p, q]][((i % 6) + 6) % 6];
    return [Math.floor(r * 255), Math.floor(g * 255), Math.floor(b * 255)];
  }

  function rgbToHsv(r, g, b) {
    r /= 255; g /= 255; b /= 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min;
    let h = 0;
    if (d > 0) h = max === r ? ((g - b) / d + 6) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
    return [h / 6, max === 0 ? 0 : d / max, max];
  }

  const hashOf = key => { let hash = 17; for (let i = 0; i < key.length; i++) hash = (Math.imul(hash, 31) + key.charCodeAt(i)) | 0; return hash; };
  const colorCache = new Map();

  function colorOfKey(key) {
    if (!key) return [150, 150, 150];
    if (colorCache.has(key)) return colorCache.get(key);
    let color;
    const cut = key.lastIndexOf(":");
    if (key.startsWith("j:")) color = colorOfKey("p:" + key.slice(2));
    else if (key.startsWith("i:")) { const tone = 135 + (hashOf(key) & 0xFFFF) % 45; color = [tone, tone, tone]; }
    else if (key.startsWith("d:") && cut > 2 && /^\d+$/.test(key.slice(cut + 1))) {
      const [h, s, v] = rgbToHsv(...colorOfKey("p:" + key.slice(2, cut)));
      color = hsv((h + 0.13 + Number(key.slice(cut + 1)) * 0.17) % 1, Math.min(1, Math.max(0, s * 0.9 + 0.1)), Math.min(0.95, Math.max(0.6, v)));
    } else {
      const hash = hashOf(key);
      color = hsv((hash & 0xFFFF) / 65535, 0.55 + ((hash >> 16) & 3) * 0.1, 0.8 + ((hash >> 18) & 1) * 0.1);
    }
    colorCache.set(key, color);
    return color;
  }

  // ---------- 检查(同游戏里世界地图窗口列的那几条，加几条编辑时容易漏的) ----------
  function problems() {
    const list = [];
    if (!S.era) return list;
    const cityIds = new Set(cities().map(item => item.id)), regionIds = new Set(regions().map(item => item.id)), seen = new Set();
    // 剧本自带的王国法理：编号也能当地区用
    for (const title of S.era.titles || []) {
      if (title.id) regionIds.add(title.id);
      if (!title.id || !(title.cities?.length > 0)) list.push(`自带王国法理「${title.zh || title.id || "?"}」要有编号和本属的城`);
      for (const id of [...(title.cities || []), ...(title.share || [])]) if (!cityIds.has(id)) list.push(`自带王国法理 ${title.id} 的城 ${id} 不在城表里`);
    }
    for (const item of S.era.polities) {
      if (seen.has(item.id)) list.push(`国家编号重复：${item.id}`);
      seen.add(item.id);
      if (!item.cap) list.push(`${nameOf(item)} 没有都城，开局这国不存在`);
      else if (!cityIds.has(item.cap)) list.push(`${nameOf(item)} 的都城 ${item.cap} 不在城表里`);
      else if (!active(city(item.cap))) list.push(`${nameOf(item)} 的都城「${nameOf(city(item.cap))}」开局年还不存在`);
      for (const id of item.r || []) if (!regionIds.has(id)) list.push(`${nameOf(item)} 认领的地区 ${id} 不存在`);
      if (item.of && !polity(item.of)) list.push(`${nameOf(item)} 的宗主 ${item.of} 不存在`);
    }
    for (const item of S.era.relations)
      for (const id of [item.a, item.b]) if (!seen.has(id || "")) list.push(`关系里写了不存在的国家：${id}`);
    return [...new Set(list)];
  }

  window.SD = {
    S, LAND_STEP, TERRAIN_STEP, RELATION_KINDS, WAR_GOALS, DECLINE_KEYS, FRAMES, CONDITION_KINDS, EFFECT_KINDS,
    pack, unpack, cellKey, first, nameOf, clone, round,
    open, close, blankText, copyPresetText, serialize, stringify, saved, ownWorld,
    before, changed, mutate, undo, beginStroke, endStroke, canUndo: () => undos.length > 0,
    cities, regions, cores, tianxia, city, region, core, polity, coreOfRegion, validCulture, active, rankOf,
    cityName, coreName, regionTitle, regionCulture, cityCulture, tianxiaName, nearest, ownerOf, cityCount,
    ensureOwnWorld, addCity, moveCity, renameCity, setRank, setCitySpecies, setCityCulture, deleteCity, clearAllCities,
    assign, makeIndependent, toggleClaim, newPolity, deletePolity, setCapital, renamePolity, setDecline,
    division, divisionOf, seatsOf, divisionName, newDivision, renameDivision, deleteDivision, assignDivision, addSeat, removeSeat, clearSeats,
    regionCityCount, newRegion, assignRegion, renameRegion, setRegionTitle, setRegionCulture, setRegionCapital, regionCapitalName,
    toggleShare, deleteRegion,
    newCore, addRegionToCore, removeRegionFromCore, renameCore, setCoreCapital, coreCapitalName, setCoreTianxia, newTianxia,
    renameTianxia, deleteCore,
    relationSentence, relationProblem, toggleRelation, relationText,
    newWall, addWallPoint, removeWallPoint, addPass, addTribes, tribesAt,
    setImport, restoresBase, paintLand, paintTerrain, paintDevelop, clearPaint, landCount,
    slotsOf, eventPolityIds, unknownPeople, colorOfKey, problems
  };
})();
