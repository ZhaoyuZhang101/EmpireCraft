// 剧本编辑器页面：读写文件、剧本列表、工具与图层、地图上的鼠标操作。右栏各卡见 scenario-panels.js，数据见 scenario-data.js，
// 画图见 scenario-map.js。操作照游戏里的剧本编辑器(ScenarioEditorScreen)：
//   左键做事、右键去掉、Shift+点取目标；右键拖、中键拖、空格+左键拖平移(刷地块、刷地形的右键是擦)；滚轮以光标为中心缩放；
//   Ctrl+Z 撤销、Ctrl+S 保存；国家、王国法理、帝国法理、行政区四个工具默认只选中，右栏选了「划入」才改归属。
(function () {
  const D = window.SD, SM = window.SM;
  const $ = id => document.getElementById(id);

  // ---------- 读写：桌面版走 preload 的 IPC，浏览器版走 scenario-server.js 的 HTTP ----------
  const desk = window.regimeEditorDesktop?.scenario || null;
  async function http(method, path, body) {
    const response = await fetch(path, { method, body });
    const type = response.headers.get("content-type") || "";
    if (!response.ok) {
      const detail = type.includes("json") ? (await response.json()).error : await response.text();
      throw new Error(detail || `请求失败 ${response.status}`);
    }
    return type.includes("json") ? response.json() : new Uint8Array(await response.arrayBuffer());
  }
  const q = text => encodeURIComponent(text);
  const IO = desk ? {
    info: () => desk.info(), list: () => desk.list(), read: folder => desk.read(folder), preset: id => desk.preset(id),
    reserve: base => desk.reserve(base), write: (folder, text) => desk.write(folder, text),
    preview: (folder, bytes) => desk.preview(folder, bytes), world: async name => new Uint8Array(await desk.world(name)),
    cultures: () => desk.cultures(), naming: () => desk.naming(), files: folder => desk.files(folder),
    file: (folder, name) => desk.file(folder, name), putFile: (folder, name, bytes) => desk.putFile(folder, name, bytes),
    remove: folder => desk.remove(folder),
    chooseDir: () => desk.chooseDir(), openFolder: folder => desk.openFolder(folder)
  } : {
    // 相对路径：网页挂在哪个前缀下(本地小服务的根、独立服务器的 /editor/)，接口就在那个前缀下找
    info: () => http("GET", "api/info"), list: () => http("GET", "api/scenarios"),
    read: folder => http("GET", "api/scenario?folder=" + q(folder)), preset: id => http("GET", "api/preset?id=" + q(id)),
    reserve: base => http("POST", "api/reserve?base=" + q(base)),
    write: (folder, text) => http("PUT", "api/scenario?folder=" + q(folder), text),
    preview: (folder, bytes) => http("PUT", "api/preview?folder=" + q(folder), bytes),
    world: name => http("GET", "api/world?file=" + q(name)), cultures: () => http("GET", "api/cultures"),
    naming: () => http("GET", "api/naming"),
    files: folder => http("GET", "api/files?folder=" + q(folder)),
    file: (folder, name) => http("GET", `api/file?folder=${q(folder)}&name=${q(name)}`),
    putFile: (folder, name, bytes) => http("PUT", `api/file?folder=${q(folder)}&name=${q(name)}`, bytes),
    remove: folder => http("DELETE", "api/scenario?folder=" + q(folder)),
    chooseDir: null, openFolder: null
  };

  // ---------- 用词 ----------
  const CULTURE_NAMES = {
    Huaxia: "华夏", China: "现代中国", Youmu: "游牧", Japan: "日本", Roma: "罗马", Frankish: "法兰克", Germanic: "日耳曼", Viking: "维京",
    Slavonic: "斯拉夫", Arab: "阿拉伯", Persepolis: "波斯", India: "天竺", Egypt: "埃及", Manzhou: "女真", Xizang: "青藏",
    Swahili: "斯瓦希里", Bantu: "班图", Zulu: "祖鲁", Aztec: "阿兹特克"
  };
  const cultureLabel = key => key ? (CULTURE_NAMES[key] ? `${CULTURE_NAMES[key]} (${key})` : key) : "";
  const TOOLS = [
    ["版图", [["view", "查看"], ["city", "城市"], ["realm", "国家"], ["land", "刷地块"], ["division", "行政区"]]],
    ["法理", [["kingdom", "王国法理"], ["empire", "帝国法理"]]],
    ["外交", [["relation", "关系"], ["event", "事件"]]],
    ["地理", [["terrain", "刷地形"], ["wall", "长城"], ["pass", "关隘"], ["tribe", "部落"]]]
  ];
  const TOOL_ICONS = { view: "iconMainInfo", city: "iconCity", realm: "iconKingdom", kingdom: "iconTitleLayer", relation: "iconWar", event: "iconHistory", terrain: "iconShovelPlus", land: "iconColorCustomization" };
  const LAYERS = [["terrain", "地形"], ["realm", "国界"], ["kingdom", "王国法理"], ["empire", "帝国法理"], ["tianxia", "天下"], ["division", "行政区"], ["wild", "无人区"]];
  const HINTS = {
    view: "左键拖动平移，滚轮缩放 · 点城：看这座城和它的国家",
    city: "点城：选中 · 点陆地：建城 · 拖城：移动 · 右键点城两次：删除",
    realm: "点城：选中该城及其国家 · 要划城，先在右栏点「把城划给它」 · 独立的城可点「以此城立国」 · 右键点城：让它独立",
    land: "左键拖动：刷地块 · 右键拖动：擦除 · Shift+点城：选取刷给谁 · 空格或中键拖动：平移",
    division: "点城：选中其国家和行政区 · 要划城，先在右栏点「把城划进来」 · 开着「点图加治所」时点空处加治所、右键去掉",
    kingdom: "点城：选中其王国法理 · 要改归属，先在右栏点「把城划进来」或「设为兼属」",
    empire: "点城：选中其所在的帝国法理，没有时可点右栏「以王国法理另立」 · 要并入，先在右栏点「把王国法理收进来」 · 右键点城：拿出来",
    relation: "点一国的城作为起始，再点另一国的城添加或去掉关系 · 右键：重选起始 · 也可在右栏直接选两国",
    event: "在右栏选择或新建事件 · 要填城、国、法理的项点旁边的「点图」再点地图上的城",
    terrain: "在右栏选地表与笔刷 · 左键拖动：绘制 · 右键拖动：复原 · 空格或中键拖动：平移",
    wall: "点击：添加一个点，连成长城 · 右键：去掉最后一点 · 在右栏切换或新建长城",
    pass: "点空处：放置关隘 · 点关隘：选中改名 · 右键点关隘：删除",
    tribe: "从空处左键拖出一个圈：开局时在圈内建立原始部落 · 点圈：选中 · 右键点圈：删除"
  };
  // 笔刷(同 ScenarioBrush)：半径 0 就是一格
  const RADII = [0, 1, 2, 3, 5, 8, 12, 20, 40];
  const SHAPES = [["circle", "圆形"], ["square", "方形"], ["diamond", "菱形"], ["rough", "毛边"]];
  // 刷地形的十九种地表(同 ScenarioEditorScreen.TerrainPalette)
  const TERRAINS = [
    ["deep_ocean", "深海"], ["close_ocean", "近海"], ["shallow_waters", "浅滩"], ["sand", "沙滩"],
    ["soil_low:grass_low", "平原"], ["soil_high:grass_high", "高地"], ["soil_low:savanna_low", "稀树草原"], ["soil_low:jungle_low", "雨林"],
    ["soil_low:desert_low", "荒漠"], ["soil_low:permafrost_low", "冻原"], ["soil_low:swamp_low", "沼泽"], ["soil_low:birch_low", "白桦林"],
    ["hills", "丘陵"], ["mountains", "山地"], ["hills:snow_hills", "雪丘"], ["mountains:snow_block", "雪山"],
    ["alpine_plateau", "高寒高原"], ["loess_plateau", "黄土高原"], ["steppe_plateau", "草原高原"]
  ];

  // ---------- 状态 ----------
  const U = {
    tool: "city", layer: "realm", mode: "select",
    sel: { city: null, realm: null, region: null, core: null, wall: -1, pass: -1, tribe: -1, event: -1, division: -1, title: -1 },
    landMode: "realm", brush: 4, shape: "circle", terrainType: 4, developBrush: false, developValue: 30, bucket: false,
    seatMode: false, relationKind: "war", relationGoal: D.WAR_GOALS[0], relationFrom: null, newCityCulture: "",
    pick: null, space: false, info: null, armed: null
  };
  SM.M.selection = U.sel;
  let sayTimer = null;

  function say(text, isError = false) {
    if (!text) return;
    const el = $("mapSay");
    el.textContent = text;
    el.style.color = isError ? "#e06d5d" : "";
    el.classList.add("show");
    clearTimeout(sayTimer);
    sayTimer = setTimeout(() => el.classList.remove("show"), 5000);
  }

  // 五秒内再点一次才做的事(删城、清空)：头一次返回 false 并提示
  function confirmTwice(key, prompt) {
    const now = Date.now();
    if (U.armed && U.armed.key === key && now - U.armed.at < 5000) { U.armed = null; return true; }
    U.armed = { key, at: now };
    say(prompt);
    return false;
  }

  // ---------- 剧本列表 ----------
  const yearText = year => year < 0 ? `前${-year}年` : `${year}年`;

  async function loadList() {
    const status = $("startStatus");
    try {
      const list = await IO.list();
      U.info = await IO.info();
      $("dirNote").textContent = "自定剧本文件夹：" + list.dir + (list.exists ? "" : "(还没有，保存时新建)");
      const rootLabel = $("modRootLabel");
      if (rootLabel && !api) rootLabel.textContent = "浏览器模式(剧本编辑器) · 模组目录：" + U.info.modRoot;
      D.S.presetIds = list.presets.map(item => item.id);
      const custom = $("customList"), presets = $("presetList");
      custom.innerHTML = "";
      presets.innerHTML = "";
      if (list.custom.length === 0) custom.append(el("p", { class: "hint" }, "还没有自定剧本：点左边「新建空白剧本」，或把右边的预设剧本「存为自定剧本」再改。"));
      for (const item of list.custom) {
        const tags = item.error ? [el("span", { class: "badge", style: "color:var(--danger)" }, "读不出来：" + item.error)] : [
          el("span", { class: "badge" }, yearText(item.year)), el("span", { class: "badge" }, item.polities + " 国"),
          item.cities >= 0 ? el("span", { class: "badge" }, "自带城表 " + item.cities + " 城") : null,
          item.imported ? el("span", { class: "badge" }, "导入地图") : null, item.author ? el("span", { class: "badge" }, "作者 " + item.author) : null
        ];
        custom.append(el("div", { class: "card scn" },
          el("div", { class: "head" }, el("span", { class: "name" }, item.zh), ...tags,
            el("span", { class: "acts" },
              el("button", { class: "small danger", onclick: () => removeCustom(item.folder, item.zh) }, "删除"),
              el("button", { class: "small", disabled: !!item.error, onclick: () => window.SS.open(item.folder, item.zh) }, "发到服务器"),
              el("button", { class: "primary small", disabled: !!item.error, onclick: () => openCustom(item.folder) }, "打开"))),
          el("div", { class: "hint" }, "文件夹：" + item.folder + (item.base ? " · 底稿：" + item.base : "")),
          item.desc ? el("div", { class: "desc" }, item.desc) : null));
      }
      for (const item of list.presets) {
        presets.append(el("div", { class: "card scn" },
          el("div", { class: "head" }, el("span", { class: "name" }, item.zh), el("span", { class: "badge" }, yearText(item.year)),
            el("span", { class: "badge" }, item.polities + " 国"),
            el("span", { class: "acts" }, el("button", { class: "small", onclick: () => copyPreset(item.id) }, "存为自定剧本"))),
          item.desc ? el("div", { class: "desc" }, item.desc) : null));
      }
      status.textContent = "";
      $("openDirBtn").hidden = !IO.openFolder;
      $("chooseDirBtn").hidden = !IO.chooseDir;
      $("folderBtn").hidden = !IO.openFolder;
    } catch (error) {
      status.className = "hint warn";
      status.textContent = location.protocol === "file:" && !desk
        ? "直接双击网页读不了文件：请用「启动桌面版.bat」，或「启动剧本编辑器(浏览器).bat」后在浏览器里打开它给的地址。"
        : "读不出剧本列表：" + error.message;
    }
  }

  async function ensureWorld() {
    if (D.S.builtin) return;
    const text = bytes => new TextDecoder().decode(bytes);
    const [cities, earth, rivers, wild, cultures, naming, development] = await Promise.all([
      IO.world("cities.json"), IO.world("earth.bytes"), IO.world("rivers.bytes").catch(() => null),
      IO.world("wilderness.json").catch(() => null), IO.cultures().catch(() => []), IO.naming().catch(() => ({})),
      IO.world("development.json").catch(() => null)
    ]);
    // 时代开发度表(雨林大泽当年开出来多少)：开局预演用
    try { D.S.development = development ? JSON.parse(text(development).replace(/^\uFEFF/, "")) : null; } catch { D.S.development = null; }
    D.S.naming = naming;
    // 行政辖境表(三份 administrative_boundaries*.json 的 boundaries 合在一起)：预演铺地前先按历史行政范围分地
    const tables = await Promise.all(["administrative_boundaries.json", "administrative_boundaries_ming.json", "administrative_boundaries_modern.json"]
      .map(name => IO.world(name).then(bytes => JSON.parse(text(bytes).replace(/^\uFEFF/, "")).boundaries || []).catch(() => [])));
    D.S.boundaries = tables.flat();
    const data = JSON.parse(text(cities).replace(/^﻿/, ""));
    D.S.builtin = { regions: data.regions || [], cities: data.cities || [], cores: data.cores || [], tianxia: data.tianxia || {} };
    D.S.cultureKeys = cultures;
    await SM.loadEarth(earth);
    if (rivers) SM.loadRivers(rivers);
    if (wild) SM.setUnits(JSON.parse(text(wild).replace(/^﻿/, "")).units);
  }

  async function busy(label, work) {
    $("startStatus").className = "hint";
    $("startStatus").textContent = label;
    try { await work(); $("startStatus").textContent = ""; }
    catch (error) { $("startStatus").className = "hint warn"; $("startStatus").textContent = label + "失败：" + error.message; toast(error.message, true); }
  }

  const openCustom = folder => busy("正在打开…", async () => {
    await ensureWorld();
    const file = await IO.read(folder);
    D.open(file.text, folder);
    // 导入地图的剧本：先把剧本文件夹里的 map.png 读进来
    U.mapFolder = folder;
    U.mapImage = D.S.era.map ? await loadMapImage(folder) : undefined;
    U.gridKey = null;
    enterEditor();
  });

  // 删除自定剧本：整个剧本文件夹，删完重读列表
  async function removeCustom(folder, name) {
    if (!confirm(`删除自定剧本「${name}」？\n整个剧本文件夹 ${folder}(连同预览图、导入的地图)都删掉，不能恢复。`)) return;
    try { await IO.remove(folder); toast("已删除 " + name); }
    catch (error) { toast("删除失败：" + error.message, true); }
    loadList();
  }

  // ---------- 导入地图(剧本文件夹里的 map.png，见 scenario-map.js 的 useImport) ----------
  let imageStamp = 0;

  async function decodeImage(bytes) {
    const bitmap = await createImageBitmap(new Blob([bytes]));
    const canvas = document.createElement("canvas");
    canvas.width = bitmap.width; canvas.height = bitmap.height;
    const context = canvas.getContext("2d", { willReadFrequently: true });
    context.drawImage(bitmap, 0, 0);
    const image = context.getImageData(0, 0, bitmap.width, bitmap.height);
    return { width: image.width, height: image.height, data: image.data, stamp: ++imageStamp, canvas };
  }

  const fromBase64 = text => { const binary = atob(text), bytes = new Uint8Array(binary.length); for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i); return bytes; };

  // 读不出、还没放图都返回 null(编辑器里显示一片海)
  async function loadMapImage(folder) {
    try {
      const file = await IO.file(folder, "map.png");
      return file.exists ? await decodeImage(fromBase64(file.base64)) : null;
    } catch (error) {
      toast("map.png 读不出来：" + error.message, true);
      return null;
    }
  }

  // 重读剧本文件夹里的 map.png(换了图、撤销换回导入地图以后)
  async function reloadMapImage() {
    const folder = D.S.folder;
    const image = await loadMapImage(folder);
    if (D.S.folder !== folder) return;
    U.mapImage = image;
    U.mapFolder = folder;
    U.gridKey = null;
    refresh();
    say(image ? `读入地图 ${image.width}×${image.height} 像素` : "剧本文件夹里还没有 map.png：先选一张图");
  }

  // 选一张图存成剧本文件夹里的 map.png(不是 PNG 的转成 PNG)，立刻写盘
  function pickMapImage() {
    const input = el("input", { type: "file", accept: "image/png,image/*" });
    input.addEventListener("change", async () => {
      const file = input.files?.[0];
      if (!file) return;
      try {
        const image = await decodeImage(new Uint8Array(await file.arrayBuffer()));
        await IO.putFile(D.S.folder, "map.png", fromBase64(image.canvas.toDataURL("image/png").split(",")[1]));
        await reloadMapImage();
      } catch (error) { say("这张图读不出来：" + error.message, true); }
    });
    input.click();
  }

  // 地球 / 导入地图互换(清空城与国家，可撤销)；换成导入时把颜色对照表写进剧本文件夹
  async function setImport(on) {
    D.setImport(on);
    fixSelection();
    if (on) {
      try { await IO.putFile(D.S.folder, "map_colors.txt", new TextEncoder().encode(SM.legendText())); }
      catch (error) { toast("颜色对照表没写成：" + error.message, true); }
      await reloadMapImage();
      say("已换成导入地图。选一张图作 map.png(或把 map.png 放进剧本文件夹再点「读入地图」)");
    } else { refresh(); say("换回地球地图"); }
  }

  // 底图跟上剧本：地球的网格，或导入地图按现在的读法、海平面读出来的网格
  function syncGrid() {
    const era = D.S.era;
    const key = era.map ? `import|${era.map.mode || "color"}|${era.map.sea ?? 0.4}|${U.mapImage ? U.mapImage.stamp : "none"}` : "earth";
    if (key === U.gridKey) return;
    U.gridKey = key;
    if (era.map) {
      // 撤销换回了导入地图而图还没读：先当一片海，读完再画
      if (U.mapImage === undefined || U.mapFolder !== D.S.folder) { U.mapImage = null; U.mapFolder = D.S.folder; reloadMapImage(); }
      U.importInfo = SM.useImport(U.mapImage, era.map.mode || "color", era.map.sea ?? 0.4);
    } else SM.useEarth();
    requestAnimationFrame(() => SM.fit());
  }

  const copyPreset = id => busy("正在存为自定剧本…", async () => {
    await ensureWorld();
    const preset = await IO.preset(id);
    const reserved = await IO.reserve(id);
    const era = JSON.parse(D.copyPresetText(preset.text, id, reserved.folder));
    if ($("newFrame").value) era.frame = $("newFrame").value;
    await IO.write(reserved.folder, D.stringify(era));
    await openCustom(reserved.folder);
  });

  const newBlank = () => busy("正在新建…", async () => {
    await ensureWorld();
    const reserved = await IO.reserve("blank");
    await IO.write(reserved.folder, D.blankText(reserved.folder, $("newFrame").value || null));
    await openCustom(reserved.folder);
  });

  // ---------- 进出编辑 ----------
  function enterEditor() {
    $("start").hidden = true;
    $("editor").hidden = false;
    Object.assign(U.sel, { city: null, realm: null, region: null, core: null, wall: -1, pass: -1, tribe: -1, event: -1, division: -1, title: -1 });
    Object.assign(U, { tool: "city", layer: "realm", mode: "select", relationFrom: null, pick: null, seatMode: false, developBrush: false });
    SM.M.layer = U.layer;
    buildBars();
    SM.resize();
    refresh();
    requestAnimationFrame(() => SM.fit());
  }

  function leaveEditor() {
    if (D.S.dirty && !confirm("有没保存的改动，回到剧本列表就丢了。确定不保存？")) return;
    D.close();
    $("editor").hidden = true;
    $("start").hidden = false;
    loadList();
  }

  async function save() {
    if (!D.S.era) return;
    try {
      await IO.write(D.S.folder, D.serialize());
      D.saved();
      try {
        const thumb = await SM.thumbnail();
        if (thumb) await IO.preview(D.S.folder, thumb);
      } catch (error) {
        toast("剧本已保存，缩略图没写成：" + error.message, true);
      }
      say("已保存");
      refresh();
    } catch (error) {
      say("保存失败：" + error.message, true);
      toast("保存失败：" + error.message, true);
    }
  }

  function undo() {
    if (D.undo()) { fixSelection(); say("撤销了一步"); refresh(); }
    else say("没有能撤销的了");
  }

  // 撤销、删东西以后，选中的要是没了就放掉
  function fixSelection() {
    const s = U.sel, era = D.S.era;
    if (!D.city(s.city)) s.city = null;
    if (!D.polity(s.realm)) s.realm = null;
    if (!D.region(s.region)) s.region = null;
    if (!D.core(s.core)) s.core = null;
    if (!(era.walls[s.wall])) s.wall = -1;
    if (!(era.passes[s.pass])) s.pass = -1;
    if (!(era.tribes || [])[s.tribe]) s.tribe = -1;
    if (!(era.events[s.event])) s.event = -1;
    if (!(era.titles || [])[s.title]) s.title = -1;
    if (!D.division(D.polity(s.realm), s.division)) s.division = -1;
    if (U.relationFrom && !D.polity(U.relationFrom)) U.relationFrom = null;
  }

  // ---------- 工具栏 ----------
  function buildBars() {
    const tools = $("toolBar"), layers = $("layerBar");
    tools.innerHTML = "";
    layers.innerHTML = "";
    for (const [group, items] of TOOLS) {
      const box = el("div", { class: "group" }, el("span", {}, group));
      for (const [id, name] of items)
        box.append(el("button", { "data-tool": id, onclick: () => setTool(id) },
          TOOL_ICONS[id] ? el("img", { class: "ico sm", src: `assets/icons/${TOOL_ICONS[id]}.png`, alt: "" }) : null, name));
      tools.append(box);
    }
    const box = el("div", { class: "group" }, el("span", {}, "图层"));
    for (const [id, name] of LAYERS) box.append(el("button", { "data-layer": id, onclick: () => setLayer(id) }, name));
    layers.append(box);
    layers.append(el("label", { class: "check" }, el("input", {
      type: "checkbox", checked: SM.M.showInactive, onchange: event => { SM.M.showInactive = event.target.checked; SM.redraw(); }
    }), "显示开局年还没有的城"));
    // 预演按哪一档大小(剧本定死了大小的照剧本)；地块画法
    const sized = Array.isArray(D.S.era.size) && D.S.era.size[0] > 0;
    const sizeBox = el("div", { class: "group" }, el("span", {}, "预演按"));
    ["小图", "中图", "大图"].forEach((name, index) => sizeBox.append(el("button", {
      "data-size": index, disabled: sized, title: sized ? "剧本定死了地图大小，照剧本的预演" : "开局时选哪一档大小，就按哪一档预演",
      onclick: () => { SM.setSizeIndex(index); refresh(); }
    }, name)));
    layers.append(sizeBox);
    layers.append(el("button", { id: "smoothBtn", onclick: () => { SM.setSmooth(!SM.M.smooth); refresh(); } }, "地块：方形"));
    layers.append(el("span", { class: "hint", id: "previewNote" }));
  }

  // 换工具时图层跟着换到看得见这件事的那一层
  const TOOL_LAYER = { kingdom: "kingdom", empire: "empire", division: "division", realm: "realm", city: "realm", relation: "realm", terrain: "terrain" };

  function setTool(tool) {
    U.tool = tool;
    U.mode = "select";
    U.pick = null;
    U.relationFrom = null;
    if (TOOL_LAYER[tool]) U.layer = TOOL_LAYER[tool];
    if (tool === "land" && !["realm", "kingdom", "wild"].includes(U.layer)) U.layer = "realm";
    refresh();
  }

  function setLayer(layer) {
    U.layer = layer;
    refresh();
  }

  // 图层上选中的那一块(颜色深一些)
  function highlightKey() {
    const s = U.sel;
    switch (U.layer) {
      case "realm": return s.realm ? "p:" + s.realm : s.city ? "i:" + s.city : null;
      case "kingdom": return s.region ? "r:" + s.region : null;
      case "empire": return s.core ? "k:" + s.core : null;
      case "tianxia": { const core = D.core(s.core); return core ? "t:" + (core.tx || "~" + core.id) : null; }
      case "division": return s.realm ? (s.division >= 0 ? `d:${s.realm}:${s.division}` : "j:" + s.realm) : null;
    }
    return null;
  }

  function refresh() {
    if (!D.S.era) return;
    const era = D.S.era;
    syncGrid();
    $("scnTitle").textContent = `${D.nameOf(era)} · ${yearText(era.year)}`;
    $("scnTitle").classList.toggle("dirty", D.S.dirty);
    $("undoBtn").disabled = !D.canUndo();
    document.querySelectorAll("#toolBar button").forEach(button => button.classList.toggle("on", button.dataset.tool === U.tool));
    document.querySelectorAll("#layerBar button[data-layer]").forEach(button => button.classList.toggle("on", button.dataset.layer === U.layer));
    const sized = Array.isArray(era.size) && era.size[0] > 0;
    document.querySelectorAll("#layerBar button[data-size]").forEach(button => {
      button.disabled = sized;
      button.classList.toggle("on", !sized && Number(button.dataset.size) === SM.M.sizeIndex);
    });
    if ($("smoothBtn")) $("smoothBtn").textContent = SM.M.smooth ? "地块：平滑" : "地块：方形";
    SM.M.layer = U.layer;
    SM.M.highlight = highlightKey();
    $("mapTip").innerHTML = U.pick ? "<b>在地图上点一座城填入</b>(Esc 取消)" : esc(HINTS[U.tool] || "");
    window.SP?.render();
    SM.redraw();
  }

  // ---------- 小工具：搭界面 ----------
  function el(tag, attrs, ...children) {
    const node = document.createElement(tag);
    for (const [key, value] of Object.entries(attrs || {})) {
      if (value === null || value === undefined || value === false) continue;
      if (key.startsWith("on")) node.addEventListener(key.slice(2), value);
      else if (key === "checked" || key === "disabled" || key === "selected" || key === "open" || key === "hidden") node[key] = !!value;
      else if (key === "value") node.value = value;
      else node.setAttribute(key, value === true ? "" : value);
    }
    for (const child of children.flat(Infinity)) if (child !== null && child !== undefined && child !== false) node.append(child);
    return node;
  }

  // ---------- 笔刷 ----------
  function noise(x, y, salt) {
    let hash = Math.imul(x, 73856093) ^ Math.imul(y, 19349663) ^ Math.imul(salt, 83492791);
    hash ^= hash >> 13;
    hash = Math.imul(hash, 1274126177);
    hash ^= hash >> 16;
    return (hash & 0xFFFF) / 65536;
  }

  function inside(shape, dx, dy, radius) {
    if (dx === 0 && dy === 0) return true;
    switch (shape) {
      case "square": return Math.abs(dx) <= radius && Math.abs(dy) <= radius;
      case "diamond": return Math.abs(dx) + Math.abs(dy) <= radius;
      case "rough": {
        const outer = radius + 0.5, solid = outer * 0.72, distance = Math.sqrt(dx * dx + dy * dy);
        if (distance <= solid) return true;
        return distance <= outer && noise(dx, dy, radius) < (outer - distance) / (outer - solid);
      }
      default: return dx * dx + dy * dy <= radius * radius + radius - 1;
    }
  }

  const covers = new Map();
  function cover(shape, radius) {
    const key = shape + radius;
    if (!covers.has(key)) {
      const list = [];
      for (let dy = -radius; dy <= radius; dy++)
        for (let dx = -radius; dx <= radius; dx++) if (inside(shape, dx, dy, radius)) list.push([dx, dy]);
      covers.set(key, list);
    }
    return covers.get(key);
  }

  // 刷地块、开发度一格 0.2°，刷地形一格 0.1°
  const brushStep = () => U.tool === "terrain" && !U.developBrush ? D.TERRAIN_STEP : D.LAND_STEP;

  // 刷地块刷给谁(键)；还没定下目标返回 null
  function landKey() {
    switch (U.landMode) {
      case "realm": return U.sel.realm ? "p:" + U.sel.realm : null;
      case "region": return U.sel.region ? "r:" + U.sel.region : null;
      case "city": return U.sel.city ? "c:" + U.sel.city : null;
      case "neutral": return "n";
    }
    return null;
  }

  let stroke = null;

  function strokeAt(lon, lat) {
    const step = brushStep(), i = Math.floor(lon / step), j = Math.floor(lat / step);
    if (stroke.lastI === i && stroke.lastJ === j) return;
    // 上一格到这一格之间逐格补上，拖快了不断线
    const fromI = stroke.lastI ?? i, fromJ = stroke.lastJ ?? j;
    const count = Math.max(Math.abs(i - fromI), Math.abs(j - fromJ), 1);
    const cells = new Set();
    for (let n = 1; n <= count; n++) {
      const ci = Math.round(fromI + (i - fromI) * n / count), cj = Math.round(fromJ + (j - fromJ) * n / count);
      for (const [dx, dy] of cover(U.shape, RADII[U.brush])) cells.add(D.pack(ci + dx, cj + dy));
    }
    stroke.lastI = i;
    stroke.lastJ = j;
    const list = [...cells];
    let changed = 0;
    if (U.tool === "land") changed = D.paintLand(list, stroke.erase ? "" : stroke.key);
    else if (U.developBrush) changed = D.paintDevelop(list, stroke.erase ? "" : String(U.developValue));
    else {
      changed = D.paintTerrain(list, stroke.erase ? "" : TERRAINS[U.terrainType][0]);
      if (changed > 0) SM.paintTexels(list);
    }
    if (changed > 0) { stroke.count += changed; SM.redraw(); }
  }

  // 油桶(同游戏的 BucketAt)：这一点连着的同一种地表整片换掉；一片超过四十万格不倒，先用笔刷隔开
  const BUCKET_LIMIT = 400000;

  function bucketAt(lon, lat, erase) {
    const type = erase ? "" : TERRAINS[U.terrainType][0];
    const found = SM.floodSame(lon, lat, BUCKET_LIMIT);
    if (!found) { say(`这一片超过 ${BUCKET_LIMIT} 格，太大了；先用笔刷隔开再倒`, true); return; }
    if (found.cells.length === 0) return;
    const rgb = SM.previewColor(type || "");
    if (!erase && found.color === ((0xFF000000 | (rgb[2] << 16) | (rgb[1] << 8) | rgb[0]) >>> 0)) { say("这一片已经是这种地表"); return; }
    const changed = D.paintTerrain(found.cells, type);
    if (changed === 0) { say(erase ? "这一片没有刷过的格子" : "这一片已经是这种地表"); return; }
    SM.paintTexels(found.cells);
    say(`整片换了 ${changed} 格`);
    refresh();
  }

  // ---------- 鼠标 ----------
  const SLOP = 5;
  let press = null, hover = { x: 0, y: 0, on: false };

  function point(event) {
    const rect = $("map").getBoundingClientRect();
    return [event.clientX - rect.left, event.clientY - rect.top];
  }

  const brushTool = () => U.tool === "land" || U.tool === "terrain";
  const writeTool = () => ["realm", "kingdom", "empire", "division"].includes(U.tool) && U.mode !== "select";

  function onDown(event) {
    if (!D.S.era) return;
    const [x, y] = point(event), [lon, lat] = SM.screenToLonLat(x, y);
    $("map").setPointerCapture(event.pointerId);
    const city = SM.cityAt(x, y);
    press = { x, y, lastX: x, lastY: y, button: event.button, shift: event.shiftKey, moved: false, kind: "pan", city };
    if (document.activeElement && document.activeElement !== document.body) document.activeElement.blur();
    if (event.button === 1 || U.space || U.pick) return;
    // 油桶：点一下倒一片，不拖(右键是整片复原)
    if (U.tool === "terrain" && !U.developBrush && U.bucket && !event.shiftKey && (event.button === 0 || event.button === 2)) {
      press.kind = "none";
      bucketAt(lon, lat, event.button === 2);
      return;
    }
    if (brushTool() && !event.shiftKey && (event.button === 0 || event.button === 2)) {
      const erase = event.button === 2, key = U.tool === "land" ? landKey() : null;
      if (U.tool === "land" && !erase && !key) { say("先 Shift+点一座城，定下要刷给谁", true); press.kind = "none"; return; }
      press.kind = "stroke";
      stroke = { erase, key, count: 0, lastI: null, lastJ: null };
      SM.M.throttle = true;
      D.beginStroke();
      strokeAt(lon, lat);
      return;
    }
    if (event.button !== 0) return;
    if (U.tool === "tribe" && D.tribesAt(lon, lat) < 0) { press.kind = "tribe"; press.lon = lon; press.lat = lat; press.radius = 0; return; }
    if (U.tool === "city" && city) { press.kind = "movecity"; return; }
    if (writeTool() && !event.shiftKey && city) {
      press.kind = "write";
      D.beginStroke();
      writeCity(city);
    }
  }

  function onMove(event) {
    const [x, y] = point(event);
    hover = { x, y, on: true };
    updateCoord(x, y);
    if (brushTool() || U.tool === "tribe") SM.redraw();
    if (!press) return;
    if (!press.moved && Math.hypot(x - press.x, y - press.y) > SLOP) press.moved = true;
    const [lon, lat] = SM.screenToLonLat(x, y);
    switch (press.kind) {
      case "pan": if (press.moved) SM.pan(x - press.lastX, y - press.lastY); break;
      case "stroke": strokeAt(lon, lat); break;
      case "tribe": press.radius = Math.hypot(lon - press.lon, lat - press.lat); SM.redraw(); break;
      case "movecity": if (press.moved) { press.lon = lon; press.lat = lat; SM.redraw(); } break;
      case "write": { const city = SM.cityAt(x, y); if (city && city !== press.last) writeCity(city); break; }
    }
    press.lastX = x;
    press.lastY = y;
  }

  function onUp(event) {
    if (!press) return;
    const [x, y] = point(event), [lon, lat] = SM.screenToLonLat(x, y), done = press;
    press = null;
    switch (done.kind) {
      case "stroke":
        D.endStroke();
        SM.M.throttle = false;
        if (stroke.count > 0) say(`${stroke.erase ? "擦了" : "刷了"} ${stroke.count} 格`);
        stroke = null;
        SM.invalidate();
        refresh();
        return;
      case "write": D.endStroke(); refresh(); return;
      case "tribe":
        if (done.radius < 0.3) { if (done.moved) say("圈太小了，拖大一点"); }
        else { U.sel.tribe = D.addTribes(done.lon, done.lat, done.radius); say("画了一个部落圈"); }
        refresh();
        return;
      case "movecity":
        if (done.moved) {
          if (!SM.isLand(lon, lat)) say("这里是水面，城立不了、挪不过去", true);
          else { D.moveCity(done.city.id, lon, lat); U.sel.city = done.city.id; }
          refresh();
          return;
        }
        break;
      case "none": return;
    }
    if (done.moved) return;
    if (done.button === 0) click(lon, lat, done.city, done.shift);
    else if (done.button === 2) rightClick(lon, lat, done.city, x, y);
    refresh();
  }

  // 划入：把城划给选中的国家、法理、帝国法理、行政区(拖过的城一路划进来)
  function writeCity(city) {
    if (press) press.last = city;
    const s = U.sel;
    let message = null;
    if (U.tool === "realm") message = s.realm ? D.assign(city.id, D.polity(s.realm)) : "先 Shift+点一座城，定下要划给谁";
    else if (U.tool === "kingdom") {
      if (!s.region) message = "先 Shift+点一座城，定下要划进哪个法理";
      else message = U.mode === "share" ? D.toggleShare(city.id, s.region) : D.assignRegion(city.id, s.region);
    } else if (U.tool === "empire") message = s.core ? D.addRegionToCore(s.core, city.r) : "先 Shift+点一座城，定下要收进哪个帝国法理";
    else if (U.tool === "division") message = s.realm ? D.assignDivision(D.polity(s.realm), s.division, city.id) : "先点一国的城选中这个国家";
    if (message) say(message);
    SM.redraw();
  }

  // 选中这座城，连同它的国家、法理
  function pickCity(city) {
    const s = U.sel, owner = D.ownerOf(city);
    s.city = city.id;
    s.realm = owner ? owner.id : null;
    s.region = city.r || null;
    s.core = D.coreOfRegion(city.r)?.id || null;
    s.division = owner ? D.divisionOf(owner, city.id) : -1;
  }

  function click(lon, lat, city, shift) {
    const s = U.sel;
    if (U.pick) { if (city) fillPick(city); return; }
    switch (U.tool) {
      case "view": case "event":
        if (city) pickCity(city);
        return;
      case "city":
        if (city) { pickCity(city); return; }
        if (!SM.inMap(lon, lat)) return;
        if (!SM.isLand(lon, lat)) { say("这里是水面，城立不了、挪不过去", true); return; }
        s.city = D.addCity(lon, lat, U.newCityCulture || null);
        pickCity(D.city(s.city));
        say("立了一座城，右栏改名" + (D.S.era.exact ? "；开局只有城心一块地，要多占地用刷地块(刷给这座城)" : ""));
        return;
      case "realm": case "kingdom": case "empire": case "division":
        if (city) {
          const keepRealm = U.tool === "division" && !shift && U.seatMode;
          if (!keepRealm) pickCity(city);
          if (U.tool === "empire" && !s.core) say("这个王国法理尚未归入帝国法理。点「以王国法理另立」可新立一个");
        } else if (U.tool === "division" && U.seatMode && s.realm && SM.inMap(lon, lat)) {
          D.addSeat(D.polity(s.realm), s.division, lon, lat);
        }
        return;
      case "land":
        if (shift && city) {
          pickCity(city);
          if (U.landMode === "realm" && !s.realm) say("这座城是独立的：先给它立国，或改成刷给「这座城」");
        }
        return;
      case "relation": {
        const owner = city ? D.ownerOf(city) : null;
        if (!city) return;
        if (!owner) { say("这座城是独立的，没有国家"); return; }
        if (!U.relationFrom || U.relationFrom === owner.id) { U.relationFrom = owner.id; s.realm = owner.id; return; }
        say(D.toggleRelation(D.polity(U.relationFrom), owner, U.relationKind, U.relationGoal));
        return;
      }
      case "wall":
        if (!SM.inMap(lon, lat)) return;
        if (s.wall < 0) s.wall = D.newWall();
        D.addWallPoint(s.wall, lon, lat);
        return;
      case "pass": {
        const [x, y] = [SM.sx(lon), SM.sy(lat)];
        const found = SM.passAt(x, y);
        if (found >= 0) s.pass = found;
        else if (SM.inMap(lon, lat)) s.pass = D.addPass(lon, lat);
        return;
      }
      case "tribe": s.tribe = D.tribesAt(lon, lat); return;
    }
  }

  function rightClick(lon, lat, city, x, y) {
    const s = U.sel;
    if (U.pick) { U.pick = null; return; }
    switch (U.tool) {
      case "city":
        if (city && confirmTwice("city:" + city.id, `五秒内再右键点一次删掉「${D.cityName(city)}」`)) { say(D.deleteCity(city.id)); fixSelection(); }
        return;
      case "realm": if (city) say(D.makeIndependent(city.id)); return;
      case "kingdom": if (city && s.region) say(D.toggleShare(city.id, s.region)); return;
      case "empire": if (city) say(D.removeRegionFromCore(city.r)); return;
      case "division":
        if (U.seatMode && s.realm) { if (D.removeSeat(D.polity(s.realm), s.division, lon, lat, 8 / SM.M.scale * D.TERRAIN_STEP + 0.15)) say("去掉了一个治所"); }
        else if (city) { const owner = D.ownerOf(city); if (owner) say(D.assignDivision(owner, -1, city.id) || "这座城不在哪个行政区里"); }
        return;
      case "relation": U.relationFrom = null; return;
      case "wall": if (s.wall >= 0) D.removeWallPoint(s.wall); return;
      case "pass": { const found = SM.passAt(x, y); if (found >= 0) { D.mutate(era => era.passes.splice(found, 1)); s.pass = -1; say("关隘已删"); } return; }
      case "tribe": {
        const found = D.tribesAt(lon, lat);
        if (found >= 0) { D.mutate(era => { era.tribes.splice(found, 1); if (!era.tribes.length) delete era.tribes; }); s.tribe = -1; say("删掉了部落圈"); }
        return;
      }
    }
  }

  // 事件里「点图」填一项：国家项取该城所属的国家，法理项取该城的王国法理
  function fillPick(city) {
    const { step, slot } = U.pick;
    let value = city.id;
    if (slot === "a" || slot === "b" || slot === "c") {
      const owner = D.ownerOf(city);
      if (!owner) { say("这座城是独立的，没有国家"); return; }
      value = owner.id;
    } else if (slot === "region") value = city.r;
    D.mutate(() => {
      if (slot === "cities") { step.cities ||= []; if (!step.cities.includes(value)) step.cities.push(value); }
      else step[slot] = value;
    });
    say("填进：" + (slot === "region" ? D.regionTitle(D.region(value)) : slot === "city" || slot === "cities" ? D.cityName(city) : D.nameOf(D.polity(value))));
    U.pick = null;
  }

  function updateCoord(x, y) {
    if (!D.S.era) return;
    const [lon, lat] = SM.screenToLonLat(x, y);
    const type = SM.typeAt(lon, lat), terrain = TERRAINS.find(item => item[0] === type);
    const city = SM.cityAt(x, y);
    let text = `经 ${lon.toFixed(2)} 纬 ${lat.toFixed(2)} · ${terrain ? terrain[1] : type}`;
    if (city) {
      const owner = D.ownerOf(city);
      text += ` · ${D.cityName(city)}(${D.active(city) ? (owner ? D.nameOf(owner) : "独立") : "开局年还没有"} · ${D.regionTitle(D.region(city.r)) || "无法理"})`;
    }
    // 这块地开局怎么算：无人区(类型、几块、开发度、挂哪个法理、归谁)，或归哪座城、补空小国
    const zone = SM.zoneInfoAt(lon, lat);
    if (zone && !city) {
      if (zone.outside) text += " · 地图范围外，开局没有";
      else if (zone.piece) {
        const piece = zone.piece, region = D.region(piece.region);
        const source = { paint: "刷的", history: "史实归属", near: "最近的城", surround: "周边的城", neutral: "永久中立，法理不管", none: "无" }[piece.source] || piece.source;
        const owner = piece.neutral ? "永久中立区：不归任何国家" : !piece.owner ? "中立：没挂上法理，周边的城也不同属一国"
          : piece.owner === "f" ? "补空小国(开局随机立)" : piece.owner.startsWith("i:") ? `归${D.cityName(D.city(piece.owner.slice(2)))}(独立)` : `归${D.nameOf(D.polity(piece.owner)) || piece.owner}`;
        text += ` · ${SM.kindLabel(zone.kind)}无人区(预测) ${piece.zones} 块${piece.develop >= 0 ? " · 开发度 " + Math.round(piece.develop) : ""}`
          + `${region ? ` · 法理：${D.regionTitle(region)}(${source})` : ""} · ${owner}`;
      } else if (zone.filler) text += " · 补空小国(开局随机立)";
      else if (zone.seat) {
        const seat = D.city(zone.seat), owner = seat ? D.ownerOf(seat) : null;
        if (seat) text += ` · 开局归${D.cityName(seat)}(${owner ? D.nameOf(owner) : "独立"})${zone.painted ? " · 刷的" : ""}`;
      }
    }
    $("coordInfo").textContent = text;
  }

  // 叠在地图上的：笔刷的范围、正在拖的部落圈、正在挪的城、关系连线、选中行政区的治所
  SM.setOverlay(ctx => {
    const era = D.S.era, s = U.sel;
    if (hover.on && brushTool() && !U.space) {
      const step = brushStep(), [lon, lat] = SM.screenToLonLat(hover.x, hover.y);
      const i = Math.floor(lon / step), j = Math.floor(lat / step), size = step / D.TERRAIN_STEP * SM.M.scale;
      ctx.fillStyle = "rgba(255,255,255,.22)";
      ctx.strokeStyle = "rgba(255,236,160,.9)";
      ctx.lineWidth = 1;
      const cells = cover(U.shape, RADII[U.brush]);
      for (const [dx, dy] of cells) ctx.fillRect(SM.sx((i + dx) * step), SM.sy((j + dy + 1) * step), size, size);
      const r = RADII[U.brush];
      ctx.strokeRect(SM.sx((i - r) * step), SM.sy((j + r + 1) * step), size * (2 * r + 1), size * (2 * r + 1));
    }
    if (press?.kind === "tribe" && press.radius > 0) {
      ctx.strokeStyle = "#ffd65a";
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.arc(SM.sx(press.lon), SM.sy(press.lat), press.radius / D.TERRAIN_STEP * SM.M.scale, 0, Math.PI * 2);
      ctx.stroke();
    }
    if (press?.kind === "movecity" && press.moved && press.lon !== undefined) {
      ctx.strokeStyle = "#ffd65a";
      ctx.lineWidth = 2;
      ctx.strokeRect(SM.sx(press.lon) - 6, SM.sy(press.lat) - 6, 12, 12);
    }
    // 选中国家的关系连线(都城到都城)
    if ((U.tool === "relation" || U.tool === "realm") && s.realm) {
      const self = D.polity(s.realm), seat = D.city(self?.cap);
      if (seat) for (const relation of era.relations) {
        if (relation.a !== s.realm && relation.b !== s.realm) continue;
        const other = D.city(D.polity(relation.a === s.realm ? relation.b : relation.a)?.cap);
        if (!other) continue;
        ctx.strokeStyle = relation.kind === "war" || relation.kind === "feud" ? "#e0503c" : "#7fd28a";
        ctx.lineWidth = 2;
        ctx.setLineDash(relation.kind === "war" ? [] : [6, 4]);
        ctx.beginPath(); ctx.moveTo(SM.sx(seat.x), SM.sy(seat.y)); ctx.lineTo(SM.sx(other.x), SM.sy(other.y)); ctx.stroke();
        ctx.setLineDash([]);
      }
    }
    if (U.tool === "division" && s.realm) {
      const target = D.polity(s.realm);
      const all = [[-1, target?.seats], ...(target?.divisions || []).map((item, index) => [index, item.seats])];
      for (const [index, seats] of all)
        for (const seat of seats || []) {
          ctx.fillStyle = index === s.division ? "#ffd65a" : "rgba(255,255,255,.6)";
          ctx.beginPath(); ctx.arc(SM.sx(seat[0]), SM.sy(seat[1]), index === s.division ? 3.5 : 2, 0, Math.PI * 2); ctx.fill();
        }
    }
  });

  // ---------- 开张 ----------
  function init() {
    mountAppBar("scenario.html");
    const canvas = $("map");
    SM.mount(canvas);
    canvas.addEventListener("pointerdown", onDown);
    canvas.addEventListener("pointermove", onMove);
    canvas.addEventListener("pointerup", onUp);
    canvas.addEventListener("pointerleave", () => { hover.on = false; SM.redraw(); });
    canvas.addEventListener("contextmenu", event => event.preventDefault());
    canvas.addEventListener("wheel", event => {
      event.preventDefault();
      const [x, y] = point(event);
      SM.zoomAt(x, y, event.deltaY < 0 ? 1.2 : 1 / 1.2);
    }, { passive: false });
    $("newBlankBtn").onclick = newBlank;
    $("reloadListBtn").onclick = loadList;
    $("openDirBtn").onclick = () => IO.openFolder("").catch(error => toast(error.message, true));
    $("chooseDirBtn").onclick = async () => { try { const result = await IO.chooseDir(); if (!result.canceled) loadList(); } catch (error) { toast(error.message, true); } };
    $("backBtn").onclick = leaveEditor;
    $("saveBtn").onclick = save;
    $("undoBtn").onclick = undo;
    $("fitBtn").onclick = () => SM.fit();
    $("folderBtn").onclick = () => IO.openFolder(D.S.folder).catch(error => toast(error.message, true));
    $("sendBtn").onclick = () => window.SS.open(D.S.folder, D.nameOf(D.S.era));
    window.addEventListener("keydown", event => {
      if (!D.S.era) return;
      const typing = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName || "");
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") { event.preventDefault(); document.activeElement?.blur(); save(); return; }
      if (typing) return;
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z") { event.preventDefault(); undo(); }
      else if (event.code === "Space") { U.space = true; event.preventDefault(); }
      else if (event.key === "Escape") { U.pick = null; U.relationFrom = null; refresh(); }
    });
    window.addEventListener("keyup", event => { if (event.code === "Space") U.space = false; });
    window.addEventListener("beforeunload", event => { if (D.S.dirty) { event.preventDefault(); event.returnValue = ""; } });
    loadList();
  }

  window.UI = {
    U, IO, el, say, refresh, save, undo, confirmTwice, pickCity, fixSelection, setTool, init,
    setImport, pickMapImage, reloadMapImage,
    cultureLabel, RADII, SHAPES, TERRAINS, yearText, landKey
  };
})();
