// 剧本编辑器(网页版)的开局预演与无人区预测：游戏里 Scripts/GeneralSystems/WorldMap/ScenarioWilderness.cs 的移植，
// 连同它用到的 ScenarioPreviewRules(Spread、MarkWall)、ScenarioBrush.ZoneValue、ScenarioCells、WorldMapFrame.Bounds/IndexFor、
// EarthTerrain.Frames/RiverTiles、WorldMapDevelopment(AreaAt、NearCity、ParseValue)、WorldMapWilderness.HistoricalOwner、
// AdministrativeBoundaryRules、ScenarioEditing.ComputeOwners。规矩、门槛常量、先后顺序都照那边，改了那边这里也要改。
// 纯函数、同步、不碰 DOM、不读文件；浏览器里挂 window.SPV，Node 里 module.exports。
//
// 小数：游戏里经纬度、门槛全是单精度(float)，这里凡是照搬的算式都逐步用 Math.fround 收成单精度，
// 取整用"四舍六入五成双"(Mathf.RoundToInt)，免得落在格子边上的城、取样点和游戏里差一格。
(function (root, factory) {
  const api = factory();
  const node = typeof module === "object" && module && module.exports;
  if (node) module.exports = api;
  // 浏览器(连同开了 Node 的 Electron 页面、Worker)里都挂上 SPV
  if (typeof window !== "undefined") window.SPV = api;
  else if (!node && root && typeof root === "object") root.SPV = api;
})(typeof self !== "undefined" ? self : this, function () {
  "use strict";

  const F = Math.fround;

  // Mathf.RoundToInt：.5 取偶数
  function roundEven(x) {
    if (Math.abs(x % 1) === 0.5) return 2 * Math.round(x / 2);
    return Math.round(x);
  }

  // ---------------------------------------------------------------- 常量(照 ScenarioWilderness 的那一组，它又照抄 WildernessSystem)

  // 预览一格的地形标记(ScenarioWilderness.CellOf)：低两位起伏，往上三位地面
  const ROCK_RELIEF = 1, HILL_RELIEF = 2, WATER_RELIEF = 3;
  const FROZEN_GROUND = 1, SAND_GROUND = 2, JUNGLE_GROUND = 3, SWAMP_GROUND = 4, GREEN_GROUND = 5, LAVA_GROUND = 6, PLATEAU_GROUND = 7;
  const WATER_CELL = WATER_RELIEF;

  // 无人区类型(WildKind 枚举，值即下标)；名字用枚举名的小写(同 WorldMapWilderness.KindKey)
  const KINDS = ["", "mountain", "desert", "tundra", "jungle", "swamp", "lava", "plateau"];
  const K_MOUNTAIN = 1, K_DESERT = 2, K_TUNDRA = 3, K_JUNGLE = 4, K_SWAMP = 5, K_LAVA = 6, K_PLATEAU = 7;

  const MIN_LAND = 16;            // 陆地不到这么多格的地块不算
  const MAX_WATER_TILES = 16;     // 64 * MaxWater(0.25)
  const MOUNTAIN_CORE = 0.5, MOUNTAIN_FLANK = 0.35, MOUNTAIN_RUGGED = 0.75;
  const LAVA_CORE = 0.3, LAVA_FLANK = 0.15, LAVA_RUGGED = 0.6;
  const BARREN_SHARE = 0.85, SWAMP_SHARE = 0.75, JUNGLE_SHARE = 0.85;
  const PLATEAU_CORE = 0.35;
  const MASSIF_COST = 60;
  const EDGE_SHARE = 0.5, MOUNTAIN_EDGE_SHARE = 0.6, LAVA_EDGE_SHARE = 0.15;
  const WET_SHARE = 0.5;
  const FILLER_REACH = 60, TERRITORY_LAND = 8;
  const SEA_GROUND = 16;
  const MIN_ZONES = 4;            // WildernessSystem.MinZones
  const DEV_FULL = 100;           // WildernessDevelopmentSystem.Full
  const RIM_SIDE = 255;
  const UNCLAIMED = -1, FILLER = -2;   // ScenarioPreviewRules.Unclaimed、Filler

  // WorldMapScenario.TerritoryBudget、TerritoryCap(按城的等级 0~3)、SeatBudget(州府治所)
  const TERRITORY_BUDGET = [9, 9, 7, 5], TERRITORY_CAP = [40, 40, 28, 16], SEAT_BUDGET = 7;
  const INT_MAX = 2147483647;

  // ScenarioCells：地块归属 0.2° 一格
  const LAND_STEP = F(0.2);
  const DEG2RAD = F(0.0174532924);
  // EarthTerrain.RiverTiles：每走 14 格留 2 格渡口，两格宽
  const FORD_EVERY = 14, FORD_WIDTH = 2, RIVER_THICKNESS = 2;

  const SOURCES = ["none", "paint", "history", "near", "surround", "neutral"];
  const S_NONE = 0, S_PAINT = 1, S_HISTORY = 2, S_NEAR = 3, S_SURROUND = 4, S_NEUTRAL = 5;

  // 四邻、八邻的次序同 C#(连片、铺地先到先得，次序不能换)
  const DX4 = [1, -1, 0, 0], DY4 = [0, 0, 1, -1];
  const DX8 = [1, -1, 0, 0, 1, 1, -1, -1], DY8 = [0, 0, 1, -1, 1, -1, 1, -1];

  // 门槛表：land(0~64) × 单精度常数，收成单精度再比(C# 里是 int * float)
  function thresholds(share) {
    const s = F(share), table = new Float32Array(65);
    for (let land = 0; land <= 64; land++) table[land] = F(land * s);
    return table;
  }
  const T_MOUNTAIN_CORE = thresholds(MOUNTAIN_CORE), T_MOUNTAIN_FLANK = thresholds(MOUNTAIN_FLANK), T_MOUNTAIN_RUGGED = thresholds(MOUNTAIN_RUGGED);
  const T_LAVA_CORE = thresholds(LAVA_CORE), T_LAVA_FLANK = thresholds(LAVA_FLANK), T_LAVA_RUGGED = thresholds(LAVA_RUGGED);
  const T_BARREN = thresholds(BARREN_SHARE), T_SWAMP = thresholds(SWAMP_SHARE), T_JUNGLE = thresholds(JUNGLE_SHARE);
  const T_PLATEAU_CORE = thresholds(PLATEAU_CORE), T_HALF = thresholds(0.5), T_RIM = thresholds(0.15);
  const T_EDGE = thresholds(EDGE_SHARE), T_MOUNTAIN_EDGE = thresholds(MOUNTAIN_EDGE_SHARE), T_LAVA_EDGE = thresholds(LAVA_EDGE_SHARE);

  // ---------------------------------------------------------------- 地图范围(EarthTerrain.Frames、WorldMapFrame)

  const FRAMES = [
    { id: "eurasia", west: -25, east: 153, top: 77.5, centerLat: 0, sizes: [[16, 7], [32, 15], [40, 18]] },
    { id: "oldworld", west: -26, east: 155, top: NaN, centerLat: 21, sizes: [[14, 9], [28, 18], [34, 22]] },
    { id: "eastasia", west: 62, east: 152, top: 62, centerLat: 0, sizes: [[14, 10], [28, 20], [32, 23]] },
    { id: "westworld", west: -26, east: 64, top: NaN, centerLat: 21, sizes: [[10, 13], [20, 25], [24, 30]] }
  ];

  // 这份剧本按哪个范围预演(ScenarioWilderness.Update)：导入地图按 imported(同 WorldMapImporter.FrameOf：西 0、东 cols*0.1、北 rows*0.1)；
  // 地球地图按 era.frame 那一档，没定、对不上的按欧亚非
  function frameOf(era, imported) {
    if (imported) {
      const step = F(0.1);
      return {
        id: "imported", west: 0, east: F(imported.cols * step), top: F(imported.rows * step), centerLat: 0,
        sizes: (imported.sizes || []).map(size => [size[0] | 0, size[1] | 0])
      };
    }
    const id = era ? era.frame : null;
    return FRAMES.find(frame => frame.id === id) || FRAMES[0];
  }

  // 剧本定死了大小(WorldMapEra.HasSize)
  function hasSize(era) {
    return !!era && Array.isArray(era.size) && era.size.length >= 2 && era.size[0] > 0 && era.size[1] > 0;
  }

  // WorldMapFrame.IndexFor：定死的大小算第几档(按面积取不超过它的最大一档，至少算中档)
  function indexFor(frame, size) {
    let best = 1;
    for (let i = 1; i < frame.sizes.length; i++)
      if (frame.sizes[i][0] * frame.sizes[i][1] <= size[0] * size[1]) best = i;
    return Math.min(best, frame.sizes.length - 1);
  }

  // 范围、大小、算第几档(ScenarioWilderness.Update 开头那一段)；范围没有大小档的返回 null
  function resolve(era, imported, sizeIndex) {
    const frame = frameOf(era, imported);
    if (!frame || !frame.sizes || frame.sizes.length === 0) return null;
    let index = Math.max(0, Math.min(frame.sizes.length - 1, (sizeIndex === undefined || sizeIndex === null ? 1 : sizeIndex) | 0));
    let size = frame.sizes[index];
    if (hasSize(era)) {
      size = [era.size[0] | 0, era.size[1] | 0];
      index = indexFor(frame, size);
    }
    return { frame, size: [size[0], size[1]], sizeIndex: index };
  }

  // 按多大预演：[东西区块, 南北区块]
  function sizeOf(era, imported, sizeIndex) {
    const resolved = resolve(era, imported, sizeIndex);
    return resolved ? resolved.size : null;
  }

  // WorldMapFrame.Bounds：一格几度、南界
  function bounds(frame, size) {
    const degrees = F(F(F(frame.east) - F(frame.west)) / F(size[0] * 64));
    const span = F(F(size[1] * 64) * degrees);
    const north = Number.isNaN(frame.top) || frame.top === undefined || frame.top === null
      ? F(F(frame.centerLat || 0) + F(span / 2)) : F(frame.top);
    return { degrees, bottom: F(north - span) };
  }

  // ---------------------------------------------------------------- 地形标记(ScenarioWilderness.CellOf)

  function cellOf(type) {
    if (!type || type.includes("ocean") || type.includes("waters")) return WATER_CELL;
    if (type.includes("lava")) return LAVA_GROUND << 2;
    // 高寒高原是高原格；黄土高原、草原高原是农地(草木平地)
    if (type === "alpine_plateau") return PLATEAU_GROUND << 2;
    if (type === "loess_plateau" || type === "steppe_plateau") return GREEN_GROUND << 2;
    const split = type.indexOf(":");
    const main = split < 0 ? type : type.substring(0, split), top = split < 0 ? type : type.substring(split + 1);
    const relief = main === "mountains" || top === "mountains" || top.includes("summit") ? ROCK_RELIEF
      : main === "hills" || top === "hills" ? HILL_RELIEF : 0;
    const ground = top.startsWith("snow") || top.startsWith("frozen") || top.includes("permafrost") ? FROZEN_GROUND
      // 沙地(sand)是沙滩，不算沙漠
      : top.includes("desert") ? SAND_GROUND
      : top.includes("jungle") ? JUNGLE_GROUND
      : top.includes("swamp") ? SWAMP_GROUND
      : (top.endsWith("_low") || top.endsWith("_high")) &&
        (top.includes("grass") || top.includes("savanna") || top.includes("birch") || top.includes("maple")) ? GREEN_GROUND
      : 0;
    return relief | ground << 2;
  }

  // ---------------------------------------------------------------- 笔刷的格子(ScenarioCells、ScenarioBrush.ZoneValue)

  function pack(i, j) { return (j + 2000) * 10000 + (i + 5000); }

  // ScenarioBrush.FirstCell：中心不小于这个经纬度的第一格
  function firstCell(at, step) {
    return Math.ceil(roundEven(F(F(F(at / step) - 0.5) * 256)) / 256);
  }

  // 一块地取哪个刷的值，按列、行先算好格子(同一列的经度、同一行的纬度都一样)：
  // lons、lats 为各列、各行取样点的经纬度(单精度)，side 一块地几度，step 一格几度
  function zoneSampler(lons, lats, side, step) {
    const half = F(side / 2), wide = side > step;
    const make = values => {
      const count = values.length, centre = new Int32Array(count), first = new Int32Array(count), last = new Int32Array(count);
      for (let k = 0; k < count; k++) {
        const at = values[k];
        centre[k] = Math.floor(F(at / step));
        first[k] = firstCell(F(at - half), step);
        last[k] = firstCell(F(at + half), step) - 1;
      }
      return { centre, first, last };
    };
    return { x: make(lons), y: make(lats), wide };
  }

  // ScenarioBrush.ZoneValue：中心那格刷了就按它；没刷就看块内别的格子，取刷得最多的(一样多取排序靠前的)。
  // usable 为 null 都算；没有返回 undefined
  function zoneValue(map, sampler, zx, zy, usable) {
    if (!map || map.size === 0) return undefined;
    const direct = map.get(pack(sampler.x.centre[zx], sampler.y.centre[zy]));
    if (direct !== undefined) return direct;
    if (!sampler.wide) return undefined;
    const i0 = sampler.x.first[zx], i1 = sampler.x.last[zx], j0 = sampler.y.first[zy], j1 = sampler.y.last[zy];
    let counts = null;
    for (let j = j0; j <= j1; j++)
      for (let i = i0; i <= i1; i++) {
        const found = map.get(pack(i, j));
        if (found === undefined || usable && !usable(found)) continue;
        if (!counts) counts = new Map();
        counts.set(found, (counts.get(found) || 0) + 1);
      }
    if (!counts) return undefined;
    let most = 0, bestName = null, value;
    for (const [key, count] of counts) {
      const name = String(key);
      // string.CompareOrdinal(name, bestName) >= 0：空的排最前
      if (count < most || count === most && (bestName === null || name >= bestName)) continue;
      most = count;
      bestName = name;
      value = key;
    }
    return value;
  }

  // ScenarioBrush.NotExact：照原图归城的 c: 不往块内别的格子取
  const notExact = key => !String(key).startsWith("c:");

  // WorldMapDevelopment.ParseValue：刷的开发度的键("0"~"100")
  function parseDevelop(key) {
    const text = key === undefined || key === null ? "" : String(key);
    if (!/^\s*[+-]?\d+\s*$/.test(text)) return NaN;
    const number = parseInt(text, 10);
    if (number > INT_MAX || number < -INT_MAX - 1) return NaN;
    return Math.max(0, Math.min(DEV_FULL, number));
  }

  // ---------------------------------------------------------------- 时代开发度表(WorldMapDevelopment)

  function prepareAreas(development) {
    const source = development && Array.isArray(development.areas) ? development.areas : [];
    const areas = [];
    for (const area of source) {
      if (!area || !area.id) continue;
      // 没写 kind 的按雨林(WorldMapDevArea.kind 的默认)；别的写法不认
      const kind = area.kind === undefined ? "jungle" : area.kind;
      if (kind !== "jungle" && kind !== "swamp") continue;
      const box = (area.box || []).filter(b => b && b.length >= 4).map(b => [F(b[0]), F(b[1]), F(b[2]), F(b[3])]);
      const poly = (area.poly || []).filter(ring => ring && ring.length >= 3 && ring.every(p => p && p.length >= 2))
        .map(ring => ring.map(p => [F(p[0]), F(p[1])]));
      let west = Infinity, south = Infinity, east = -Infinity, north = -Infinity;
      for (const b of box) {
        west = Math.min(west, b[0]); south = Math.min(south, b[1]); east = Math.max(east, b[2]); north = Math.max(north, b[3]);
      }
      for (const ring of poly)
        for (const p of ring) {
          west = Math.min(west, p[0]); south = Math.min(south, p[1]); east = Math.max(east, p[0]); north = Math.max(north, p[1]);
        }
      areas.push({
        dev: area.dev || {}, all: area.all === undefined || area.all === null ? -1 : F(area.all),
        swamp: kind === "swamp", box, poly, west, south, east, north
      });
    }
    return areas;
  }

  // 点在不在多边形里(射线法，WorldMapDevelopment.Inside，单精度)
  function insideRing(ring, x, y) {
    let inside = false;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
      const xi = ring[i][0], yi = ring[i][1], xj = ring[j][0], yj = ring[j][1];
      if (yi > y !== yj > y && x < F(F(F(F(xj - xi) * F(y - yi)) / F(yj - yi)) + xi)) inside = !inside;
    }
    return inside;
  }

  // WorldMapDevelopment.AreaAt：这个经纬度落在哪一片(先写的优先)；都不在为 -1
  function areaAt(areas, lon, lat) {
    for (let i = 0; i < areas.length; i++) {
      const area = areas[i];
      if (lon < area.west || lon > area.east || lat < area.south || lat > area.north) continue;
      for (const b of area.box)
        if (lon >= b[0] && lon <= b[2] && lat >= b[1] && lat <= b[3]) return i;
      for (const ring of area.poly)
        if (insideRing(ring, lon, lat)) return i;
    }
    return -1;
  }

  // 按哪个时代的表(WorldMapData.BaseOf)：给了 input.table 就用它(null 为不用表)；导入地图不用；写了 base 的按 base；
  // 游戏里的自定剧本(编号 custom/<文件夹>)没写 base 的按文件夹名(去掉重名加的 _2、_3)对得上预设才算——这里没有预设的名单，
  // 给了 input.presets 按它对，没给按开发度表里出现过的时代编号对；预设本身(编号不带 custom/)按自己的编号
  function tableOf(input, imported) {
    if (imported) return null;
    if (input.table !== undefined) return input.table || null;
    const era = input.era;
    if (era.base) return String(era.base);
    const id = String(era.id || "");
    if (!id.startsWith("custom/") && !input.custom) return id || null;
    let name = id.startsWith("custom/") ? id.substring(7) : id;
    const cut = name.lastIndexOf("_");
    if (cut > 0 && /^\s*[+-]?\d+\s*$/.test(name.substring(cut + 1))) name = name.substring(0, cut);
    let known = input.presets;
    if (!Array.isArray(known)) {
      known = [];
      const areas = input.development && Array.isArray(input.development.areas) ? input.development.areas : [];
      for (const area of areas) for (const key of Object.keys(area && area.dev || {})) known.push(key);
    }
    return known.includes(name) ? name : null;
  }

  // ---------------------------------------------------------------- 地理单元与史实归属(WorldMapWilderness)

  function prepareUnits(units) {
    const byId = new Map();
    for (const unit of Array.isArray(units) ? units : []) {
      if (!unit || !unit.id || byId.has(unit.id)) continue;
      const r = F(unit.r === undefined || unit.r === null ? 3 : unit.r);
      const box = (unit.box || []).filter(b => b && b.length >= 4).map(b => [F(b[0]), F(b[1]), F(b[2]), F(b[3])]);
      const pts = (unit.pts || []).filter(p => p && p.length >= 2).map(p => [F(p[0]), F(p[1])]);
      // Measure：外框(含锚点半径)、面积(经度×纬度的平方度)
      let west = Infinity, south = Infinity, east = -Infinity, north = -Infinity, area = 0;
      for (const b of box) {
        west = Math.min(west, b[0]); south = Math.min(south, b[1]); east = Math.max(east, b[2]); north = Math.max(north, b[3]);
        area = F(area + F(F(b[2] - b[0]) * F(b[3] - b[1])));
      }
      for (const p of pts) {
        const pad = F(r / Math.max(F(0.15), F(Math.cos(F(Math.min(89, F(Math.abs(p[1]) + r)) * DEG2RAD)))));
        west = Math.min(west, F(p[0] - pad)); east = Math.max(east, F(p[0] + pad));
        south = Math.min(south, F(p[1] - r)); north = Math.max(north, F(p[1] + r));
        area = F(area + F(F(F(F(Math.PI) * r) * r) / Math.max(F(0.15), F(Math.cos(F(Math.abs(p[1]) * DEG2RAD))))));
      }
      byId.set(unit.id, { id: unit.id, kinds: unit.k || unit.kinds || [], box, pts, r, r2: F(r * r), west, south, east, north, area });
    }
    return byId;
  }

  // WorldMapWilderness.Covers
  function covers(unit, lon, lat) {
    if (lon < unit.west || lon > unit.east || lat < unit.south || lat > unit.north) return false;
    for (const b of unit.box)
      if (lon >= b[0] && lon <= b[2] && lat >= b[1] && lat <= b[3]) return true;
    if (unit.pts.length === 0) return false;
    const squeeze = F(Math.cos(F(lat * DEG2RAD)));
    for (const p of unit.pts) {
      const dx = F(F(lon - p[0]) * squeeze), dy = F(lat - p[1]);
      if (F(F(dx * dx) + F(dy * dy)) <= unit.r2) return true;
    }
    return false;
  }

  // ---------------------------------------------------------------- 行政辖境的面(AdministrativeBoundaryRules，那边用双精度)

  function polygonBounds(polygons) {
    let west = Infinity, south = Infinity, east = -Infinity, north = -Infinity;
    for (const polygon of polygons || [])
      for (const ring of polygon || [])
        for (const point of ring || []) {
          if (!point || point.length < 2) continue;
          const x = F(point[0]), y = F(point[1]);
          west = Math.min(west, x); east = Math.max(east, x); south = Math.min(south, y); north = Math.max(north, y);
        }
    return west <= east && south <= north ? { west, south, east, north } : null;
  }

  function inRing(ring, x, y) {
    if (!ring || ring.length < 3) return false;
    let inside = false;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
      const a = ring[j], b = ring[i];
      if (!a || !b || a.length < 2 || b.length < 2) continue;
      const ax = F(a[0]), ay = F(a[1]), bx = F(b[0]), by = F(b[1]);
      const cross = F(x - ax) * F(by - ay) - F(y - ay) * F(bx - ax);
      if (Math.abs(cross) < 0.0000001 && x >= Math.min(ax, bx) && x <= Math.max(ax, bx) &&
          y >= Math.min(ay, by) && y <= Math.max(ay, by)) return true;
      if (ay > y !== by > y && x < F(bx - ax) * F(y - ay) / F(by - ay) + ax) inside = !inside;
    }
    return inside;
  }

  function polygonsContain(polygons, lon, lat) {
    for (const polygon of polygons || []) {
      if (!polygon || polygon.length === 0 || !inRing(polygon[0], lon, lat)) continue;
      let hole = false;
      for (let i = 1; i < polygon.length; i++)
        if (inRing(polygon[i], lon, lat)) { hole = true; break; }
      if (!hole) return true;
    }
    return false;
  }

  // ---------------------------------------------------------------- 城归哪国(ScenarioEditing.ComputeOwners)

  const cityYearFrom = city => city.from === undefined || city.from === null ? -INT_MAX - 1 : city.from;
  const cityYearTo = city => city.to === undefined || city.to === null ? INT_MAX : city.to;
  // WorldMapCity.ActiveIn
  const activeIn = (city, year) => !!city && cityYearFrom(city) <= year && year <= cityYearTo(city);
  // WorldMapEra.RankOf
  function rankOf(era, city) {
    const ranks = era.ranks;
    if (ranks && city.id !== undefined && Object.prototype.hasOwnProperty.call(ranks, city.id)) return ranks[city.id] | 0;
    return city.k === undefined || city.k === null ? 2 : city.k | 0;
  }

  // 开局各城归哪国：点名的城与都城先归，其余的归认领所在地区、都城离它最近的国(点名独立的不归)；返回 Map<城编号, 国家对象>
  function ownersOf(era, cities) {
    const owners = new Map(), byId = new Map(), year = era.year | 0;
    for (const city of cities || [])
      if (activeIn(city, year) && !byId.has(city.id)) byId.set(city.id, city);
    const polities = era.polities || [];
    for (const polity of polities) {
      const listed = (polity.cities || []).concat([polity.cap]);
      for (const id of listed)
        if (id && byId.has(id) && !owners.has(id)) owners.set(id, polity);
    }
    const independent = Array.isArray(era.independent) ? new Set(era.independent) : null;
    for (const city of byId.values()) {
      if (owners.has(city.id)) continue;
      let best = null, bestDistance = Infinity;
      for (const polity of polities) {
        if (!polity.r || !polity.r.includes(city.r === undefined ? "" : city.r) || independent && independent.has(city.id)) continue;
        const seat = byId.get(polity.cap === undefined || polity.cap === null ? "" : polity.cap);
        if (!seat) continue;
        const dx = F(F(city.x) - F(seat.x)), dy = F(F(city.y) - F(seat.y)), distance = F(F(dx * dx) + F(dy * dy));
        if (distance >= bestDistance) continue;
        bestDistance = distance;
        best = polity;
      }
      if (best) owners.set(city.id, best);
    }
    return owners;
  }

  // ---------------------------------------------------------------- 主体

  function compute(input) {
    const era = input && input.era;
    if (!era || !input.codes || !(input.w > 0) || !(input.h > 0) || !(input.step > 0)) return null;
    const imported = input.imported || null;
    const resolved = resolve(era, imported, input.sizeIndex);
    if (!resolved) return null;
    const { frame, size, sizeIndex } = resolved;
    const settings = input.settings || {};
    const cities = input.cities || [], regions = input.regions || [];
    const polities = era.polities || [];
    const year = era.year | 0;
    const land = input.land instanceof Map ? input.land : new Map();
    const develop = input.develop instanceof Map ? input.develop : new Map();
    const exact = era.exact === true;
    const ownWild = era.own_wilderness === true;
    // ScenarioWilderness.WildOff、JungleOff：无人区照设置(本剧本自己开了的不看)；雨林全开发只管导入地图
    const noWild = !ownWild && settings.wilderness === false;
    const noJungle = !!imported && settings.jungleDeveloped === true;

    const { degrees, bottom } = bounds(frame, size);
    const west = F(frame.west);
    const tw = size[0] * 64, th = size[1] * 64, zw = tw >> 3, zh = th >> 3, n = zw * zh;
    const zoneDegrees = F(degrees * 8);

    const owners = ownersOf(era, cities);
    const ownerOf = city => city ? owners.get(city.id) || null : null;

    // 各列、各行两种取样点的经纬度：块中心那一格的左下角(+4，城的地块，WorldMapScenario.PaintTerritory)、
    // 再加半格(+4.5，挂法理、开发度，ScenarioWilderness.Center)
    const lon4 = new Float32Array(zw), lat4 = new Float32Array(zh), lonC = new Float32Array(zw), latC = new Float32Array(zh);
    for (let zx = 0; zx < zw; zx++) {
      lon4[zx] = F(west + F((zx * 8 + 4) * degrees));
      lonC[zx] = F(west + F((zx * 8 + 4.5) * degrees));
    }
    for (let zy = 0; zy < zh; zy++) {
      lat4[zy] = F(bottom + F((zy * 8 + 4) * degrees));
      latC[zy] = F(bottom + F((zy * 8 + 4.5) * degrees));
    }
    const sampler4 = land.size > 0 ? zoneSampler(lon4, lat4, zoneDegrees, LAND_STEP) : null;
    const samplerC = land.size > 0 || develop.size > 0 ? zoneSampler(lonC, latC, zoneDegrees, LAND_STEP) : null;

    // ======== 一、取样(Sample)：每格取最近的预览像素，数各块各类格子；地球地图画大河水道
    const lut = new Uint8Array(256);
    const types = input.types || [];
    for (let i = 0; i < 256; i++) lut[i] = i < types.length ? cellOf(types[i]) : WATER_CELL;
    const codes = input.codes, gridW = input.w | 0, gridH = input.h | 0;
    const lon0 = F(input.lon0), lat0 = F(input.lat0), step = F(input.step);
    const col = new Int32Array(tw), row = new Int32Array(th);
    for (let x = 0; x < tw; x++) {
      const px = roundEven(F(F(F(west + F((x + 0.5) * degrees)) - lon0) / step));
      col[x] = px >= 0 && px < gridW ? px : -1;
    }
    for (let y = 0; y < th; y++) {
      const py = roundEven(F(F(F(bottom + F((y + 0.5) * degrees)) - lat0) / step));
      row[y] = py >= 0 && py < gridH ? py * gridW : -1;
    }
    const river = imported ? null : riverTiles(input.rivers, west, bottom, degrees, tw, th);
    const water = new Uint8Array(n), rock = new Uint8Array(n), hills = new Uint8Array(n);
    // 地面按类型分段记在一张表里：ground[类型 * n + 地块]
    const ground = new Uint8Array(8 * n);
    countTiles(codes, lut, col, row, river, tw, th, zw, n, water, rock, hills, ground);
    const landCount = new Uint8Array(n);
    for (let z = 0; z < n; z++) landCount[z] = 64 - water[z];
    const frozen = ground.subarray(FROZEN_GROUND * n, FROZEN_GROUND * n + n), sand = ground.subarray(SAND_GROUND * n, SAND_GROUND * n + n);
    const lava = ground.subarray(LAVA_GROUND * n, LAVA_GROUND * n + n), plateau = ground.subarray(PLATEAU_GROUND * n, PLATEAU_GROUND * n + n);
    // 丛林、沼泽、草木平地下面要改(时代开发度把草木算回丛林沼泽)，都是这次新数出来的，直接改
    const jungle = ground.subarray(JUNGLE_GROUND * n, JUNGLE_GROUND * n + n), swamp = ground.subarray(SWAMP_GROUND * n, SWAMP_GROUND * n + n);
    const green = ground.subarray(GREEN_GROUND * n, GREEN_GROUND * n + n);

    // ======== 二、城心(放得下的城)：那块留给城(Build 的第二段，口径同 WorldMapScenario.Plan)
    const anchored = new Uint8Array(n);
    const regionById = new Map();
    for (const region of regions)
      if (region && region.id && !regionById.has(region.id)) regionById.set(region.id, region);
    const regionIds = new Set(regions.map(region => region && region.id));
    const seats = [], seatCity = [], seatZoneList = [], seatTileX = [], seatTileY = [];
    const seatOf = new Map(), crowded = [];
    // 城心按 2° 一格分桶(开发度按离最近的城多远算)
    const seatPoints = new Map();
    for (const city of cities) {
      if (!activeIn(city, year) || !regionIds.has(city.r === undefined || city.r === null ? "" : city.r) || seatOf.has(city.id)) continue;
      const lon = F(city.x), lat = F(city.y);
      const tx = Math.floor(F(F(lon - west) / degrees)), ty = Math.floor(F(F(lat - bottom) / degrees));
      if (tx < 4 || ty < 4 || tx >= tw - 4 || ty >= th - 4) continue;
      if (sizeIndex === 0 && rankOf(era, city) > 2) continue;
      const z = (ty >> 3) * zw + (tx >> 3);
      seatOf.set(city.id, seats.length);
      seats.push(city.id);
      seatCity.push(city);
      seatZoneList.push(z);
      seatTileX.push(tx);
      seatTileY.push(ty);
      if (anchored[z]) crowded.push(city.id);
      anchored[z] = 1;
      const bucket = Math.floor(F(lon / 2)) * 1000 + Math.floor(F(lat / 2));
      let list = seatPoints.get(bucket);
      if (!list) seatPoints.set(bucket, list = []);
      list.push(lon, lat);
    }
    const seatCount = seats.length;
    const seatZone = Int32Array.from(seatZoneList);

    // 这座城开局放不下的原因(ScenarioWilderness.PlaceProblem)
    const unplaced = [];
    for (const city of cities) {
      if (!city || city.id === undefined || city.id === null || !activeIn(city, year) || seatOf.has(city.id)) continue;
      const reason = !city.r || !regionById.has(city.r) ? "region" : sizeIndex === 0 && rankOf(era, city) > 2 ? "small" : "outside";
      unplaced.push({ id: city.id, reason });
    }

    // 刷的地块对得上哪几座城("c:城"、"r:地区"、"p:国")：Claims 与 Preview 里各建一遍，同一张表
    function paintKeys() {
      const keys = new Map();
      const add = (name, index) => {
        let list = keys.get(name);
        if (!list) keys.set(name, list = []);
        list.push(index);
      };
      for (const city of cities) {
        if (!city || city.id === undefined || city.id === null || !seatOf.has(city.id)) continue;
        const index = seatOf.get(city.id);
        add("c:" + city.id, index);
        add("r:" + (city.r === undefined || city.r === null ? "" : city.r), index);
        const polity = ownerOf(city);
        if (polity) add("p:" + polity.id, index);
      }
      return keys;
    }

    // 这几座城里离这一格最近的(按格子的直线距离，先到先得)
    function nearestSeat(list, tx, ty) {
      let best = list[0], nearest = Infinity;
      for (let k = 0; k < list.length; k++) {
        const seat = list[k], dx = seatTileX[seat] - tx, dy = seatTileY[seat] - ty, distance = dx * dx + dy * dy;
        if (distance >= nearest) continue;
        nearest = distance;
        best = seat;
      }
      return best;
    }

    // 照原图分地(Claims)：各块地刷给哪座城。"c:" 归那座城、留给城；"p:"、"r:" 归那国、那块法理最近的城但不留；
    // 水面多的只有 "c:" 才领；放不下的城对不上，刷给它的地没人领。本剧本开了无人区的 "c:" 也不留
    let claim = null, kept = null;
    if (exact) {
      claim = new Int32Array(n).fill(-1);
      kept = new Uint8Array(n);
      if (land.size > 0) {
        const keys = paintKeys();
        for (let zy = 0; zy < zh; zy++)
          for (let zx = 0; zx < zw; zx++) {
            const key = zoneValue(land, sampler4, zx, zy, notExact);
            if (key === undefined || key.length < 3) continue;
            const list = keys.get(key);
            if (!list) continue;
            const z = zy * zw + zx;
            const toCity = key.charCodeAt(0) === 99;
            if (!toCity && landCount[z] < TERRITORY_LAND) continue;
            claim[z] = nearestSeat(list, zx * 8 + 4, zy * 8 + 4);
            if (!toCity || ownWild) continue;
            kept[z] = 1;
            anchored[z] = 1;
          }
      }
      // 城心那块归那座城(同开局：城立好时就归它，刷的让开)
      for (let i = seatCount - 1; i >= 0; i--) claim[seatZone[i]] = i;
    }

    // ======== 三、时代开发度与刷的开发度(同 WorldMapDevelopment.Apply)：当年没开出来的草木平地算回丛林、大泽；刷满的开满、留给城
    const dev = new Float32Array(n).fill(-1);
    const table = tableOf(input, imported);
    const areas = table !== null ? prepareAreas(input.development) : [];
    if (develop.size > 0 || areas.length > 0) {
      for (let z = 0; z < n; z++) {
        if (anchored[z]) continue;
        const zx = z % zw, zy = (z / zw) | 0, landHere = landCount[z];
        const lon = lonC[zx], lat = latC[zy];
        let mark = NaN;
        if (develop.size > 0) {
          const text = zoneValue(develop, samplerC, zx, zy, null);
          if (text !== undefined) mark = parseDevelop(text);
        }
        const painted = !Number.isNaN(mark);
        let value = -1, areaSwamp = false, inArea = false;
        if (areas.length > 0) {
          const index = areaAt(areas, lon, lat);
          if (index >= 0) {
            inArea = true;
            const area = areas[index];
            // ValueIn：这一片这个时代的开发度，没写按 all
            value = Object.prototype.hasOwnProperty.call(area.dev, table) ? F(area.dev[table]) : area.all;
            areaSwamp = area.swamp;
          }
        }
        const convertible = landHere >= MIN_LAND && jungle[z] + swamp[z] + green[z] >= T_HALF[landHere];
        let amount, toSwamp;
        if (painted) {
          amount = Math.max(0, Math.min(DEV_FULL, mark));
          // 刷的不在哪一片里：按地块里原有的丛林、沼泽哪样多定类型；一格都没有的(草地)不管
          if (inArea) toSwamp = areaSwamp;
          else if (jungle[z] + swamp[z] > 0) toSwamp = swamp[z] > jungle[z];
          else continue;
          if (amount >= DEV_FULL) {
            dev[z] = DEV_FULL;
            continue;
          }
          if (!convertible) continue;
        } else {
          if (!inArea || value < 0 || value >= DEV_FULL || !convertible) continue;
          // NearCity：腹地的数 × (1 + e^(−距离))，到 100 为止
          amount = value <= 0 ? 0
            : Math.min(DEV_FULL, F(value * F(1 + F(Math.exp(-seatDistance(seatPoints, lon, lat))))));
          if (amount >= DEV_FULL) continue;
          toSwamp = areaSwamp;
        }
        if (toSwamp) swamp[z] += green[z];
        else jungle[z] += green[z];
        green[z] = 0;
        dev[z] = amount;
      }
    }

    // ======== 四、归类(Classify，开局没有上回的类型；雨林全开发时丛林格当普通的地)；刷满开发度的雨林大泽开满、留给城
    const raw = new Uint8Array(n);
    for (let z = 0; z < n; z++) {
      const kind = noWild ? 0
        : classify(landCount[z], water[z], rock[z], hills[z], frozen[z], sand[z], noJungle ? 0 : jungle[z], swamp[z], lava[z], plateau[z]);
      raw[z] = kind;
      if (dev[z] >= DEV_FULL && (kind === K_JUNGLE || kind === K_SWAMP)) anchored[z] = 1;
    }

    // 高原哪一边(AlpineSide)：高原格过半是高原面 1，一成五以下是平地 0，其间是外缘 255
    const alpineSide = new Uint8Array(n);
    for (let z = 0; z < n; z++) {
      const landHere = landCount[z], alpine = plateau[z];
      alpineSide[z] = landHere > 0 && alpine * 2 > landHere ? 1 : landHere > 0 && alpine > T_RIM[landHere] ? RIM_SIDE : 0;
    }
    // 隔着外缘不领(AcrossRim)
    const acrossRim = (seatSide, side) => seatSide !== side && seatSide !== RIM_SIDE && side !== RIM_SIDE;
    const seatSide = new Uint8Array(seatCount);
    for (let i = 0; i < seatCount; i++) seatSide[i] = alpineSide[seatZone[i]];

    // 高寒高原算国土(PlateauReach)：各块地走得到的最近的城(四邻、按块数；只穿过平常的地与高寒高原，不翻外缘)，
    // 有城走得到的高原地块留给城；照原图分地的只认刷的
    const reach = new Int32Array(n).fill(-1);
    if (exact && claim) reach.set(claim);
    else {
      const queue = new Int32Array(n);
      let head = 0, tail = 0;
      for (let i = 0; i < seatCount; i++) {
        const z = seatZone[i];
        if (reach[z] >= 0) continue;
        reach[z] = i;
        queue[tail++] = z;
      }
      while (head < tail) {
        const z = queue[head++], x = z % zw, y = (z / zw) | 0, seat = reach[z];
        for (let d = 0; d < 4; d++) {
          const nx = x + DX4[d], ny = y + DY4[d];
          if (nx < 0 || ny < 0 || nx >= zw || ny >= zh) continue;
          const next = ny * zw + nx;
          if (reach[next] >= 0 || landCount[next] < TERRITORY_LAND) continue;
          const kind = raw[next];
          if (kind !== 0 && kind !== K_PLATEAU) continue;
          if (acrossRim(seatSide[seat], alpineSide[next])) continue;
          reach[next] = seat;
          queue[tail++] = next;
        }
      }
    }
    for (let z = 0; z < n; z++)
      if (raw[z] === K_PLATEAU && reach[z] >= 0) anchored[z] = 1;

    // ======== 五、连片(同 WildernessSystem.Regroup)：同类型四邻连成核心；大的先长，往外收这一类地形过半的地块，
    // 长到同类的小片整片并进来(山脉、熔岩互收对方零星的核心，WildernessSystem.Absorbs)；去掉不到 MinZones 块的
    const piece = new Int32Array(n).fill(-1);
    const groupKind = [], groupZones = [];
    {
      const queue = new Int32Array(n);
      for (let start = 0; start < n; start++) {
        if (piece[start] >= 0 || raw[start] === 0 || anchored[start]) continue;
        const kind = raw[start], mark = groupKind.length, zones = [];
        let head = 0, tail = 0;
        piece[start] = mark;
        queue[tail++] = start;
        while (head < tail) {
          const z = queue[head++], x = z % zw, y = (z / zw) | 0;
          zones.push(z);
          for (let d = 0; d < 4; d++) {
            const nx = x + DX4[d], ny = y + DY4[d];
            if (nx < 0 || ny < 0 || nx >= zw || ny >= zh) continue;
            const next = ny * zw + nx;
            if (piece[next] >= 0 || raw[next] !== kind || anchored[next]) continue;
            piece[next] = mark;
            queue[tail++] = next;
          }
        }
        groupKind.push(kind);
        groupZones.push(zones);
      }
    }
    const groupCount = groupKind.length;
    const grown = new Uint8Array(groupCount);
    // OrderByDescending 是稳定排序，Array.prototype.sort 也是
    const growOrder = Array.from({ length: groupCount }, (_, index) => index).sort((a, b) => groupZones[b].length - groupZones[a].length);
    for (const g of growOrder) {
      const zones = groupZones[g];
      if (zones.length === 0) continue;
      grown[g] = 1;
      const kind = groupKind[g];
      const edge = kind === K_MOUNTAIN ? T_MOUNTAIN_EDGE : kind === K_LAVA ? T_LAVA_EDGE : T_EDGE;
      // zones 既是这一片的地块，也当队列用(C# 里另开一个队列，进队的次序和进 zones 的次序一样)
      for (let head = 0; head < zones.length; head++) {
        const current = zones[head], x = current % zw, y = (current / zw) | 0;
        for (let d = 0; d < 4; d++) {
          const nx = x + DX4[d], ny = y + DY4[d];
          if (nx < 0 || ny < 0 || nx >= zw || ny >= zh) continue;
          const next = ny * zw + nx, other = piece[next];
          if (other >= 0) {
            if (other === g || grown[other]) continue;
            const otherKind = groupKind[other], otherZones = groupZones[other];
            // WildernessSystem.Absorbs
            if (!(otherKind === kind || otherZones.length < MIN_ZONES &&
                  (kind === K_MOUNTAIN && otherKind === K_LAVA || kind === K_LAVA && otherKind === K_MOUNTAIN))) continue;
            for (let k = 0; k < otherZones.length; k++) {
              piece[otherZones[k]] = g;
              zones.push(otherZones[k]);
            }
            groupZones[other] = [];
            grown[other] = 1;
            continue;
          }
          const landHere = landCount[next];
          if (anchored[next] || landHere < MIN_LAND) continue;
          const share = kind === K_MOUNTAIN ? rock[next] + hills[next] + lava[next]
            : kind === K_DESERT || kind === K_TUNDRA ? sand[next] + frozen[next]
            : kind === K_LAVA ? lava[next]
            : kind === K_PLATEAU ? plateau[next] + frozen[next] + rock[next]
            : jungle[next] + swamp[next];
          if (share < edge[landHere]) continue;
          piece[next] = g;
          zones.push(next);
        }
      }
    }
    const region = new Int32Array(n).fill(-1);
    const pieceKind = [], pieceZones = [], pieceLon = [], pieceLat = [];
    {
      const order = [];
      for (let g = 0; g < groupCount; g++)
        if (groupZones[g].length >= MIN_ZONES) order.push(g);
      order.sort((a, b) => groupZones[b].length - groupZones[a].length);
      for (const g of order) {
        const zones = groupZones[g], index = pieceKind.length;
        let sumX = 0, sumY = 0;
        for (let k = 0; k < zones.length; k++) {
          const z = zones[k];
          region[z] = index;
          sumX = F(sumX + z % zw);
          sumY = F(sumY + ((z / zw) | 0));
        }
        pieceKind.push(groupKind[g]);
        pieceZones.push(zones);
        pieceLon.push(F(west + F(F(F(F(sumX / zones.length) * 8) + 4.5) * degrees)));
        pieceLat.push(F(bottom + F(F(F(F(sumY / zones.length) * 8) + 4.5) * degrees)));
      }
    }
    const pieceCount = pieceKind.length;
    // 开发度只记在雨林大泽里(没刷、不在时代表里的天然丛林沼泽为零)
    for (let z = 0; z < n; z++) {
      const r = region[z];
      dev[z] = r >= 0 && (pieceKind[r] === K_JUNGLE || pieceKind[r] === K_SWAMP) ? Math.max(0, dev[z]) : -1;
    }

    // ======== 六、辖地(Territory)：走进各块地的代价(×10；0 过不去，水面)，城的辖地与各片周边的城
    const cost = new Int32Array(n);
    for (let z = 0; z < n; z++) {
      const landHere = landCount[z];
      cost[z] = landHere < TERRITORY_LAND ? 0
        : rock[z] >= T_HALF[landHere] ? MASSIF_COST
        : frozen[z] + sand[z] >= T_BARREN[landHere] ? 30
        : plateau[z] + frozen[z] >= T_BARREN[landHere] ? 30
        : jungle[z] + swamp[z] >= T_HALF[landHere] ? 25
        : frozen[z] + sand[z] >= T_HALF[landHere] ? 20
        : rock[z] + hills[z] >= T_HALF[landHere] ? 16
        : 10;
    }
    const terr = new Int32Array(n).fill(-1);
    if (claim) {
      // 照原图分地：开局只按刷的分，不往外铺、不补小国
      for (let z = 0; z < n; z++) terr[z] = region[z] >= 0 ? -1 : claim[z];
    } else {
      // ScenarioWilderness.Spread：从城心按地形代价往外铺(四邻，无人区、水面铺不进去)，铺不到的陆地开局多半补随机小国
      const dist = new Int32Array(n).fill(INT_MAX);
      const buckets = new Array(FILLER_REACH + 1);
      for (let i = 0; i < seatCount; i++) {
        const z = seatZone[i];
        if (dist[z] === 0) continue;
        dist[z] = 0;
        terr[z] = i;
        (buckets[0] || (buckets[0] = [])).push(z);
      }
      for (let d = 0; d <= FILLER_REACH; d++) {
        const bucket = buckets[d];
        if (!bucket) continue;
        for (let k = 0; k < bucket.length; k++) {
          const z = bucket[k];
          if (dist[z] !== d) continue;
          const x = z % zw, y = (z / zw) | 0, seat = terr[z];
          for (let dir = 0; dir < 4; dir++) {
            const nx = x + DX4[dir], ny = y + DY4[dir];
            if (nx < 0 || ny < 0 || nx >= zw || ny >= zh) continue;
            const next = ny * zw + nx;
            if (region[next] >= 0 || cost[next] === 0) continue;
            // 高原外缘作界线：城不领外缘那边的地
            if (acrossRim(seatSide[seat], alpineSide[next])) continue;
            const nd = d + cost[next];
            if (nd > FILLER_REACH || nd >= dist[next]) continue;
            dist[next] = nd;
            terr[next] = seat;
            (buckets[nd] || (buckets[nd] = [])).push(next);
          }
        }
      }
      // 铺不到的高寒高原归走得到的最近的城(同 WorldMapZones.FillAll，不看远近)，不补小国
      for (let z = 0; z < n; z++)
        if (terr[z] < 0 && region[z] < 0 && raw[z] === K_PLATEAU && reach[z] >= 0) terr[z] = reach[z];
      for (let z = 0; z < n; z++)
        if (terr[z] < 0 && region[z] < 0 && landCount[z] >= TERRITORY_LAND) terr[z] = FILLER;
    }
    // 各片周边的城(八邻、不是无人区的陆地)
    const around = [];
    for (let r = 0; r < pieceCount; r++) {
      const set = new Set(), zones = pieceZones[r];
      for (let k = 0; k < zones.length; k++) {
        const z = zones[k], x = z % zw, y = (z / zw) | 0;
        for (let d = 0; d < 8; d++) {
          const nx = x + DX8[d], ny = y + DY8[d];
          if (nx < 0 || ny < 0 || nx >= zw || ny >= zh) continue;
          const next = ny * zw + nx;
          if (region[next] >= 0 || terr[next] === -1) continue;
          set.add(terr[next]);
        }
      }
      around.push(set);
    }

    // ======== 七、挂法理的走法(Attach)：从各城(连补空小国)的辖地按地块往外走(四邻、不过海、不限远近)，
    // 无人区的地块记最先走到它的那座城。隔山不领：成片的山(山占一半以上的地块)至多连着走进 2 块，从山里下不到别处的平地
    const label = new Int32Array(n).fill(-1);
    {
      const DEPTH_LIMIT = 2;
      const stepAt = new Int32Array(n).fill(-1), depthAt = new Int32Array(n).fill(INT_MAX);
      const queueZone = [], queueDepth = [];
      for (let z = 0; z < n; z++) {
        if (terr[z] === -1) continue;
        label[z] = terr[z];
        stepAt[z] = 0;
        depthAt[z] = 0;
        queueZone.push(z);
        queueDepth.push(0);
      }
      for (let head = 0; head < queueZone.length; head++) {
        const z = queueZone[head], depth = queueDepth[head];
        if (depth > depthAt[z]) continue;
        const x = z % zw, y = (z / zw) | 0, massif = cost[z] === MASSIF_COST;
        for (let d = 0; d < 4; d++) {
          const nx = x + DX4[d], ny = y + DY4[d];
          if (nx < 0 || ny < 0 || nx >= zw || ny >= zh) continue;
          const next = ny * zw + nx;
          if (landCount[next] <= SEA_GROUND) continue;
          const nextDepth = cost[next] === MASSIF_COST ? depth + 1 : massif ? -1 : 0;
          if (nextDepth < 0 || nextDepth > DEPTH_LIMIT || nextDepth >= depthAt[next]) continue;
          // 头一回走到的定归属；之后在山里走得浅一些的路只为接着往前走
          if (stepAt[next] < 0) {
            stepAt[next] = stepAt[z] + 1;
            label[next] = label[z];
          }
          depthAt[next] = nextDepth;
          queueZone.push(next);
          queueDepth.push(nextDepth);
        }
      }
    }

    // ======== 八、时代文件的史实归属(History；地球地图)：各无人区地块落在哪个写了归属的地理单元里(取范围最小的)
    let history = null, historyRegion = null, historyUnit = null;
    const wildOwners = era.wilderness && typeof era.wilderness === "object" ? Object.keys(era.wilderness) : [];
    if (!imported && !era.map && wildOwners.length > 0 && pieceCount > 0) {
      const units = prepareUnits(input.units);
      if (units.size > 0) {
        history = new Array(n).fill(null);
        historyRegion = new Array(n).fill(null);
        historyUnit = new Array(n).fill(null);
        // 各类型能对上的单元(次序同时代文件里写的，面积一样的取先写的)
        const byKind = KINDS.map(name => {
          const list = [];
          if (!name) return list;
          for (const unitId of wildOwners) {
            const polity = era.wilderness[unitId], unit = units.get(unitId);
            if (!polity || !unit || !unit.kinds.includes(name)) continue;
            list.push({ unit, polity: String(polity), id: unitId });
          }
          return list;
        });
        const local = era.wilderness_regions && typeof era.wilderness_regions === "object" ? era.wilderness_regions : null;
        for (let r = 0; r < pieceCount; r++) {
          const candidates = byKind[pieceKind[r]], zones = pieceZones[r];
          if (candidates.length === 0) continue;
          for (let k = 0; k < zones.length; k++) {
            const z = zones[k], lon = lonC[z % zw], lat = latC[(z / zw) | 0];
            let best = null;
            for (let c = 0; c < candidates.length; c++) {
              const entry = candidates[c];
              if (!covers(entry.unit, lon, lat) || best !== null && entry.unit.area >= best.unit.area) continue;
              best = entry;
            }
            if (best === null) continue;
            history[z] = best.polity;
            historyUnit[z] = best.id;
            if (local && Object.prototype.hasOwnProperty.call(local, best.id)) historyRegion[z] = local[best.id];
          }
        }
      }
    }

    // ======== 九、归属(Own)：刷的地块 → 史实归属 → 最先走到的城的王国法理 → 周边同属一国
    const cityById = new Map();
    for (const city of cities)
      if (city && city.id !== undefined && city.id !== null && !cityById.has(city.id)) cityById.set(city.id, city);
    const seatCityAt = index => index >= 0 && index < seatCount ? cityById.get(seats[index]) || null : null;
    // 各城的国家(独立的城自成一国)
    const ownerKey = city => {
      if (!city) return null;
      const polity = ownerOf(city);
      return polity ? "p:" + polity.id : "c:" + city.id;
    };
    // 王国法理首都(同 WorldMapScenario.BuildTitles：剧本定的，再是地区里的国都，再是最大的城)；地区里没有放得下的城就没有这块法理
    const byRegion = new Map();
    for (let i = 0; i < seatCount; i++) {
      const city = seatCityAt(i);
      if (!city) continue;
      const key = city.r === undefined || city.r === null ? "" : city.r;
      let list = byRegion.get(key);
      if (!list) byRegion.set(key, list = []);
      list.push(city);
    }
    const titleSeats = new Map();
    const lowestRank = list => {
      let best = null, bestRank = Infinity;
      for (const city of list) {
        const rank = rankOf(era, city);
        if (rank < bestRank) { bestRank = rank; best = city; }
      }
      return best;
    };
    function titleSeat(regionId) {
      if (!regionId) return null;
      if (titleSeats.has(regionId)) return titleSeats.get(regionId);
      let seat = null;
      const members = byRegion.get(regionId);
      if (members) {
        const info = regionById.get(regionId), chosen = info ? info.cap : null;
        seat = (chosen ? members.find(city => city.id === chosen) : null) ||
          lowestRank(members.filter(city => { const polity = ownerOf(city); return !polity || polity.cap === city.id; })) ||
          lowestRank(members);
      }
      titleSeats.set(regionId, seat);
      return seat;
    }
    // 一座城的法理归谁：法理首都所在国；不称帝的国家都城不是法理首都时另立一块、归它自己(见 WorldMapScenario.OwnTitles)
    const holders = new Map();
    function holderOfCity(city) {
      if (!city) return null;
      if (holders.has(city)) return holders.get(city);
      const seatKey = ownerKey(titleSeat(city.r));
      const polity = ownerOf(city);
      const holder = polity && !polity.emp && polity.cap === city.id && seatKey !== "p:" + polity.id ? "p:" + polity.id : seatKey;
      holders.set(city, holder);
      return holder;
    }
    // 国家的都城(放得下的才算)
    const polityById = new Map();
    for (const polity of polities)
      if (polity && polity.id && !polityById.has(polity.id)) polityById.set(polity.id, polity);
    const capitals = new Map();
    function capitalOf(polityId) {
      if (polityId === null || polityId === undefined) return null;
      if (capitals.has(polityId)) return capitals.get(polityId);
      const polity = polityId ? polityById.get(polityId) : null;
      const key = polity ? (polity.cap === undefined || polity.cap === null ? "" : polity.cap) : null;
      const capital = polity && seatOf.has(key) ? seatCityAt(seatOf.get(key)) : null;
      capitals.set(polityId, capital);
      return capital;
    }

    // 按块：归谁("p:国"、"c:独立的城"、"f"；没有为 null)、挂哪块王国法理、从哪来、是不是永久中立
    const zoneHolder = new Array(n).fill(null), zoneTitle = new Array(n).fill(null);
    const zoneSource = new Uint8Array(n), zoneNeutral = new Uint8Array(n);
    const surround = new Array(pieceCount).fill(null);
    for (let r = 0; r < pieceCount; r++) {
      // 周边的城全属同一国
      let common = null, mixed = false;
      for (const mark of around[r]) {
        const key = mark === FILLER ? null : ownerKey(seatCityAt(mark));
        if (key === null || common !== null && common !== key) {
          mixed = true;
          break;
        }
        common = key;
      }
      surround[r] = mixed ? null : common;
      const zones = pieceZones[r];
      for (let k = 0; k < zones.length; k++) {
        const z = zones[k], zx = z % zw, zy = (z / zw) | 0;
        const key = land.size > 0 ? zoneValue(land, samplerC, zx, zy, notExact) : undefined;
        const neutral = key === "n";
        let title = null, holder = null, source = S_NONE;
        if (key !== undefined && key.length > 2) {
          const id = key.substring(2), first = key.charCodeAt(0);
          const seat = first === 112 ? capitalOf(id) : first === 114 ? titleSeat(id) : null;
          if (seat) {
            title = first === 112 ? seat.r : id;
            holder = first === 112 ? holderOfCity(seat) : ownerKey(seat);
            source = S_PAINT;
          }
        }
        if (source === S_NONE && history !== null && history[z] !== null) {
          const historical = history[z], capital = capitalOf(historical);
          if (capital) {
            let local = titleSeat(historyRegion[z]);
            if (!local || ownerKey(local) !== "p:" + historical || holderOfCity(local) !== "p:" + historical) local = capital;
            title = local.r;
            holder = holderOfCity(local);
            source = S_HISTORY;
          }
        }
        if (source === S_NONE && label[z] !== -1) {
          const near = seatCityAt(label[z]);
          title = near ? near.r : null;
          holder = label[z] === FILLER ? "f" : holderOfCity(near);
          source = S_NEAR;
        }
        if (holder === null && surround[r] !== null) {
          holder = surround[r];
          if (source === S_NONE) source = S_SURROUND;
        }
        zoneTitle[z] = title === undefined ? null : title;
        zoneHolder[z] = neutral ? null : holder;
        zoneSource[z] = neutral ? S_NEUTRAL : source;
        zoneNeutral[z] = neutral ? 1 : 0;
      }
    }

    // ======== 十、预览(Preview)：各块地开局归哪座城。先后同开局(WorldMapScenario.SpreadTerritory)：城心；刷的地块；
    // 历史范围；州府治所；再一起按地形代价往外铺(不进无人区、不过长城)。照原图分地的不往外铺
    const zoneOwner = new Int32Array(n).fill(UNCLAIMED), zonePainted = new Uint8Array(n);
    for (let i = 0; i < seatCount; i++)
      if (zoneOwner[seatZone[i]] < 0) zoneOwner[seatZone[i]] = i;
    const budget = new Int32Array(seatCount), cap = new Int32Array(seatCount);
    for (const city of cities) {
      if (!city || city.id === undefined || city.id === null || !seatOf.has(city.id)) continue;
      const index = seatOf.get(city.id), rank = Math.max(0, Math.min(3, rankOf(era, city)));
      budget[index] = roundEven(F(TERRITORY_BUDGET[rank] * 10));
      cap[index] = TERRITORY_CAP[rank];
    }
    if (land.size > 0) {
      const keys = paintKeys();
      for (let zy = 0; zy < zh; zy++)
        for (let zx = 0; zx < zw; zx++) {
          const z = zy * zw + zx;
          if (zoneOwner[z] >= 0 || region[z] >= 0) continue;
          const key = zoneValue(land, sampler4, zx, zy, notExact);
          if (key === undefined || key.length < 3) continue;
          const list = keys.get(key);
          if (!list) continue;
          if (key.charCodeAt(0) !== 99 && landCount[z] < TERRITORY_LAND) continue;
          zoneOwner[z] = nearestSeat(list, zx * 8 + 4, zy * 8 + 4);
          zonePainted[z] = 1;
        }
    }
    // 一座城也没有的图开局也不补小国(补空要照着剧本的城取文化，见 WorldMapScenario.PlanFillers)
    if (!exact && seatCount > 0) {
      // 这国某行政区(-1 京畿)放得下的城(MembersOf；ScenarioEditing.DivisionOf：都城、不在任何行政区的为 -1)
      const membersBy = new Map();
      for (const city of cities) {
        if (!city || city.id === undefined || city.id === null || !seatOf.has(city.id)) continue;
        const polity = ownerOf(city);
        if (!polity) continue;
        let division = -1;
        if (Array.isArray(polity.divisions) && city.id && polity.cap !== city.id)
          for (let d = 0; d < polity.divisions.length; d++)
            if (polity.divisions[d] && Array.isArray(polity.divisions[d].cities) && polity.divisions[d].cities.includes(city.id)) {
              division = d;
              break;
            }
        let byDivision = membersBy.get(polity);
        if (!byDivision) membersBy.set(polity, byDivision = new Map());
        let list = byDivision.get(division);
        if (!list) byDivision.set(division, list = []);
        list.push(seatOf.get(city.id));
      }
      const NO_SEATS = [];
      const membersOf = (polity, division) => {
        const byDivision = membersBy.get(polity);
        return byDivision && byDivision.get(division) || NO_SEATS;
      };

      // 历史范围(BoundaryOwners，同 WorldMapScenario.PaintAdministrativeDefaults)：刷的、城心之外的地按范围归本行政区最近的城。
      // 范围取行政区自己写的 boundary，没有就查行政辖境表(input.boundaries，同 WorldMapAdministrativeBoundaries.For：
      // 本剧本编号、年份、国、行政区名对得上的；没有再按所本的预设编号查)
      const drawn = new Int32Array(n).fill(-1);
      const boundaryTable = Array.isArray(input.boundaries) ? input.boundaries : [];
      const eraId = String(era.id || "");
      const lookup = (id, polityId, name) => boundaryTable.filter(item => item && item.era === id && item.year === year &&
        item.polity === polityId && (item.division || "") === name && Array.isArray(item.polygons) && item.polygons.length > 0);
      for (const polity of polities) {
        const divisions = Array.isArray(polity.divisions) ? polity.divisions : [];
        for (let division = -1; division < divisions.length; division++) {
          const data = division >= 0 ? divisions[division] || null : null;
          const name = data === null ? "_core" : String(data.name || "").split("|")[0];
          let polygons = [];
          const custom = data === null ? polity.boundary : data.boundary;
          if (custom) polygons = custom;
          else if (boundaryTable.length > 0) {
            let found = lookup(eraId, polity.id, name);
            if (found.length === 0 && table && table !== eraId) found = lookup(table, polity.id, name);
            for (const item of found) polygons = polygons.concat(item.polygons);
          }
          const box = polygonBounds(polygons);
          if (!box) continue;
          const members = membersOf(polity, division);
          if (members.length === 0) continue;
          const x0 = Math.max(0, Math.floor(F(F(box.west - west) / zoneDegrees))), x1 = Math.min(zw - 1, Math.ceil(F(F(box.east - west) / zoneDegrees)));
          const y0 = Math.max(0, Math.floor(F(F(box.south - bottom) / zoneDegrees))), y1 = Math.min(zh - 1, Math.ceil(F(F(box.north - bottom) / zoneDegrees)));
          for (let zy = y0; zy <= y1; zy++)
            for (let zx = x0; zx <= x1; zx++) {
              const z = zy * zw + zx;
              if (drawn[z] >= 0 || landCount[z] < 16) continue;
              if (!polygonsContain(polygons, lon4[zx], lat4[zy])) continue;
              drawn[z] = nearestSeat(members, zx * 8 + 4, zy * 8 + 4);
            }
        }
      }
      for (let z = 0; z < n; z++)
        if (zoneOwner[z] < 0 && drawn[z] >= 0 && region[z] < 0) zoneOwner[z] = drawn[z];

      // 州府治所：和城一起往外铺，铺到的地归本行政区最近的城(同 AnchorTerritory)
      const seatCities = [];
      for (const polity of polities) {
        const divisions = Array.isArray(polity.divisions) ? polity.divisions : [];
        for (let division = -1; division < divisions.length; division++) {
          const points = division < 0 ? polity.seats : divisions[division] ? divisions[division].seats : null;
          if (!points || points.length === 0) continue;
          let members = membersOf(polity, division);
          if (members.length === 0) members = membersOf(polity, -1);
          if (members.length === 0) continue;
          for (const point of points) {
            if (!point || point.length < 2) continue;
            // ZoneAt
            const zx = Math.floor(F(F(F(point[0]) - west) / zoneDegrees)), zy = Math.floor(F(F(F(point[1]) - bottom) / zoneDegrees));
            if (zx < 0 || zy < 0 || zx >= zw || zy >= zh) continue;
            const z = zy * zw + zx;
            if (zoneOwner[z] >= 0 || region[z] >= 0 || cost[z] === 0) continue;
            zoneOwner[z] = seatCount + seatCities.length;
            seatCities.push(members);
          }
        }
      }
      const total = seatCount + seatCities.length;
      const allBudget = new Int32Array(total), allCap = new Int32Array(total);
      const anchorBudget = roundEven(F(SEAT_BUDGET * 10));
      for (let i = 0; i < total; i++) {
        allBudget[i] = i < seatCount ? budget[i] : anchorBudget;
        allCap[i] = i < seatCount ? cap[i] : INT_MAX;
      }
      // 压着长城的地块(按折线；开局的走向还要沿山脊、河谷弯折，见 WorldMapWalls.Plan，这里差一两块)
      const wall = new Uint8Array(n);
      for (const item of era.walls || [])
        markWall(item ? item.pts : null, west, bottom, zoneDegrees, zw, zh, wall);
      spread(zw, zh, zoneOwner, cost, region, wall, allBudget, allCap);
      // 铺不到的高寒高原归走得到的最近的城(同 WorldMapZones.FillAll)，不补小国
      for (let z = 0; z < n; z++)
        if (zoneOwner[z] === FILLER && raw[z] === K_PLATEAU && reach[z] >= 0) zoneOwner[z] = reach[z];
      for (let z = 0; z < n; z++)
        if (zoneOwner[z] >= seatCount)
          zoneOwner[z] = nearestSeat(seatCities[zoneOwner[z] - seatCount], z % zw * 8 + 4, ((z / zw) | 0) * 8 + 4);
    }

    // ======== 汇总成输出
    const zoneWild = new Uint8Array(n);
    for (let z = 0; z < n; z++)
      if (region[z] >= 0) zoneWild[z] = pieceKind[region[z]];
    // 一片里出现得最多的值(一样多取先出现的)
    const mostOf = (zones, pick) => {
      const counts = new Map();
      let best, most = 0;
      for (let k = 0; k < zones.length; k++) {
        const value = pick(zones[k]);
        if (value === undefined) continue;
        const count = (counts.get(value) || 0) + 1;
        counts.set(value, count);
        if (count > most) { most = count; best = value; }
      }
      return best;
    };
    // "p:国" → 国编号，"c:城" → "i:城编号"，"f" 补空小国，没有为 ""
    const ownerText = holder => holder === null || holder === undefined ? ""
      : holder === "f" ? "f" : holder.startsWith("p:") ? holder.substring(2) : "i:" + holder.substring(2);
    const pieces = [];
    for (let r = 0; r < pieceCount; r++) {
      const zones = pieceZones[r], kind = pieceKind[r];
      let developed = -1;
      if (kind === K_JUNGLE || kind === K_SWAMP) {
        let sum = 0;
        for (let k = 0; k < zones.length; k++) sum += dev[zones[k]];
        developed = sum / zones.length;
      }
      let neutral = true;
      for (let k = 0; k < zones.length; k++)
        if (!zoneNeutral[zones[k]]) { neutral = false; break; }
      const title = mostOf(zones, z => zoneTitle[z] === null ? "" : zoneTitle[z]);
      const source = mostOf(zones, z => zoneSource[z]);
      const holder = mostOf(zones, z => zoneHolder[z] === null ? "" : zoneHolder[z]);
      // 地理单元：这一片多数地块落在哪个写了史实归属的单元里(多数地块没落在哪个单元里的为 "")
      const unit = historyUnit ? mostOf(zones, z => historyUnit[z] === null ? "" : historyUnit[z]) : "";
      pieces.push({
        kind: KINDS[kind], zones: zones.length, lon: pieceLon[r], lat: pieceLat[r], develop: developed, neutral,
        region: title || "", source: SOURCES[source || 0], owner: ownerText(holder || null), unit: unit || "",
        // 以下是接口之外多给的：周边的城全属同一国时的那一国(同 owner 的写法，没有为 "")
        surround: ownerText(surround[r])
      });
    }
    // 各城归哪国(国编号；独立的城为 "")
    const seatPolity = seats.map((_, index) => {
      const polity = ownerOf(seatCity[index]);
      return polity ? String(polity.id) : "";
    });

    return {
      frameId: frame.id, size: [size[0], size[1]], cols: zw, rows: zh,
      west, bottom, zoneDegrees,
      zoneOwner, zoneWild, zonePiece: region, zonePainted,
      seats, seatZone, unplaced, crowded,
      kinds: KINDS.slice(), pieces,
      // ---- 接口之外多给的(都是算的时候现成的，按块比按片准)
      sizeIndex,                 // 算第几档(定死大小的图按面积折成的档，小档不放三档城)
      table,                     // 时代开发度按哪个时代的表(没有为 null)
      wildOff: noWild,           // 因模组设置关着无人区，这张图开局不划
      seatPolity,                // 各城归哪国
      zoneDev: dev,              // 雨林大泽地块的开发度(不是为 -1)
      zoneRaw: raw,              // 光按地形归的类(kinds 的下标；留给城的地块也有值)
      zoneLand: landCount,       // 各块的陆地格数(0~64)
      zoneCost: cost,            // 走进各块地的代价(×10；0 为水面多、开局没人领)
      zoneLabel: label,          // 无人区地块最先被哪座城走到(seats 的下标；-2 补空小国；-1 走不到)
      zoneSource,                // 无人区地块的归属从哪来(sources 的下标)
      sources: SOURCES.slice(),
      zoneHolder,                // 无人区地块归谁("p:国编号"、"c:独立的城"、"f" 补空小国；没有、永久中立为 null)
      zoneRegion: zoneTitle,     // 无人区地块挂哪块王国法理(地区编号；没挂为 null)
      zoneNeutral                // 无人区地块刷了永久中立
    };
  }

  // ScenarioWilderness.Sample 的数格子那一段：每格取最近的预览像素，数各块的水面、山地、丘陵与各类地面
  // (单拎成一个小函数：近三百万格，是最费时的一段，免得跟着 compute 整个函数一起被引擎退回慢速执行)
  function countTiles(codes, lut, col, row, river, tw, th, zw, n, water, rock, hills, ground) {
    for (let ty = 0; ty < th; ty++) {
      const offset = row[ty], zoneRow = (ty >> 3) * zw, riverRow = ty * tw;
      for (let tx = 0; tx < tw; tx++) {
        const c = col[tx];
        const cell = offset < 0 || c < 0 ? WATER_CELL : lut[codes[offset + c]];
        const z = zoneRow + (tx >> 3);
        let relief = cell & 3;
        if (relief !== WATER_RELIEF && river !== null && river[riverRow + tx] === 1) relief = WATER_RELIEF;
        if (relief === WATER_RELIEF) {
          water[z]++;
          continue;
        }
        if (relief === ROCK_RELIEF) rock[z]++;
        else if (relief === HILL_RELIEF) hills[z]++;
        // 岩浆(平地)算陆地，另记(同 WildernessSystem.Count)
        const kind = cell >> 2;
        if (kind !== 0) ground[kind * n + z]++;
      }
    }
  }

  // ScenarioWilderness.Classify
  function classify(land, water, rock, hills, frozen, sand, jungle, swamp, lava, plateau) {
    if (land < MIN_LAND || water > MAX_WATER_TILES) return 0;
    // 熔岩先判(熔岩原、火山)；够不上的零星岩浆算山地
    if (lava >= T_LAVA_CORE[land] || lava >= T_LAVA_FLANK[land] && lava + rock >= T_LAVA_RUGGED[land]) return K_LAVA;
    const peak = rock + lava;
    if (peak >= T_MOUNTAIN_CORE[land] || peak >= T_MOUNTAIN_FLANK[land] && peak + hills >= T_MOUNTAIN_RUGGED[land]) return K_MOUNTAIN;
    // 高原(雪山既算山地又算冻土，按两样里多的那样估)
    if (plateau >= T_PLATEAU_CORE[land] && plateau + Math.max(rock + hills, frozen) >= T_BARREN[land]) return K_PLATEAU;
    if (frozen + sand >= T_BARREN[land]) return frozen > sand ? K_TUNDRA : K_DESERT;
    if (swamp >= T_SWAMP[land]) return K_SWAMP;
    if (jungle + swamp >= T_JUNGLE[land]) return jungle >= swamp ? K_JUNGLE : K_SWAMP;
    return 0;
  }

  // 大河水道落在哪些格上(EarthTerrain.RiverTiles 的走法，渡口已留开；图边一格不画，同 ScenarioWilderness.Rivers)。
  // rivers：[{ rank, line: 经,纬,经,纬… }]；没有河返回 null
  function riverTiles(rivers, west, bottom, degrees, tw, th) {
    if (!Array.isArray(rivers) || rivers.length === 0) return null;
    const river = new Uint8Array(tw * th);
    for (const item of rivers) {
      const line = item && item.line;
      if (!line || line.length < 4) continue;
      let walked = 0;
      let px = Math.floor(F(F(F(line[0]) - west) / degrees)), py = Math.floor(F(F(F(line[1]) - bottom) / degrees));
      for (let i = 2; i + 1 < line.length; i += 2) {
        let x0 = px, y0 = py;
        const x1 = Math.floor(F(F(F(line[i]) - west) / degrees)), y1 = Math.floor(F(F(F(line[i + 1]) - bottom) / degrees));
        px = x1;
        py = y1;
        const dx = Math.abs(x1 - x0), dy = -Math.abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        let err = dx + dy;
        for (;;) {
          if (walked++ % FORD_EVERY >= FORD_WIDTH)
            for (let t = 0; t < RIVER_THICKNESS; t++) {
              const x = x0 + (dy === 0 ? 0 : t), y = y0 + (dy === 0 ? t : 0);
              if (x >= 1 && y >= 1 && x < tw - 1 && y < th - 1) river[y * tw + x] = 1;
            }
          if (x0 === x1 && y0 === y1) break;
          const e2 = 2 * err;
          if (e2 >= dy) { err += dy; x0 += sx; }
          if (e2 <= dx) { err += dx; y0 += sy; }
        }
      }
    }
    return river;
  }

  // 离最近的城心几度(经向按纬度折算，ScenarioWilderness.SeatDistance)；周围 ±3 个 2° 的桶里没有城为无穷远
  function seatDistance(seatPoints, lon, lat) {
    const squeeze = F(Math.cos(F(lat * DEG2RAD)));
    const cx = Math.floor(F(lon / 2)), cy = Math.floor(F(lat / 2));
    let best = Infinity;
    for (let dy = -3; dy <= 3; dy++)
      for (let dx = -3; dx <= 3; dx++) {
        const list = seatPoints.get((cx + dx) * 1000 + cy + dy);
        if (!list) continue;
        for (let k = 0; k < list.length; k += 2) {
          const x = F(F(list[k] - lon) * squeeze), y = F(list[k + 1] - lat);
          best = Math.min(best, F(F(x * x) + F(y * y)));
        }
      }
    return best === Infinity ? Infinity : F(Math.sqrt(best));
  }

  // ScenarioPreviewRules.MarkWall：一道长城压着哪些地块(至多八邻相连，地块四邻走不过去)；points 为 [经度, 纬度]
  function markWall(points, west, bottom, side, width, height, wall) {
    if (!points || points.length < 2 || !(side > 0)) return;
    for (let p = 1; p < points.length; p++) {
      const a = points[p - 1], b = points[p];
      if (!a || !b || a.length < 2 || b.length < 2) continue;
      const ax = F(F(F(a[0]) - west) / side), ay = F(F(F(a[1]) - bottom) / side);
      const bx = F(F(F(b[0]) - west) / side), by = F(F(F(b[1]) - bottom) / side);
      const steps = Math.max(1, Math.ceil(F(Math.max(Math.abs(F(bx - ax)), Math.abs(F(by - ay))) * 2)));
      for (let s = 0; s <= steps; s++) {
        const x = Math.floor(F(ax + F(F(F(bx - ax) * s) / steps))), y = Math.floor(F(ay + F(F(F(by - ay) * s) / steps)));
        if (x >= 0 && y >= 0 && x < width && y < height) wall[y * width + x] = 1;
      }
    }
  }

  // ScenarioPreviewRules.Spread：owner 里城心、刷的地块已填好(城的下标)，其余为 -1；cost 为走进这块地的代价(0 过不去)；
  // 无人区(region >= 0)、长城进不去；budget、cap 为各城走得多远、领几块(已有的地算在内)。
  // 铺完：压着长城的地块归挨着的城；还没人领的陆地记 Filler
  function spread(width, height, owner, cost, region, wall, budget, cap) {
    const n = width * height;
    const dist = new Int32Array(n).fill(INT_MAX), from = new Int32Array(n).fill(-1);
    const counts = new Int32Array(budget.length);
    let reach = 0;
    for (let i = 0; i < budget.length; i++) reach = Math.max(reach, budget[i]);
    const buckets = new Array(reach + 1);
    for (let z = 0; z < n; z++) {
      if (owner[z] < 0) continue;
      dist[z] = 0;
      from[z] = owner[z];
      counts[owner[z]]++;
      (buckets[0] || (buckets[0] = [])).push(z);
    }
    const closed = zone => cost[zone] === 0 || region[zone] >= 0 || wall[zone] === 1;
    for (let d = 0; d <= reach; d++) {
      const bucket = buckets[d];
      if (!bucket) continue;
      for (let k = 0; k < bucket.length; k++) {
        const z = bucket[k];
        if (dist[z] !== d) continue;
        const s = from[z];
        if (owner[z] < 0) {
          if (counts[s] >= cap[s]) continue;
          owner[z] = s;
          counts[s]++;
        } else if (owner[z] !== s) continue;
        const x = z % width, y = (z / width) | 0;
        for (let dir = 0; dir < 8; dir++) {
          const nx = x + DX8[dir], ny = y + DY8[dir];
          if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
          const next = ny * width + nx;
          if (owner[next] >= 0 || cost[next] === 0 || region[next] >= 0 || wall[next] === 1) continue;
          let step = cost[next];
          if (dir >= 4) {
            // 斜走：两边都走不过去就是穿缝(长城的斜线、水角)，不算连着；代价乘 1.4
            if (closed(y * width + nx) && closed(ny * width + x)) continue;
            step = (step * 14 / 10) | 0;
          }
          const nd = d + step;
          if (nd > budget[s] || nd >= dist[next]) continue;
          dist[next] = nd;
          from[next] = s;
          (buckets[nd] || (buckets[nd] = [])).push(next);
        }
      }
    }
    for (let z = 0; z < n; z++) {
      if (owner[z] !== UNCLAIMED || cost[z] === 0 || region[z] >= 0) continue;
      let near = -1;
      if (wall[z] === 1) {
        const x = z % width, y = (z / width) | 0;
        for (let dir = 0; dir < 4 && near < 0; dir++) {
          const nx = x + DX8[dir], ny = y + DY8[dir];
          if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
          const next = ny * width + nx;
          if (wall[next] !== 1 && owner[next] >= 0) near = owner[next];
        }
      }
      owner[z] = near >= 0 ? near : FILLER;
    }
  }

  // ScenarioPreviewRules.Vote(平滑地块，画图层时用)：zones、weights 为一格周围四块地和权重(块号 -1 或类别为 skip 的不算)；
  // 同一类的权重加起来，取最大的一类，返回这一类里权重最大的那块；都不算返回 -1
  function vote(zones, weights, classes, skip) {
    let best = -1, pick = -1;
    for (let i = 0; i < zones.length; i++) {
      const zone = zones[i];
      if (zone < 0 || classes[zone] === skip) continue;
      let sum = 0, top = -1, representative = zone;
      for (let j = 0; j < zones.length; j++) {
        if (zones[j] < 0 || classes[zones[j]] !== classes[zone]) continue;
        sum = F(sum + weights[j]);
        if (weights[j] <= top) continue;
        top = weights[j];
        representative = zones[j];
      }
      if (sum > F(best + 1e-5) || Math.abs(F(sum - best)) <= F(1e-5) && representative < pick) {
        best = sum;
        pick = representative;
      }
    }
    return pick;
  }

  // ScenarioPreviewRules.BaseValue：擦掉这一格回到什么——导入时的归属(baseline：Map<格子, 键>；归的城还在才算，cities：Set<城编号>)；没有为 null
  function baseValue(baseline, cell, cities) {
    if (!baseline || !baseline.has(cell)) return null;
    const key = baseline.get(cell);
    return typeof key === "string" && key.startsWith("c:") && !(cities && cities.has(key.substring(2))) ? null : key;
  }

  return {
    compute, frameOf, sizeOf, vote, baseValue,
    // 另给的小工具：四档范围、范围的南界与一格几度、地块写法的标记、格子编号、城归哪国、无人区类型名表
    FRAMES, bounds, cellOf, pack, ownersOf, KINDS: KINDS.slice(), SOURCES: SOURCES.slice(), FILLER, UNCLAIMED
  };
});
