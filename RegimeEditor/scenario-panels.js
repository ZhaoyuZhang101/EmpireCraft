// 剧本编辑器的右栏：随工具换的各张卡(剧本、城、国家、行政区、王国法理、帝国法理、关系、事件、刷地块、刷地形、长城、关隘、部落圈)。
// 卡里的输入框改完(失去焦点或回车)才写进剧本；每次改动后整栏重搭，原来在哪个输入框里接着还在哪个。
// 游戏里的编辑器只给君主姓名年龄，这里把文件里能写的国家各项(宗主、年号、前史、宗室后妃、名将名臣、近代政治、属地)也摆出来，
// 实在没摆的在「原始数据」里直接改这个国家的 JSON。
(function () {
  const D = window.SD, SM = window.SM, UI = window.UI, U = UI.U, el = UI.el;
  const side = () => document.getElementById("side");

  const IDEOLOGIES = [["Anarchism", "无政府主义"], ["SocialDemocracy", "社会民主主义"], ["Libertarianism", "自由意志主义"], ["SocialLiberalism", "社会自由主义"],
    ["Capitalism", "资本主义"], ["ConservativeLiberalism", "保守自由主义"], ["Centrism", "中间派"], ["Socialism", "工团主义"], ["Communism", "社会主义"],
    ["ReligiousDemocracy", "教派民主主义"], ["Authoritarianism", "权威主义"], ["Conservatism", "保守主义"], ["Fascism", "法西斯主义"]];
  const RELATION_NAMES = { war: "宣战", truce: "停战", alliance: "结盟", marriage: "联姻", pact: "通好", protect: "保护", tribute: "称臣纳贡",
    vassal: "立为附庸", union: "拉入关税同盟", puppet: "立为傀儡", friend: "旧谊", feud: "宿怨" };
  const RELS = [["son", "子"], ["daughter", "女"], ["grandson", "孙"], ["granddaughter", "孙女"], ["brother", "兄弟"], ["sister", "姐妹"],
    ["nephew", "侄"], ["cousin", "远支宗室"], ["wife", "正妻"], ["consort", "妃嫔"]];
  const CONDITION_NAMES = { holds_city: "某国据有某城", holds_region: "某国据有某法理过半", gone: "某国已亡", alive: "某国尚在", empire: "某国已称帝",
    at_war: "两国交战中", strain_above: "某国王朝积弊高于", before: "开局未满几年", dead: "某人已不在世" };
  const EFFECT_NAMES = { relation: "两国关系", proclaim: "称帝", rename: "改国号", move_capital: "迁都", culture: "改文化", adopt_huaxia: "采纳华夏制度(入主)",
    rebel: "起兵割据", cede: "献城改投", dies: "人物身故", heir: "立储", grant_title: "封官授爵", submit: "奉表归附", rally: "号召归附",
    puppet: "扶植傀儡政权", ideology: "改执政理念", exile: "政府流亡", banner_city: "建满城", banner_enclosure: "跑马圈地", banner_law: "颁行逃人法" };
  const DECLINE_NAMES = { age: "国祚(年)", strain: "王朝积弊", graft: "吏治败坏", annexation: "土地兼并", bloat: "冗官冗费", military: "武备废弛",
    factionalism: "党争", levies: "苛捐杂税" };
  const SPECIES = [["human", "人类"], ["orc", "兽人"], ["elf", "精灵"], ["dwarf", "矮人"]];

  // ---------- 搭输入框的小工具 ----------
  const commit = (change) => { change(); UI.refresh(); };
  const mutate = change => commit(() => D.mutate(change));

  // 写一项：空的就把键去掉(文件里不留空串、false)
  function setKey(target, key, value) {
    if (value === "" || value === null || value === undefined || value === false) delete target[key];
    else target[key] = value;
  }

  const field = (label, control, hint) => el("label", { class: "field" }, label, control, hint ? el("span", { class: "hint" }, hint) : null);

  function text(value, onCommit, attrs = {}) {
    return el("input", {
      type: "text", value: value ?? "", ...attrs,
      onchange: event => onCommit(event.target.value.trim()),
      onkeydown: event => { if (event.key === "Enter") event.target.blur(); }
    });
  }

  function number(value, onCommit, attrs = {}) {
    return el("input", {
      type: "number", value: value ?? "", ...attrs,
      onchange: event => { const raw = event.target.value.trim(); onCommit(raw === "" ? null : Number(raw)); },
      onkeydown: event => { if (event.key === "Enter") event.target.blur(); }
    });
  }

  function select(options, value, onCommit, attrs = {}) {
    const node = el("select", { ...attrs, onchange: event => onCommit(event.target.value) });
    const known = options.some(option => option[0] === (value ?? ""));
    for (const [key, label] of known ? options : [[value ?? "", value ? `${value}(不认得)` : "(没定)"], ...options])
      node.append(el("option", { value: key, selected: key === (value ?? "") }, label));
    return node;
  }

  const check = (label, value, onCommit) => el("label", { class: "check" },
    el("input", { type: "checkbox", checked: !!value, onchange: event => onCommit(event.target.checked) }), label);

  const button = (label, onClick, cls = "small", attrs = {}) => el("button", { class: cls, onclick: onClick, ...attrs }, label);
  const card = (title, ...children) => el("div", { class: "card" }, el("h4", {}, title), ...children);
  const hint = (textValue, warn) => el("div", { class: "hint" + (warn ? " warn" : "") }, textValue);
  const fold = (title, open, ...children) => el("details", { open }, el("summary", {}, title), el("div", {}, ...children));
  const swatch = key => { const c = D.colorOfKey(key); return el("span", { class: "swatch", style: `background:rgb(${c.join(",")})` }); };

  const cultureOptions = (blank) => [...(blank ? [["", blank]] : []), ...D.S.cultureKeys.map(key => [key, UI.cultureLabel(key)])];
  const polityOptions = (blank = "(没选)") => [["", blank], ...D.S.era.polities.map(item => [item.id, `${D.nameOf(item)} (${item.id})`])];
  const regionOptions = () => D.regions().map(item => [item.id, `${D.regionTitle(item)} · ${D.nameOf(item)} (${D.regionCityCount(item.id)} 城)`]);

  // 城、地区的备选(输入框下拉里按编号或名字找)
  function dataLists() {
    const cities = el("datalist", { id: "dlCities" }), regions = el("datalist", { id: "dlRegions" }), species = el("datalist", { id: "dlSpecies" });
    for (const item of D.cities()) if (D.active(item)) cities.append(el("option", { value: item.id }, D.cityName(item)));
    for (const item of D.regions()) regions.append(el("option", { value: item.id }, D.regionTitle(item) + " " + D.nameOf(item)));
    for (const item of D.S.era.titles || []) regions.append(el("option", { value: item.id }, D.first(item.t) + " " + (item.zh || "")));
    for (const [id, name] of SPECIES) species.append(el("option", { value: id }, name));
    return el("div", { hidden: true }, cities, regions, species);
  }

  const cityLabel = id => { const item = D.city(id); return item ? D.cityName(item) : id ? `${id}(城表上没有)` : ""; };

  // ---------- 剧本 ----------
  function scenarioCard() {
    const era = D.S.era;
    const frame = SM.frameBounds();
    const sizeText = era.size ? `${era.size[0] * 64}×${era.size[1] * 64} 格(剧本定的)` : frame ? `开局时选大中小；填了就定死` : "开局时选";
    const setSide = (at, tiles) => mutate(() => {
      if (tiles === null) { delete era.size; return; }
      const chunks = Math.max(4, Math.min(48, Math.round(tiles / 64)));
      const current = era.size || (frame ? frame.size : U.importInfo?.sizes?.[1] || [32, 15]);
      era.size = [Math.max(4, Math.min(48, current[0])), Math.max(4, Math.min(48, current[1]))];
      era.size[at] = chunks;
      // 大小定死了范围也得定：一格多少度是范围除以大小
      if (!era.frame && !era.map) era.frame = "eurasia";
    });
    const problems = D.problems();
    const tech = era.tech || {};
    return card("剧本",
      field("名称", text(era.zh, value => commit(() => { if (value && value !== era.zh) D.mutate(() => { const old = era.zh; era.zh = value; for (const key of ["tw", "en"]) if (!era[key] || era[key] === old) era[key] = value; }); }))),
      el("div", { class: "row" },
        field("开局年份(公元前写负数)", number(era.year, value => { if (Number.isInteger(value) && value !== 0) mutate(() => { era.year = value; }); })),
        field("作者", text(era.author, value => mutate(() => { era.author = value; })))),
      field("简介", el("textarea", { style: "min-height:54px", onchange: event => mutate(() => { const value = event.target.value.trim(), old = era.dzh || ""; era.dzh = value; for (const key of ["dtw", "den"]) if (!era[key] || era[key] === old) era[key] = value; }) }, era.dzh || "")),
      el("div", { class: "row" },
        field("地图", select([["earth", "地球"], ["import", "导入 map.png"]], era.map ? "import" : "earth", value => {
          if (!confirm("更换地图将清空城市、国家、法理、关系、长城与事件(可撤销)。确定更换？")) { UI.refresh(); return; }
          UI.setImport(value === "import");
        })),
        era.map ? null : field("范围", select([["", "生成时再选"], ["eurasia", "欧亚非"], ["oldworld", "亚欧非全图"], ["eastasia", "东亚(半幅)"], ["westworld", "欧非西亚(半幅)"]],
          era.frame || "", value => { mutate(() => setKey(era, "frame", value)); SM.fit(); })),
        field("列在", select([["east", "东方"], ["west", "西方"]], era.focus || "east", value => mutate(() => { era.focus = value; })))),
      era.map ? el("div", { class: "row" },
        field("读法", select([["color", "按颜色认地形"], ["height", "灰度高程"]], era.map.mode || "color", value => mutate(() => { era.map.mode = value; }))),
        era.map.mode === "height" ? field("海平面(0~1)", number(era.map.sea ?? 0.4, value => {
          if (value === null) return;
          mutate(() => { era.map.sea = Math.min(0.98, Math.max(0.02, value > 1 ? value / 100 : value)); });
        }, { step: "0.05" })) : null) : null,
      era.map ? el("div", { class: "row" },
        button("选一张图作 map.png", () => UI.pickMapImage(), "small primary"),
        button("读入地图", () => UI.reloadMapImage())) : null,
      era.map ? hint(U.importInfo?.blank ? "剧本文件夹里还没有 map.png：现在显示一片海。选一张图，或把 map.png 放进剧本文件夹再点「读入地图」。"
        : `${U.importInfo?.cols}×${U.importInfo?.rows} 像素，一像素一格(0.1°)，城的经纬度从左下角 0 起 · 颜色对照表在剧本文件夹的 map_colors.txt 中`, U.importInfo?.blank) : null,
      el("div", { class: "row" },
        field("东西长(格)", number(era.size ? era.size[0] * 64 : null, value => setSide(0, value), { placeholder: "不定" })),
        field("南北宽(格)", number(era.size ? era.size[1] * 64 : null, value => setSide(1, value), { placeholder: "不定" }))),
      hint(sizeText + "(按 64 格取整)；两格都清空回到开局时选大中小。定了范围的，地图上画出框，框外的开局没有。"),
      el("div", { class: "row" },
        check("本剧本无人区：开启(不看模组设置)", era.own_wilderness, value => mutate(() => setKey(era, "own_wilderness", value))),
        D.S.land.size > 0 && [...D.S.land.values()].some(value => value.startsWith("c:")) || era.exact
          ? check("版图：照原图分地", era.exact, value => mutate(() => {
            setKey(era, "exact", value);
            if (value && (D.S.landBase.size === 0 || era.base_stale)) {
              delete era.base_stale;
              D.S.landBase = new Map([...D.S.land].filter(([, key]) => key.startsWith("c:")));
            }
          })) : null),
      hint(era.exact ? "只按刷的地块分地：城不往外铺、不补小国，别的地没人领。" : "城从城心往外铺，铺不到的地补小国；刷的地块先归刷的那家。"),
      hint(D.ownWorld() ? `自带城表：${D.cities().length} 城、${D.regions().length} 个王国法理` : "用内置城表；头一回改城或法理时整套拷进剧本"),
      fold("科技档(文化 → 档)", false,
        hint("开局各文化的科技档(Technology/TechTree.json 的 tier)，* 为其余文化；不写的按 3。"),
        ...Object.keys(tech).map(key => el("div", { class: "row" },
          el("span", { class: "grow" }, key === "*" ? "* 其余文化" : UI.cultureLabel(key)),
          number(tech[key], value => { if (Number.isInteger(value)) mutate(() => { era.tech[key] = value; }); }, { class: "w60" }),
          button("×", () => mutate(() => { delete era.tech[key]; })))),
        el("div", { class: "row" },
          select([["", "加一个文化…"], ["*", "* 其余文化"], ...cultureOptions().filter(option => !(option[0] in tech))], "", value => { if (value) mutate(() => { (era.tech ||= {})[value] = 3; }); }))),
      problems.length ? el("div", { class: "problems" }, ...problems.map(item => el("div", {}, "· " + item))) : hint("没查出对不上的地方(保存后可再跑 Tools/WorldMap/check_scenario.py 细查)"),
      el("div", { class: "row" },
        button("清空全部城和国家", () => { if (UI.confirmTwice("clearCities", "五秒内再点一次：全部城和国家都清掉，地图上一座城也不留(可撤销)")) { UI.say(D.clearAllCities()); UI.fixSelection(); UI.refresh(); } }, "small danger")));
  }

  // ---------- 城 ----------
  function placeCard() {
    return card("放城",
      field("新城的文化", select(cultureOptions("随所在地"), U.newCityCulture, value => { U.newCityCulture = value; UI.refresh(); })),
      hint("城的文化就是它所在王国法理的文化：选了文化而所在法理不是这种文化时，几度内有这种文化的城就跟它的法理，没有就给这座城另立一个。"));
  }

  function cityCard() {
    const era = D.S.era, item = D.city(U.sel.city);
    if (!item) return card("城", hint(U.tool === "city" ? "点陆地立一座城，或点一座城选中" : "点一座城选中"));
    const owner = D.ownerOf(item), live = D.active(item), region = D.region(item.r);
    const shares = (item.share || []).map(id => D.regionTitle(D.region(id)) || id);
    const own = era.species?.[item.id] || "";
    return card(el("span", {}, swatch(owner ? "p:" + owner.id : "i:" + item.id), `城 · ${D.cityName(item)}`),
      el("div", { class: "row" },
        field("城名", text(D.cityName(item), value => commit(() => D.renameCity(item.id, value)))),
        field("大小", select([["1", "大城"], ["2", "中城"], ["3", "小城"], ["0", "0 档"]], String(D.rankOf(item)), value => commit(() => D.setRank(item.id, Number(value)))))),
      hint(`编号 ${item.id} · 经 ${item.x} 纬 ${item.y} · ${live ? (owner ? "开局归 " + D.nameOf(owner) + (owner.cap === item.id ? "(都城)" : "") : "独立") : "开局年还没有这座城"}`, !live),
      hint(`王国法理：${D.regionTitle(region) || "(没有，这座城不会出现)"}${shares.length ? " · 兼属：" + shares.join("、") : ""}`, !region),
      live && SM.M.unplaced?.has(item.id) ? hint({ outside: "开局放不下：超出地图范围或太靠边，这座城不会出现", small: "小图不放小城：选小图时这座城不会出现",
        region: "没有所属的王国法理，这座城不会出现" }[SM.M.unplaced.get(item.id)] || "开局放不下，这座城不会出现", true) : null,
      live && SM.M.crowded?.has(item.id) ? hint("和别的城挤在同一块地，开局会挪开几块", true) : null,
      el("div", { class: "row" },
        field("文化", select(cultureOptions(), D.cityCulture(item.id), value => commit(() => D.setCityCulture(item.id, value)))),
        field("种族", text(own, value => commit(() => D.setCitySpecies(item.id, value)), { list: "dlSpecies", placeholder: "随国家 / 随文化" }))),
      hint("小图只放大城、中城；都城应是大城或中城。改文化：法理里只有它一座城就改这个法理的文化，还有别的城就以它另立一个王国法理。"),
      el("div", { class: "row" },
        owner ? null : button("以此城立国", () => commit(() => { const result = D.newPolity(item.id); UI.say(result.message); if (result.polity) { U.sel.realm = result.polity.id; UI.setTool("realm"); } }), "small primary", { disabled: !live }),
        owner && owner.cap !== item.id ? button(`立为${D.nameOf(owner)}都城`, () => commit(() => UI.say(D.setCapital(owner, item.id)))) : null,
        owner ? button("让它独立", () => commit(() => { UI.say(D.makeIndependent(item.id)); UI.pickCity(item); })) : null,
        button("删城", () => { if (UI.confirmTwice("city:" + item.id, `五秒内再点一次删掉「${D.cityName(item)}」`)) commit(() => { UI.say(D.deleteCity(item.id)); UI.fixSelection(); }); }, "small danger")));
  }

  // ---------- 点地图时(只选中 / 划入 / 兼属) ----------
  function modeCard() {
    const names = { realm: "把城划给它", kingdom: "把城划进来", empire: "把王国法理收进来", division: "把城划进来" };
    const set = mode => { U.mode = mode; UI.refresh(); };
    const info = U.mode === "select" ? "点城只是选中，不改任何东西；要改归属先按上面的按钮"
      : U.tool === "realm" ? "点或拖过城划进来；右键点城让它独立；Shift+点城换一国"
      : U.tool === "kingdom" ? (U.mode === "share" ? "点城加上或去掉兼属(至多两个)；Shift+点城换法理" : "点或拖过城划进来，城的国家不变；Shift+点城换法理")
      : U.tool === "empire" ? "点或拖过城，把它所在的王国法理收进来；右键点城拿出来；Shift+点城换帝国法理"
      : "点或拖过本国的城划进来；右键点城放回京畿；Shift+点城换行政区";
    return card("点地图时",
      el("div", { class: "row" },
        button("只选中", () => set("select"), "small" + (U.mode === "select" ? " primary" : "")),
        button(names[U.tool], () => set("write"), "small" + (U.mode === "write" ? " primary" : "")),
        U.tool === "kingdom" ? button("设为兼属", () => set("share"), "small" + (U.mode === "share" ? " primary" : "")) : null),
      hint(info));
  }

  // ---------- 国家 ----------
  function personRow(list, index, person, kin) {
    const set = (key, value) => mutate(() => setKey(person, key, value));
    const setNumber = (key, value, none = -1) => mutate(() => { if (value === null || value === none) delete person[key]; else person[key] = Math.round(value); });
    const noFamily = person.fam === "";
    return el("div", { class: "person" },
      el("div", { class: "row" },
        el("span", { class: "idx" }, String(index)),
        text(person.fam, value => mutate(() => { if (value) person.fam = value; else delete person.fam; }), { placeholder: noFamily ? "无姓" : "姓(空=随机)", class: "w80", disabled: noFamily }),
        text(person.giv, value => set("giv", value), { placeholder: "名 简体|英文", class: "grow" }),
        number(person.age > 0 ? person.age : null, value => setNumber("age", value), { placeholder: "岁", class: "w60" }),
        select([["", "性别"], ["m", "男"], ["f", "女"]], person.sex || "", value => set("sex", value), { class: "w60" })),
      kin ? el("div", { class: "row" },
        select([["", "关系…"], ...RELS], person.rel || "", value => set("rel", value), { class: "w110" }),
        field("of", number(person.of >= 0 ? person.of : null, value => setNumber("of", value), { placeholder: "君主", title: "子孙：父亲在本表的序号；后妃：丈夫的序号。空着是君主" })),
        field("生母", number(person.mo >= 0 ? person.mo : null, value => setNumber("mo", value), { title: "生母在本表的序号(得是正妻或妃嫔)" })),
        field("同家", number(person.in >= 0 ? person.in : null, value => setNumber("in", value), { title: "后妃：与本表这一位同一娘家" })),
        check("储君", person.heir, value => set("heir", value))) : el("div", { class: "row" },
        select([["minister", "名臣"], ["general", "名将"]], person.kind || "minister", value => set("kind", value), { class: "w80" }),
        select([["0", "良才"], ["1", "名"], ["2", "国柱"]], String(person.rank || 0), value => mutate(() => { person.rank = Number(value); }), { class: "w80" }),
        text(person.post, value => set("post", value), { list: "dlPosts", placeholder: "职位或坐镇的城", class: "grow" }),
        field("同家", number(person.in >= 0 ? person.in : null, value => setNumber("in", value), { title: "与本国宗室表里这一位后妃同一家" }))),
      el("div", { class: "row" },
        check("无姓(只称名)", noFamily, value => mutate(() => { if (value) person.fam = ""; else delete person.fam; })),
        kin ? text(person.house, value => set("house", value), { placeholder: "娘家是哪国(国编号)", class: "grow" }) : el("span", { class: "grow" }),
        button("删", () => mutate(() => list.splice(index, 1)), "small danger")));
  }

  function realmCard() {
    const era = D.S.era, s = U.sel, item = D.polity(s.realm);
    const picker = el("div", { class: "row" },
      select(polityOptions(era.polities.length ? "选国家…" : "还没有国家"), s.realm || "", value => {
        s.realm = value || null;
        const seat = D.city(D.polity(value)?.cap);
        if (seat) { s.city = seat.id; SM.centerOn(seat.x, seat.y); }
        UI.refresh();
      }, { class: "grow" }));
    if (!item) return card("国家", picker, hint(era.polities.length ? "点地图上的城，或在上面选一个国家" : "还没有国家：用城市工具点陆地放城，再按「以此城立国」"));
    const selected = D.city(s.city), claimed = (item.r || []).map(id => [id, D.regionTitle(D.region(id)) || id + "(不存在)"]);
    const ruler = item.ruler || {};
    const setRuler = (key, value) => mutate(() => { item.ruler ||= {}; setKey(item.ruler, key, value); if (!Object.keys(item.ruler).length) delete item.ruler; });
    const modern = item.modern || {};
    const setModern = (key, value) => mutate(() => { item.modern ||= {}; setKey(item.modern, key, value); if (!Object.keys(item.modern).length) delete item.modern; });
    const flag = (key, label, title) => el("span", { title }, check(label, item[key], value => mutate(() => setKey(item, key, value))));
    const raw = el("textarea", { class: "code" }, D.stringify(item));
    const posts = el("datalist", { id: "dlPosts" }, ...[["chief", "首席朝官"], ["core", "中枢要职"], ["div", "部寺"], ["general", "统一支军"], ["pm", "首相、总理"], ["none", "不任官"]]
      .map(([id, name]) => el("option", { value: id }, name)));
    return card(el("span", {}, swatch("p:" + item.id), `国家 · ${D.nameOf(item)}`),
      picker, posts,
      el("div", { class: "row" },
        field("国名", text(item.zh, value => commit(() => D.renamePolity(item, value)))),
        field("英文名", text(item.en, value => mutate(() => setKey(item, "en", value)))),
        field("繁体名", text(item.tw, value => mutate(() => setKey(item, "tw", value))))),
      el("div", { class: "row" },
        field("文化", select(cultureOptions(), item.c || "", value => mutate(() => { item.c = value; }))),
        field("种族", text(item.species, value => mutate(() => setKey(item, "species", value)), { list: "dlSpecies", placeholder: "随文化" }))),
      hint(`编号 ${item.id} · 都城：${item.cap ? cityLabel(item.cap) : "没有(开局这国不存在)"} · 开局 ${D.cityCount(item)} 城`, !item.cap || !D.city(item.cap)),
      el("div", { class: "row" },
        check("开局称帝", item.emp, value => mutate(() => setKey(item, "emp", value))),
        check("国号加「大」", item.grand, value => mutate(() => setKey(item, "grand", value))),
        selected && D.active(selected) && item.cap !== selected.id ? button(`立「${D.cityName(selected)}」为都城`, () => commit(() => UI.say(D.setCapital(item, selected.id)))) : null),
      el("div", { class: "row" },
        field("宗主(开局即为其成员)", select(polityOptions("无").filter(option => option[0] !== item.id), item.of || "", value => mutate(() => setKey(item, "of", value)))),
        field("帝国法理核心", select([["", "不定"], ...D.cores().map(core => [core.id, `${D.coreName(core)} (${core.id})`])], item.core || "", value => mutate(() => setKey(item, "core", value))))),
      el("div", {},
        hint("认领的王国法理(里面没被别国点名的城都归这国)："),
        el("div", { class: "chips" }, ...claimed.map(([id, name]) => el("span", { class: "chip" }, name, button("×", () => commit(() => UI.say(D.toggleClaim(item, id)))))),
          selected ? button((item.r || []).includes(selected.r) ? "不再认领选中城的法理" : "认领选中城的法理", () => commit(() => UI.say(D.toggleClaim(item, selected.r)))) : null)),
      (item.cities || []).length ? hint("点名的城：" + item.cities.map(cityLabel).join("、")) : null,

      fold("君主", true,
        el("div", { class: "row" },
          field("君主姓", text(ruler.fam, value => mutate(() => { item.ruler ||= {}; if (value) item.ruler.fam = value; else delete item.ruler.fam; if (!Object.keys(item.ruler).length) delete item.ruler; }), { placeholder: ruler.fam === "" ? "无姓" : "空=照文化起", disabled: ruler.fam === "" })),
          field("君主名", text(ruler.giv, value => setRuler("giv", value), { placeholder: "空=照文化起" })),
          field("年龄", number(ruler.age > 0 ? ruler.age : null, value => mutate(() => { item.ruler ||= {}; if (value > 0) item.ruler.age = Math.round(value); else delete item.ruler.age; if (!Object.keys(item.ruler).length) delete item.ruler; }), { class: "w60" }))),
        el("div", { class: "row" },
          check("无姓(只称名)", ruler.fam === "", value => mutate(() => { item.ruler ||= {}; if (value) item.ruler.fam = ""; else delete item.ruler.fam; if (!Object.keys(item.ruler).length) delete item.ruler; })),
          check("女主", ruler.sex === "f", value => setRuler("sex", value ? "f" : "")),
          check("雄主", item.hero, value => mutate(() => setKey(item, "hero", value)))),
        hint("姓、名写「简体|英文|繁体」，英文、繁体可省：朱|Zhu、由检|Youjian。西方人物勾无姓，全名写在名里。"),
        el("div", { class: "row" },
          field("年号", text(item.era_name, value => mutate(() => setKey(item, "era_name", value)), { placeholder: "崇祯|Chongzhen" })),
          field("第几年", number(item.era_year, value => mutate(() => { if (value > 0) item.era_year = Math.round(value); else delete item.era_year; }), { class: "w60" })),
          field("前史朝代", text(item.dynasty, value => mutate(() => setKey(item, "dynasty", value)), { placeholder: "history.json 的编号" })))),

      fold(`宗室与后妃(${(item.kin || []).length})`, false,
        hint("序号从 0 起，就是 of、生母、同家里填的那个数。宗室不写姓随君主姓；正妻开局与丈夫成婚，一人只能有一位正妻。"),
        ...(item.kin || []).map((person, index) => personRow(item.kin, index, person, true)),
        button("+ 宗室或后妃", () => mutate(() => { (item.kin ||= []).push({ giv: "", rel: "son" }); }))),

      fold(`名将名臣(${(item.famous || []).length})`, false,
        hint("事件里要点名的人(起兵、献城、身故)得先写在这里或宗室里；职位可写城编号，坐镇该城。"),
        ...(item.famous || []).map((person, index) => personRow(item.famous, index, person, false)),
        button("+ 名将名臣", () => mutate(() => { (item.famous ||= []).push({ giv: "", kind: "minister", rank: 0 }); }))),

      fold("近代政治", !!item.modern,
        el("div", { class: "row" },
          field("政体", select([["", "照文化"], ["republic", "共和"], ["constitutional", "君主立宪"], ["monarchy", "君主国"]], modern.regime || "", value => setModern("regime", value))),
          field("执政理念", select([["", "不定"], ...IDEOLOGIES], modern.ideology || "", value => setModern("ideology", value)))),
        el("div", { class: "row" },
          field("执政党名号", text(modern.party, value => setModern("party", value), { placeholder: "简体|英文|繁体" })),
          check("一党专政", modern.one_party, value => setModern("one_party", value))),
        field("元首职位", text(item.title, value => mutate(() => setKey(item, "title", value)), { placeholder: "委员长|Chairman" })),
        hint("共和、立宪要和「开局称帝」一起勾才组建政府、设议会。别的政党(parties)在下面的原始数据里写。")),

      declineFold(item),

      fold("附属与其它", false,
        el("div", { class: "row" },
          field("本身是谁的属地", select(polityOptions("不是").filter(option => option[0] !== item.id), item.colony_of || "", value => mutate(() => setKey(item, "colony_of", value)))),
          field("属地类型", select([["", "直辖殖民地(默认)"], ["governorate", "直辖殖民地"], ["company", "特许公司"], ["protectorate", "保护国"], ["dominion", "自治领"], ["mandate", "委任统治地"], ["lease", "租借地"]],
            item.dependency || "", value => mutate(() => setKey(item, "dependency", value))))),
        el("div", { class: "row" },
          field("流亡在哪国", select(polityOptions("不流亡").filter(option => option[0] !== item.id), item.exile || "", value => mutate(() => setKey(item, "exile", value)))),
          field("旧都(留都)", text(item.old_capital, value => mutate(() => setKey(item, "old_capital", value)), { list: "dlCities" }))),
        el("div", { class: "row" },
          field("入主华夏时的国号", text(item.conquest, value => mutate(() => setKey(item, "conquest", value)), { placeholder: "清|Qing" })),
          field("据有即算入关的城", text(item.conquest_city, value => mutate(() => setKey(item, "conquest_city", value)), { list: "dlCities" }))),
        el("div", { class: "row" },
          field("开局就用的朝廷(文化)", select(cultureOptions("不另设"), item.court || "", value => mutate(() => setKey(item, "court", value)))),
          field("简称", text(item.abbr, value => mutate(() => setKey(item, "abbr", value)), { placeholder: "英|UK" }))),
        el("div", { class: "row" },
          flag("autonomy", "地方实力派", "现代国家底下留兵留财政的成员，要和宗主一起写"),
          flag("banner", "八旗多元制", "要和开局称帝一起勾"),
          flag("holy", "神圣之名", "开局取得所在天下的普世之名，再授神圣"),
          flag("shogun", "幕府将军家", "本国君主就任宗主帝国的征夷大将军"),
          flag("king", "已称王(周制战国)", "只用于战国"),
          flag("company", "特许公司", "和属地一起写"))),

      fold("原始数据(这个国家的 JSON)", false,
        hint("上面没摆出来的项(行政区的范围、殖民地、别的政党……)在这里直接改；键名、取值见 Docs/scenario-authoring-guide.md。"),
        raw,
        el("div", { class: "row" }, button("写回", () => {
          let parsed;
          try { parsed = JSON.parse(raw.value); } catch (error) { UI.say("JSON 读不出来：" + error.message, true); return; }
          if (!parsed || typeof parsed !== "object" || Array.isArray(parsed) || !parsed.id) { UI.say("得是一个带 id 的对象", true); return; }
          if (parsed.id !== item.id && D.polity(parsed.id)) { UI.say("国家编号重复：" + parsed.id, true); return; }
          commit(() => D.mutate(() => { for (const key of Object.keys(item)) delete item[key]; Object.assign(item, parsed); }));
          U.sel.realm = parsed.id;
          UI.say("已写回(可撤销)");
          UI.refresh();
        }))),

      el("div", { class: "row" },
        button("删国家", () => { if (UI.confirmTwice("realm:" + item.id, `五秒内再点一次删除「${D.nameOf(item)}」，其城市将改为独立或按认领归别国`)) commit(() => { UI.say(D.deletePolity(item)); UI.fixSelection(); }); }, "small danger")));
  }

  function declineFold(item) {
    return fold("国祚与积弊", !!item.decline,
      item.emp ? null : hint("开局称帝才有国祚与王朝积弊", true),
      el("div", { class: "grid two" }, ...D.DECLINE_KEYS.map(key => field(DECLINE_NAMES[key],
        number(item.decline?.[key] ?? null, value => commit(() => D.setDecline(item, key, value === null ? "" : String(value))), { placeholder: "照默认" })))),
      hint("空着照默认。国祚：开局时本朝已立几年，不满 10 年算新开国；积弊 60 起入危机；六弊不写的补到积弊的一半。"),
      el("div", { class: "row" }, button("清回默认", () => mutate(() => { delete item.decline; }), "small", { disabled: !item.decline })));
  }

  // ---------- 行政区 ----------
  function divisionCard() {
    const s = U.sel, item = D.polity(s.realm);
    if (!item) return card("行政区", hint("先点一国的城选中这个国家"));
    const divisions = item.divisions || [], current = D.division(item, s.division);
    const members = current ? (current.cities || []).map(cityLabel) : D.cities().filter(c => D.active(c) && D.ownerOf(c) === item && D.divisionOf(item, c.id) < 0).map(c => D.cityName(c));
    const seats = D.seatsOf(item, s.division) || [];
    return card(`行政区 · ${D.nameOf(item)}：${divisions.length} 个`,
      item.emp ? null : hint("该国开局不称帝，行政区不会生效。可在国家卡片中勾「开局称帝」", true),
      el("div", { class: "row" },
        select([["-1", D.divisionName(item, -1)], ...divisions.map((division, index) => [String(index), D.first(division.name) || "?"])], String(s.division),
          value => { s.division = Number(value); UI.refresh(); }, { class: "grow" }),
        button("新行政区", () => commit(() => { s.division = D.newDivision(item); U.mode = "write"; UI.say("新立了一个行政区：点或拖过本国的城划进去"); })),
        current ? button("删", () => commit(() => { D.deleteDivision(item, s.division); s.division = -1; }), "small danger") : null),
      current ? field("名称(简体|英文|繁体)", text(current.name, value => commit(() => D.renameDivision(item, s.division, value)))) : hint(`都城${cityLabel(item.cap)}留在京畿直辖，划不进行政区`),
      hint(`${members.length} 城：${members.join("、") || "(还没有)"}`),
      el("div", { class: "row" },
        button(U.seatMode ? "点图加治所：开" : "点图加治所：关", () => { U.seatMode = !U.seatMode; UI.refresh(); }, "small" + (U.seatMode ? " primary" : "")),
        button(`清掉治所(${seats.length})`, () => commit(() => D.clearSeats(item, s.division)), "small", { disabled: seats.length === 0 })),
      hint("治所：当年州府治所所在的位置，不会建城。开局时按治所划分各行政区的边界；没写治所的按辖下的城与地形推算。"));
  }

  // ---------- 王国法理 ----------
  function regionCard() {
    const s = U.sel, item = D.region(s.region), selected = D.city(s.city);
    const picker = el("div", { class: "row" }, select([["", "选法理…"], ...regionOptions()], s.region || "", value => { s.region = value || null; UI.refresh(); }, { class: "grow" }));
    if (!item) return card("王国法理", picker, hint("点地图上的城，或在上面选一个法理"));
    const core = D.coreOfRegion(item.id);
    const claimers = D.S.era.polities.filter(polity => (polity.r || []).includes(item.id)).map(D.nameOf);
    return card(el("span", {}, swatch("r:" + item.id), `王国法理 · ${D.regionTitle(item)}`), picker,
      el("div", { class: "row" },
        field("地区名", text(item.zh, value => commit(() => D.renameRegion(item.id, value)))),
        field("法理名", text(D.regionTitle(item), value => commit(() => D.setRegionTitle(item.id, value)), { title: "华夏一般是单字国名：雍、蜀……可写 简体|英文|繁体" }))),
      el("div", { class: "row" },
        field("文化", select(cultureOptions(), D.regionCulture(item), value => commit(() => D.setRegionCulture(item.id, value)))),
        field("首都", el("input", { value: D.regionCapitalName(item) || "(没有城)", disabled: true }))),
      hint(`编号 ${item.id} · ${D.regionCityCount(item.id)} 城 · 帝国法理：${core ? D.coreName(core) : "(未归入)"}${claimers.length ? " · 认领它的国：" + claimers.join("、") : ""}`),
      el("div", { class: "row" },
        selected && selected.r === item.id && item.cap !== selected.id ? button(`立「${D.cityName(selected)}」为王国法理首都`, () => commit(() => UI.say(D.setRegionCapital(item.id, selected.id)))) : null,
        selected ? button("选中城另立法理", () => commit(() => { s.region = D.newRegion(selected.id); UI.say("另立了一个王国法理，右栏改名"); })) : null,
        button("删王国法理", () => commit(() => { UI.say(D.deleteRegion(item.id)); UI.fixSelection(); }), "small danger")),
      selected && (selected.share || []).length ? hint(`「${D.cityName(selected)}」兼属：${selected.share.map(id => D.regionTitle(D.region(id)) || id).join("、")}`) : null);
  }

  // ---------- 剧本自带的王国法理(titles) ----------
  function titlesCard() {
    const era = D.S.era, s = U.sel, list = era.titles || [], item = list[s.title], selected = D.city(s.city);
    const cityList = (key, label, note) => field(label, text((item[key] || []).join(", "), value => mutate(() => {
      const ids = value.split(/[,，、\s]+/).filter(Boolean);
      if (ids.length) item[key] = ids; else delete item[key];
    }), { placeholder: "城编号，逗号隔开" }), (item[key] || []).map(cityLabel).join("、") || note);
    return card(`剧本自带的王国法理(${list.length})`,
      fold("不照城表的地区分法理时才用", list.length > 0,
        hint("写进去的城改归这一块，没写的补空城跟同一原地区里最近的那座；编号也能当地区用(帝国法理收的、事件的法理、无人区挂靠)。一块也不写就照城表的地区立法理。"),
        el("div", { class: "row" },
          select([["-1", list.length ? "选一块…" : "还没有"], ...list.map((title, index) => [String(index), `${D.first(title.t) || title.zh || "?"} (${title.id})`])], String(s.title),
            value => { s.title = Number(value); UI.refresh(); }, { class: "grow" }),
          button("以选中城新立", () => {
            if (!selected) { UI.say("先点一座城：新立的这块以它为首府", true); return; }
            commit(() => D.mutate(() => {
              const titles = era.titles ||= [];
              let n = titles.length + 1;
              while (titles.some(title => title.id === "t" + n) || D.region("t" + n)) n++;
              const name = D.cityName(selected);
              titles.push({ id: "t" + n, zh: name, en: selected.en || name, tw: selected.tw || name, t: name, cap: selected.id, cities: [selected.id] });
              s.title = titles.length - 1;
            }));
          }, "small primary"),
          item ? button("删", () => commit(() => { D.mutate(() => { era.titles.splice(s.title, 1); if (!era.titles.length) delete era.titles; }); s.title = -1; }), "small danger") : null),
        item ? el("div", { class: "row" },
          field("编号", text(item.id, value => { if (/^[a-z0-9_]+$/.test(value) && !list.some(title => title !== item && title.id === value)) mutate(() => { item.id = value; }); else UI.say("编号只用小写字母、数字、下划线，且不重复", true); })),
          field("省份名", text(item.zh, value => mutate(() => { const old = item.zh; item.zh = value; for (const key of ["tw", "en"]) if (!item[key] || item[key] === old) item[key] = value; }))),
          field("法理名", text(item.t, value => mutate(() => setKey(item, "t", value)), { placeholder: "陕|Shaan" }))) : null,
        item ? el("div", { class: "row" },
          field("首府" + (item.cap ? " · " + cityLabel(item.cap) : ""), text(item.cap, value => mutate(() => setKey(item, "cap", value)), { list: "dlCities" })),
          selected && !(item.cities || []).includes(selected.id) ? button(`把「${D.cityName(selected)}」划进本属`, () => mutate(() => {
            for (const title of era.titles) { if (title.cities) title.cities = title.cities.filter(id => id !== selected.id); }
            (item.cities ||= []).push(selected.id);
          })) : null) : null,
        item ? cityList("cities", "本属的城", "(还没有，至少要一座)") : null,
        item ? cityList("share", "兼属的城", "") : null));
  }

  // ---------- 帝国法理与天下 ----------
  function coreCard() {
    const s = U.sel, item = D.core(s.core), selected = D.city(s.city), region = D.region(s.region);
    const picker = el("div", { class: "row" }, select([["", "选帝国法理…"], ...D.cores().map(core => [core.id, `${D.coreName(core)} (${(core.r || []).length} 块)`])], s.core || "",
      value => { s.core = value || null; UI.refresh(); }, { class: "grow" }));
    const found = region ? button(`以王国法理「${D.regionTitle(region)}」另立`, () => commit(() => { s.core = D.newCore(region.id); UI.say("另立了一个帝国法理，右栏改名、定首都与天下"); })) : null;
    if (!item) return card("帝国法理", picker, hint(region ? "这个王国法理尚未归入帝国法理。点下面的按钮可新立一个" : "点地图上的城，或在上面选一个帝国法理"), found);
    const table = D.tianxia();
    return card(el("span", {}, swatch("k:" + item.id), `帝国法理 · ${D.coreName(item)}`), picker,
      el("div", { class: "row" },
        field("名称", text(D.coreName(item), value => commit(() => D.renameCore(item.id, value)))),
        field("首都", el("input", { value: D.coreCapitalName(item) || "(没有城)", disabled: true }))),
      el("div", { class: "row" },
        field("所属天下", select([["", "自成一个天下"], ...Object.keys(table).sort().map(id => [id, D.first(table[id])])], item.tx || "", value => commit(() => D.setCoreTianxia(item.id, value)))),
        item.tx ? field("天下名", text(D.tianxiaName(item.tx), value => commit(() => D.renameTianxia(item.tx, value)))) : null,
        button("新天下", () => commit(() => D.newTianxia(item.id)))),
      el("div", {}, hint("收的王国法理："),
        el("div", { class: "chips" }, ...(item.r || []).map(id => el("span", { class: "chip" }, D.regionTitle(D.region(id)) || id, button("×", () => commit(() => UI.say(D.removeRegionFromCore(id)))))))),
      hint("开局称帝的国家仍以本国都城为帝国法理首都。"),
      el("div", { class: "row" },
        selected && (item.r || []).includes(selected.r) && item.cap !== selected.id ? button(`立「${D.cityName(selected)}」为帝国法理首都`, () => commit(() => UI.say(D.setCoreCapital(item.id, selected.id)))) : null,
        found,
        button("删帝国法理", () => commit(() => { D.deleteCore(item.id); s.core = null; }), "small danger")));
  }

  // ---------- 关系 ----------
  function relationCard() {
    const era = D.S.era, s = U.sel;
    const kinds = D.RELATION_KINDS.map(kind => [kind, RELATION_NAMES[kind]]);
    const from = D.polity(U.relationFrom);
    const shown = s.realm ? era.relations.filter(item => item.a === s.realm || item.b === s.realm) : era.relations;
    let other = "";
    const otherSelect = select(polityOptions("对方…").filter(option => option[0] !== U.relationFrom), "", value => { other = value; });
    return card("关系",
      el("div", { class: "row" },
        field("种类", select(kinds, U.relationKind, value => { U.relationKind = value; UI.refresh(); })),
        U.relationKind === "war" ? field("战争目标", select(D.WAR_GOALS.map(goal => [goal, goal === "迫使附庸" ? "臣服(迫使附庸)" : goal]), U.relationGoal, value => { U.relationGoal = value; })) : null),
      hint(`${D.relationSentence(U.relationKind, "起始国", "对方")}。再加一次同样的就是去掉。`),
      el("div", { class: "row" },
        field("起始国", select(polityOptions("点地图上的城…"), U.relationFrom || "", value => { U.relationFrom = value || null; s.realm = value || s.realm; UI.refresh(); })),
        field("对方", otherSelect),
        button("加上/去掉", () => {
          const target = D.polity(other);
          if (!from || !target) { UI.say("先定下起始国和对方", true); return; }
          commit(() => UI.say(D.toggleRelation(from, target, U.relationKind, U.relationGoal)));
        }, "small primary")),
      hint(s.realm ? `${D.nameOf(D.polity(s.realm))} 的关系(${shown.length})：` : `全部关系(${shown.length})：`),
      el("div", { class: "list-lines" }, ...shown.map(item => {
        const problem = D.relationProblem(D.polity(item.a), D.polity(item.b), item.kind);
        return el("div", { class: "line" }, el("span", {}, D.relationText(item), problem ? el("div", { class: "hint warn" }, problem) : null),
          item.kind === "friend" || item.kind === "feud" ? number(item.value ?? null, value => mutate(() => { if (value) item.value = Math.round(value); else delete item.value; }), { class: "w60", placeholder: "30", title: "分量" }) : null,
          item.kind === "war" ? text(item.city, value => mutate(() => setKey(item, "city", value)), { class: "w80", list: "dlCities", placeholder: "目标城", title: "夺城之战打下它就算赢，不写取对方都城" }) : null,
          button("×", () => mutate(() => era.relations.splice(era.relations.indexOf(item), 1))));
      })));
  }

  // ---------- 剧情事件 ----------
  const SLOT_NAMES = { a: "国", b: "对方", c: "第三方", city: "城", cities: "另带的城", region: "法理", value: "数值", name: "国号", person: "人物",
    relation: "关系", goal: "战争目标", culture: "文化", roving: "流寇", hero: "雄主", all: "全部城", party: "执政党", one_party: "一党专政" };
  const NAME_LABEL = { dies: "死因", grant_title: "爵位", ideology: "理念", puppet: "国号", rebel: "国号" };

  function slotControl(step, effect, slot) {
    const set = value => mutate(() => setKey(step, slot, value));
    const pick = () => { U.pick = { step, slot }; UI.refresh(); };
    const picking = U.pick && U.pick.step === step && U.pick.slot === slot;
    const pickButton = button(picking ? "在地图上点城…" : "点图", pick, "small" + (picking ? " primary" : ""));
    const label = slot === "name" && NAME_LABEL[step.kind] ? NAME_LABEL[step.kind]
      : slot === "b" && (step.kind === "rebel" || step.kind === "puppet") ? "新国编号"
      : slot === "c" ? (step.kind === "exile" ? "占领者" : "入侵者") : slot === "value" && step.kind === "before" ? "年" : SLOT_NAMES[slot];
    switch (slot) {
      case "a": case "b": case "c":
        if (slot === "b" && (step.kind === "rebel" || step.kind === "puppet"))
          return field(label, text(step.b, set, { placeholder: "自己起，如 shun；后面的事件用它" }));
        return el("div", { class: "row" }, field(label, select([["", "(没填)"], ...D.eventPolityIds().map(id => [id, `${D.nameOf(D.polity(id)) || "事件里立的国"} (${id})`])], step[slot] || "", set)), pickButton);
      case "city":
        return el("div", { class: "row" }, field(label + (step.city ? " · " + cityLabel(step.city) : ""), text(step.city, set, { list: "dlCities", placeholder: "城编号" })), pickButton);
      case "cities":
        return el("div", { class: "row" }, field(label + "(城编号，逗号隔开)", text((step.cities || []).join(", "), value => mutate(() => {
          const list = value.split(/[,，、\s]+/).filter(Boolean);
          if (list.length) step.cities = list; else delete step.cities;
        }), { placeholder: (step.cities || []).map(cityLabel).join("、") })), pickButton);
      case "region":
        return el("div", { class: "row" }, field(label + (step.region ? " · " + (D.regionTitle(D.region(step.region)) || D.first((D.S.era.titles || []).find(item => item.id === step.region)?.t) || "不存在") : ""), text(step.region, set, { list: "dlRegions", placeholder: "地区编号" })), pickButton);
      case "value": return field(label, number(step.value ?? null, value => mutate(() => { if (value === null) delete step.value; else step.value = Math.round(value); }), { class: "w80" }));
      case "relation": return field(label, select(D.RELATION_KINDS.map(kind => [kind, RELATION_NAMES[kind]]), step.relation || "war", value => mutate(() => {
        step.relation = value;
        if (value === "war") step.goal ||= D.WAR_GOALS[0]; else delete step.goal;
      })));
      case "goal": return field(label, select(D.WAR_GOALS.map(goal => [goal, goal]), step.goal || D.WAR_GOALS[0], set));
      case "culture": return field(label, select(cultureOptions("(没填)"), step.culture || "", set));
      case "roving": case "hero": case "all": case "one_party": return check(label, step[slot], value => set(value));
      case "name":
        if (step.kind === "ideology") return field(label, select([["", "(没填)"], ...IDEOLOGIES], step.name || "", set));
        return field(label, text(step.name, set, { placeholder: step.kind === "dies" ? "可省" : "简体|英文|繁体" }));
      case "person": {
        const unknown = D.unknownPeople(step.person);
        return field(label + "(简体姓+名，顿号隔开)", text(step.person, set, { placeholder: "李自成、刘宗敏" }),
          unknown.length ? `${unknown.join("、")} 不是剧本人物，开局找不到人` : null);
      }
      default: return field(label, text(step[slot], set));
    }
  }

  function stepBox(item, list, index, effect) {
    const step = list[index];
    const kinds = (effect ? D.EFFECT_KINDS : D.CONDITION_KINDS).map(kind => [kind, (effect ? EFFECT_NAMES : CONDITION_NAMES)[kind]]);
    return el("div", { class: "step" },
      el("div", { class: "row" },
        select(kinds, step.kind, value => mutate(() => {
          step.kind = value;
          if (effect && value === "relation" && !step.relation) { step.relation = "war"; step.goal = D.WAR_GOALS[0]; }
        }), { class: "grow" }),
        button("×", () => mutate(() => list.splice(index, 1)), "small danger")),
      ...D.slotsOf(step, effect).map(slot => slotControl(step, effect, slot)));
  }

  function eventCard() {
    const era = D.S.era, s = U.sel, item = era.events[s.event];
    const picker = el("div", { class: "row" },
      select([["-1", era.events.length ? "选事件…" : "还没有事件"], ...era.events.map((event, index) => [String(index), `${index + 1}. ${event.name || "(没名)"} · 第 ${event.after || 0} 年起`])],
        String(s.event), value => { s.event = Number(value); U.pick = null; UI.refresh(); }, { class: "grow" }),
      button("新事件", () => commit(() => { D.mutate(() => era.events.push({ name: "事件" + (era.events.length + 1), text: "", after: 0, when: [], then: [] })); s.event = era.events.length - 1; }), "small primary"));
    if (!item) return card("剧情事件", picker, hint("开局后每年查一次：开局满几年、条件全部满足就触发。还没有事件：点「新事件」加一条"));
    return card("剧情事件", picker,
      field("名称(日志里用)", text(item.name, value => { if (value) mutate(() => { item.name = value; }); })),
      field("史书与屏幕上的一句", text(item.text, value => mutate(() => { item.text = value; }), { placeholder: "简体|English|繁體；只写对得上的城名、国名" })),
      el("div", { class: "row" },
        field("开局后第几年起", number(item.after || 0, value => { if (value >= 0) mutate(() => { item.after = Math.round(value); }); }, { class: "w80" })),
        check("满足就再触发(可重复)", item.repeat, value => mutate(() => setKey(item, "repeat", value)))),
      el("h4", {}, "条件(全部满足)", el("span", { class: "acts" }, button("+ 条件", () => mutate(() => item.when.push({ kind: "holds_city" }))))),
      ...item.when.map((step, index) => stepBox(item, item.when, index, false)),
      el("h4", {}, "结果(按顺序做)", el("span", { class: "acts" }, button("+ 结果", () => mutate(() => item.then.push({ kind: "relation", relation: "war", goal: D.WAR_GOALS[0] }))))),
      ...item.then.map((step, index) => stepBox(item, item.then, index, true)),
      hint("结果做不了的开局跳过。只出文字、没有结果的史事要加一条「开局未满几年」卡住年份。"),
      el("div", { class: "row" },
        button("上移", () => { if (s.event > 0) commit(() => { D.mutate(() => era.events.splice(s.event - 1, 0, era.events.splice(s.event, 1)[0])); s.event--; }); }),
        button("下移", () => { if (s.event < era.events.length - 1) commit(() => { D.mutate(() => era.events.splice(s.event + 1, 0, era.events.splice(s.event, 1)[0])); s.event++; }); }),
        button("删事件", () => commit(() => { D.mutate(() => era.events.splice(s.event, 1)); s.event = -1; }), "small danger")));
  }

  // ---------- 笔刷 ----------
  function brushRow() {
    const cells = radius => { let count = 0; for (let dy = -radius; dy <= radius; dy++) for (let dx = -radius; dx <= radius; dx++) if (dx * dx + dy * dy <= radius * radius + radius - 1 || dx === 0 && dy === 0) count++; return count; };
    return el("div", { class: "row" },
      field("笔刷", select(UI.RADII.map((radius, index) => [String(index), radius === 0 ? "1 格" : `半径 ${radius}(约 ${cells(radius)} 格)`]), String(U.brush), value => { U.brush = Number(value); UI.refresh(); })),
      field("形状", select(UI.SHAPES, U.shape, value => { U.shape = value; UI.refresh(); })));
  }

  function landCard() {
    const s = U.sel, key = UI.landKey();
    const target = U.landMode === "realm" ? D.nameOf(D.polity(s.realm)) : U.landMode === "region" ? D.regionTitle(D.region(s.region))
      : U.landMode === "city" ? D.cityName(D.city(s.city)) : "永久中立区";
    const groups = new Map();
    for (const value of D.S.land.values()) groups.set(value, (groups.get(value) || 0) + 1);
    const name = value => value === "n" ? "永久中立" : value.startsWith("p:") ? D.nameOf(D.polity(value.slice(2))) || value
      : value.startsWith("r:") ? D.regionTitle(D.region(value.slice(2))) || value : value.startsWith("c:") ? cityLabel(value.slice(2)) : value;
    return card("刷地块",
      field("刷给", select([["realm", "国家"], ["region", "王国法理"], ["city", "这座城"], ["neutral", "永久中立"]], U.landMode, value => {
        U.landMode = value;
        if (value === "region") U.layer = "kingdom"; else if (value === "neutral") U.layer = "wild"; else U.layer = "realm";
        UI.refresh();
      })),
      hint(key ? `现在刷给：${target}(已刷 ${D.landCount(key)} 格)` : "先 Shift+点一座城，定下要刷给谁", !key),
      brushRow(),
      hint("每格 0.2°。开局先归那国或那块法理最近的城；刷在无人区上的定这片地挂哪个法理；永久中立区盖到的无人区开局即中立。"),
      groups.size ? el("div", { class: "list-lines" }, ...[...groups].sort((a, b) => b[1] - a[1]).slice(0, 12).map(([value, count]) =>
        el("div", { class: "line" }, el("span", {}, `${name(value)}：${count} 格`)))) : null,
      el("div", { class: "row" },
        button(D.restoresBase() ? "全部回到导入时的样子" : "清掉全部刷的地块", () => { if (UI.confirmTwice("clearLand", "五秒内再点一次，刷的地块全部清掉(可撤销)")) commit(() => D.clearPaint("land")); }, "small danger", { disabled: D.S.land.size === 0 })));
  }

  function terrainCard() {
    return card("刷地形",
      el("div", { class: "row" },
        button(U.developBrush ? "笔刷：开发度(点了换回地形)" : "笔刷：地形(点了换刷开发度)", () => { U.developBrush = !U.developBrush; if (U.developBrush) U.layer = "wild"; UI.refresh(); }),
        U.developBrush ? null : button(U.bucket ? "落笔：油桶(点了换笔刷)" : "落笔：笔刷(点了换油桶)", () => { U.bucket = !U.bucket; UI.refresh(); }, "small" + (U.bucket ? " primary" : ""))),
      U.bucket && !U.developBrush ? hint("油桶：点一下，连着的同一种地表整片换成选的地表；右键点是整片复原。一片太大就先用笔刷隔开。") : null,
      U.developBrush
        ? field("开发度(0~100)", select([0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100].map(value => [String(value), String(value)]), String(U.developValue), value => { U.developValue = Number(value); UI.refresh(); }))
        : el("div", { class: "palette" }, ...UI.TERRAINS.map(([type, name], index) => {
          const color = SM.previewColor(type);
          return el("button", { class: "small" + (index === U.terrainType ? " primary" : ""), onclick: () => { U.terrainType = index; UI.refresh(); } },
            el("span", { class: "swatch", style: `background:rgb(${color.join(",")})` }), name);
        })),
      brushRow(),
      hint(U.developBrush
        ? `开发度 ${U.developValue}：已刷 ${D.S.develop.size} 格(每格 0.2°)。雨林大泽的开发度，盖过时代开发度表；无人区图层上开发度越高越淡。`
        : `已刷 ${D.S.terrain.size} 格(每格 0.1°)，开局生成地表时将覆盖原有地形。刷出来的高原生成世界时自动围山脊、留山口。`),
      el("div", { class: "row" },
        button("清掉全部刷的地形", () => { if (UI.confirmTwice("clearTerrain", "五秒内再点一次，刷的地形全部复原(可撤销)")) commit(() => D.clearPaint("terrain")); }, "small danger", { disabled: D.S.terrain.size === 0 }),
        button("清掉全部刷的开发度", () => { if (UI.confirmTwice("clearDevelop", "五秒内再点一次，刷的开发度全部清掉(可撤销)")) commit(() => D.clearPaint("develop")); }, "small danger", { disabled: D.S.develop.size === 0 })));
  }

  // ---------- 长城、关隘、部落圈 ----------
  function wallCard() {
    const era = D.S.era, s = U.sel, wall = era.walls[s.wall];
    return card("长城",
      el("div", { class: "row" },
        select([["-1", era.walls.length ? "选长城…" : "还没有长城"], ...era.walls.map((item, index) => [String(index), `${D.first(item.name) || "?"} (${(item.pts || []).length} 点)`])],
          String(s.wall), value => { s.wall = Number(value); const point = era.walls[s.wall]?.pts?.[0]; if (point) SM.centerOn(point[0], point[1]); UI.refresh(); }, { class: "grow" }),
        button("新长城", () => commit(() => { s.wall = D.newWall(); }), "small primary")),
      wall ? field("名称(简体|英文|繁体)", text(wall.name, value => { if (value) mutate(() => { wall.name = value; }); })) : hint("点「新长城」，再在地图上一点一点连起来"),
      wall ? hint(`${(wall.pts || []).length} 个点。两点之间铺墙时沿山脊找路；长城作行政区界线，城往外铺不过长城。`) : null,
      wall ? el("div", { class: "row" },
        button("去掉最后一点", () => commit(() => D.removeWallPoint(s.wall)), "small", { disabled: !(wall.pts || []).length }),
        button("删长城", () => commit(() => { D.mutate(() => era.walls.splice(s.wall, 1)); s.wall = -1; }), "small danger")) : null);
  }

  function passCard() {
    const era = D.S.era, s = U.sel, pass = era.passes[s.pass];
    return card("关隘",
      el("div", { class: "row" },
        select([["-1", era.passes.length ? "选关隘…" : "还没有关隘"], ...era.passes.map((item, index) => [String(index), D.first(item.name) || "?"])], String(s.pass),
          value => { s.pass = Number(value); const at = era.passes[s.pass]?.at; if (at) SM.centerOn(at[0], at[1]); UI.refresh(); }, { class: "grow" })),
      pass ? field("名称(简体|英文|繁体)", text(pass.name, value => { if (value) mutate(() => { pass.name = value; }); })) : hint("点空处放一座关隘"),
      pass ? hint(`经 ${pass.at?.[0]} 纬 ${pass.at?.[1]}。开局时 5 格内有长城就吸到城墙上作关门。关隘不是城，事件里不能当城用。`) : null,
      pass ? el("div", { class: "row" }, button("删关隘", () => commit(() => { D.mutate(() => era.passes.splice(s.pass, 1)); s.pass = -1; }), "small danger")) : null);
  }

  function tribeCard() {
    const era = D.S.era, s = U.sel, list = era.tribes || [], item = list[s.tribe];
    if (!item) return card("原始部落圈", hint(`共 ${list.length} 个圈。从空处拖出一个圈，或点一个圈选中`),
      era.exact ? hint("照原图分地：部落开局只占城心一块地") : null);
    return card("原始部落圈",
      el("div", { class: "row" },
        field("文化", select(cultureOptions(), item.culture || "", value => mutate(() => { item.culture = value; }))),
        field("种族", text(item.species, value => mutate(() => setKey(item, "species", value)), { list: "dlSpecies", placeholder: "随文化" }))),
      el("div", { class: "row" },
        field("几个部落", select([["0", "按圈大小"], ...Array.from({ length: 12 }, (_, i) => [String(i + 1), String(i + 1)])], String(item.count || 0), value => mutate(() => { item.count = Number(value); }))),
        field("半径(度)", number(item.radius, value => { if (value >= 0.3) mutate(() => { item.radius = D.round(value, 1); }); }, { step: "0.1" }))),
      hint(`圈心 ${item.at?.[0]}, ${item.at?.[1]} · 共 ${list.length} 个圈。开局在圈里立这种文化的原始部落。`),
      el("div", { class: "row" }, button("删掉这个圈", () => commit(() => { D.mutate(() => { era.tribes.splice(s.tribe, 1); if (!era.tribes.length) delete era.tribes; }); s.tribe = -1; }), "small danger")));
  }

  function wildCard() {
    const pv = SM.M.preview;
    if (!pv) return card("无人区(粗略示意)",
      hint("开局预演没算出来" + (SM.M.previewError ? "(" + SM.M.previewError + ")" : "") + "：这里只按地形粗略标出荒漠冻土、山地、雨林大泽，灰斜线是刷的永久中立区。", true));
    const counts = new Map();
    for (const piece of pv.pieces) {
      const entry = counts.get(SM.kindLabel(piece.kind)) || [0, 0];
      entry[0]++; entry[1] += piece.zones;
      counts.set(SM.kindLabel(piece.kind), entry);
    }
    const era = D.S.era;
    return card("无人区(预测)",
      hint(`按 ${pv.size[0] * 64}×${pv.size[1] * 64} 格预测：一块地约 ${pv.zoneDegrees.toFixed(2)}°`),
      el("div", { class: "list-lines" }, ...[...counts].map(([kind, entry]) =>
        el("div", { class: "line" }, el("span", {}, `${kind} ${entry[0]} 片 ${entry[1]} 块`)))),
      hint(era.map ? "导入地图：没有时代开发度表，只按刷的" : era.base ? `雨林与大泽的开发度按「${era.base}」时代表设定，刷过的以刷的为准` : "不是从预设复制的剧本：雨林大泽开发度只按刷的"),
      era.exact ? hint("按原图分地：刷给城市的地块归该城，城市不向外扩展；无人认领的土地照常判定为无人区") : null,
      hint("此为预测：开局时按同样的门槛划分，城心地块留给城市。村镇在这里无法预见，结果可能差几块。国界、法理图层上无人区按归属上色并打斜线，灰斜线是中立，淡黄斜线是开局补的小国。"));
  }

  // ---------- 整栏 ----------
  function render() {
    const root = side();
    if (!root || !D.S.era) return;
    // 记住焦点在第几个输入框，重搭后放回去
    const inputs = () => [...root.querySelectorAll("input,select,textarea")];
    const focused = inputs().indexOf(document.activeElement), scroll = root.scrollTop;
    const cards = [dataLists()];
    switch (U.tool) {
      case "view": cards.push(scenarioCard(), cityCard()); break;
      case "city": cards.push(placeCard(), cityCard()); break;
      case "realm": cards.push(modeCard(), realmCard(), cityCard()); break;
      case "land": cards.push(landCard()); break;
      case "division": cards.push(modeCard(), divisionCard()); break;
      case "kingdom": cards.push(modeCard(), regionCard(), titlesCard()); break;
      case "empire": cards.push(modeCard(), coreCard()); break;
      case "relation": cards.push(relationCard()); break;
      case "event": cards.push(eventCard()); break;
      case "terrain": cards.push(terrainCard()); break;
      case "wall": cards.push(wallCard()); break;
      case "pass": cards.push(passCard()); break;
      case "tribe": cards.push(tribeCard()); break;
    }
    if (U.layer === "wild") cards.push(wildCard());
    // 折叠的那几节开着的接着开着
    const opened = new Set([...root.querySelectorAll("details[open] > summary")].map(node => node.textContent.replace(/\(\d+\)/, "")));
    const closed = new Set([...root.querySelectorAll("details:not([open]) > summary")].map(node => node.textContent.replace(/\(\d+\)/, "")));
    root.replaceChildren(...cards.filter(Boolean));
    for (const node of root.querySelectorAll("details")) {
      const title = node.querySelector("summary").textContent.replace(/\(\d+\)/, "");
      if (opened.has(title)) node.open = true; else if (closed.has(title)) node.open = false;
    }
    root.scrollTop = scroll;
    if (focused >= 0) inputs()[focused]?.focus({ preventScroll: true });
  }

  window.SP = { render };
  UI.init();
})();
