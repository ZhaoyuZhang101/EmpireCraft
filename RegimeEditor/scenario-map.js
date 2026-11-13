// 剧本编辑器的地图一侧：地表像素图(WorldMaps/earth.bytes，每格 0.05°，隔一格取一格，一像素 0.1°)、图层颜色、城点与各种标记。
// 做法照游戏里的 ScenarioEditorScreen：底图叠上刷的地形；图层颜色按 0.2° 一格(同刷地块的格子)从城往外按地形代价铺开
// (水过不去、翻山贵)，刷的地块先归刷的那家，两块相接处描边。
// 图层颜色照开局逐块预演(scenario-preview.js，搬自游戏的 ScenarioWilderness)：各块地开局归哪座城、哪些是无人区、挂哪个法理、
// 哪些地补空小国、哪些城放不下。预演模块没载入或算不出来时退回"从城往外按地形代价铺"的近似(computeFillApprox)。
(function () {
  const D = window.SD;
  const STRIDE = 2, FILL = 2;
  // 各地形大类(classOf)走进去的代价，0 为水(过不去)：水、平原、高原、丘陵、山地、雪山、荒漠冻土、雪丘
  const CLASS_COST = [0, 10, 13, 20, 45, 70, 14, 30];
  // 从城往外至多铺多远(代价；平原一格 10，约十度)
  const REACH = 500;

  const M = {
    ready: false, w: 0, h: 0, fw: 0, fh: 0, lonW: 0, latS: 0, latN: 0, offX: 0, offY: 0, types: [],
    scale: 1, ox: 0, oy: 0, layer: "realm", highlight: null, showInactive: true,
    sizeIndex: 1, smooth: false, preview: null, previewKey: "", previewError: "", unplaced: new Map(), crowded: new Set(),
    rivers: [], units: [],
    fillKeys: [], fillOwner: null, fillRevision: -1
  };
  let baseColor, baseClass, baseCode, workClass, terrainImage, terrainCanvas, terrainCtx, fillCanvas, fillCtx, fillImage;
  let canvas, ctx, needsDraw = false, overlayDraw = null, appliedTerrainRevision = -1;
  let earth = null, current = null, workCode = null;

  // ---------- 地形 ----------
  function previewColor(type) {
    switch (type) {
      case "deep_ocean": return [40, 90, 190];
      case "close_ocean": return [55, 120, 220];
      case "shallow_waters": return [80, 170, 240];
      case "sand": return [240, 220, 150];
      case "mountains": return [90, 90, 90];
      case "mountains:snow_block": return [245, 245, 250];
      case "hills": return [130, 125, 110];
      case "hills:snow_hills": return [215, 220, 230];
      case "alpine_plateau": return [143, 144, 112];
      case "loess_plateau": return [185, 154, 104];
      case "steppe_plateau": return [157, 164, 85];
    }
    const high = type.endsWith("_high");
    const biome = type.includes(":") ? type.slice(type.indexOf(":") + 1) : type;
    if (biome.includes("grass")) return high ? [70, 130, 45] : [96, 160, 60];
    if (biome.includes("savanna")) return high ? [180, 160, 70] : [200, 190, 90];
    if (biome.includes("jungle")) return high ? [30, 100, 30] : [40, 120, 40];
    if (biome.includes("desert")) return high ? [225, 195, 110] : [245, 225, 150];
    if (biome.includes("permafrost")) return high ? [180, 195, 210] : [200, 215, 225];
    if (biome.includes("birch")) return high ? [90, 130, 75] : [110, 150, 90];
    if (biome.includes("maple")) return high ? [110, 140, 60] : [130, 160, 70];
    if (biome.includes("swamp")) return high ? [80, 100, 50] : [90, 110, 60];
    return [96, 160, 60];
  }

  // 地形大类：0 水，1 平原，2 高原，3 丘陵，4 山地，5 雪山，6 荒漠冻土，7 雪丘
  function classOf(type) {
    if (!type || type.includes("ocean") || type.includes("waters")) return 0;
    switch (type) {
      case "mountains": return 4;
      case "mountains:snow_block": return 5;
      case "hills": return 3;
      case "hills:snow_hills": return 7;
      case "alpine_plateau": case "loess_plateau": case "steppe_plateau": return 2;
    }
    if (type.includes("desert") || type.includes("permafrost")) return 6;
    return type.endsWith("_high") ? 2 : 1;
  }

  async function inflateRaw(bytes) {
    const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("deflate-raw"));
    return new Uint8Array(await new Response(stream).arrayBuffer());
  }

  // earth.bytes：ECEARTH1、列、行、左下角经纬度、步长、地块写法表、deflate 压的网格(第 0 行在最南)
  async function loadEarth(bytes) {
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    if (new TextDecoder().decode(bytes.subarray(0, 8)) !== "ECEARTH1") throw new Error("地表数据文件头不对");
    const cols = view.getInt32(8, true), rows = view.getInt32(12, true);
    const lon0 = view.getFloat32(16, true), lat0 = view.getFloat32(20, true), step = view.getFloat32(24, true);
    const count = view.getInt32(28, true);
    let at = 32;
    const types = [];
    for (let i = 0; i < count; i++) {
      const length = bytes[at];
      types.push(new TextDecoder().decode(bytes.subarray(at + 1, at + 1 + length)));
      at += 1 + length;
    }
    const packed = view.getInt32(at, true);
    const grid = await inflateRaw(bytes.subarray(at + 4, at + 4 + packed));
    if (grid.length < cols * rows) throw new Error("地表网格不完整");
    M.types = types;
    const w = Math.floor(cols / STRIDE), h = Math.floor(rows / STRIDE), preview = step * STRIDE;
    const codes = new Uint8Array(w * h);
    for (let y = 0; y < h; y++)
      for (let x = 0; x < w; x++) codes[y * w + x] = grid[y * STRIDE * cols + x * STRIDE];
    // 像素对到刷地形的格子(0.1°)：同 ScenarioEditorScreen.ApplyTerrainPaint 的 offX、offY
    earth = makeGrid(w, h, codes, Math.floor(lon0 / D.TERRAIN_STEP + 0.5 * preview / D.TERRAIN_STEP),
      Math.floor(lat0 / D.TERRAIN_STEP + 0.5 * preview / D.TERRAIN_STEP), null);
    earth.lon0 = lon0; earth.lat0 = lat0;
    useGrid(earth);
    M.ready = true;
  }

  // 一张地表网格(一像素 0.1°，第 0 行在最南)：地块编号，连同按编号查好的颜色、地形大类
  function makeGrid(w, h, codes, offX, offY, imported) {
    const lut = M.types.map(previewColor), classes = M.types.map(classOf);
    const color = new Uint32Array(w * h), cls = new Uint8Array(w * h);
    for (let index = 0; index < w * h; index++) {
      if (codes[index] >= lut.length) codes[index] = 0;
      const rgb = lut[codes[index]];
      color[index] = 0xFF000000 | (rgb[2] << 16) | (rgb[1] << 8) | rgb[0];
      cls[index] = classes[codes[index]];
    }
    return { w, h, codes, color, cls, offX, offY, imported, lon0: 0, lat0: 0 };
  }

  // 换上一张网格(地球的、或导入地图的)：底图、图层颜色两张画布照新尺寸重做
  function useGrid(grid) {
    if (current === grid) return;
    current = grid;
    M.w = grid.w; M.h = grid.h; M.fw = Math.max(1, Math.floor(grid.w / FILL)); M.fh = Math.max(1, Math.floor(grid.h / FILL));
    M.offX = grid.offX; M.offY = grid.offY;
    M.lonW = M.offX * D.TERRAIN_STEP;
    M.latS = M.offY * D.TERRAIN_STEP;
    M.latN = M.latS + grid.h * D.TERRAIN_STEP;
    M.imported = grid.imported;
    baseColor = grid.color; baseClass = grid.cls; baseCode = grid.codes;
    workClass = new Uint8Array(baseClass);
    workCode = new Uint8Array(baseCode);
    M.gridStamp = (M.gridStamp || 0) + 1;
    terrainCanvas = document.createElement("canvas");
    terrainCanvas.width = grid.w; terrainCanvas.height = grid.h;
    terrainCtx = terrainCanvas.getContext("2d");
    terrainImage = terrainCtx.createImageData(grid.w, grid.h);
    fillCanvas = document.createElement("canvas");
    fillCanvas.width = M.fw; fillCanvas.height = M.fh;
    fillCtx = fillCanvas.getContext("2d");
    fillImage = fillCtx.createImageData(M.fw, M.fh);
    M.fillOwner = new Int32Array(M.fw * M.fh).fill(-1);
    appliedTerrainRevision = -1;
    M.fillRevision = -1;
    M.fillDrawn = null;
    redraw();
  }

  const useEarth = () => { if (earth) useGrid(earth); };

  // 导入地图(同 WorldMapImporter)：剧本文件夹里的 map.png 一像素一格(每格 0.1°，经纬度从左下角 0 起)，太大的按比例缩到 2048 以内；
  // color 按最近的颜色认成地形(对照表 map_colors.txt)，height 是灰度高程(低于海平面是海)。image 为 {width, height, data(RGBA，自上而下)}，
  // 还没放图给 null：一片深海(1024×512)。返回 {cols, rows, blank}
  const IMPORT_MAX = 2048, BLANK_W = 1024, BLANK_H = 512, IMPORT_SCALES = [1, 1.5, 2];

  function useImport(image, mode, sea) {
    const key = image ? `${image.stamp}|${mode}|${sea}` : "blank";
    if (current?.imported?.key === key) return current.imported;
    const code = type => Math.max(0, M.types.indexOf(type));
    let cols = BLANK_W, rows = BLANK_H, codes;
    if (!image) codes = new Uint8Array(cols * rows).fill(code("deep_ocean"));
    else {
      const width = image.width, height = image.height, data = image.data;
      const shrink = Math.max(1, Math.ceil(Math.max(width, height) / IMPORT_MAX));
      cols = Math.max(64, Math.floor(width / shrink));
      rows = Math.max(64, Math.floor(height / shrink));
      codes = new Uint8Array(cols * rows);
      const byHeight = mode === "height", level = Math.min(0.98, Math.max(0.02, Number(sea) || 0.4));
      const palette = M.types.map(previewColor), cache = new Map();
      const heightCodes = ["deep_ocean", "close_ocean", "shallow_waters", "sand", "soil_low:grass_low", "soil_high:grass_high", "hills", "mountains",
        "hills:snow_hills", "mountains:snow_block"].map(code);
      for (let y = 0; y < rows; y++)
        for (let x = 0; x < cols; x++) {
          const sxp = Math.min(width - 1, x * shrink), syp = Math.min(height - 1, y * shrink);
          const at = ((height - 1 - syp) * width + sxp) * 4, r = data[at], g = data[at + 1], b = data[at + 2];
          let found;
          if (byHeight) {
            const value = (r * 0.299 + g * 0.587 + b * 0.114) / 255;
            if (value < level) { const depth = (level - value) / level; found = heightCodes[depth > 0.35 ? 0 : depth > 0.08 ? 1 : 2]; }
            else {
              const rise = (value - level) / Math.max(0.02, 1 - level);
              found = heightCodes[rise < 0.04 ? 3 : rise < 0.35 ? 4 : rise < 0.55 ? 5 : rise < 0.7 ? 6 : rise < 0.85 ? 7 : rise < 0.92 ? 8 : 9];
            }
          } else {
            const rgb = (r << 16) | (g << 8) | b;
            found = cache.get(rgb);
            if (found === undefined) {
              let best = Infinity;
              found = 0;
              for (let i = 0; i < palette.length && i < 256; i++) {
                const dr = palette[i][0] - r, dg = palette[i][1] - g, db = palette[i][2] - b, distance = dr * dr + dg * dg + db * db;
                if (distance < best) { best = distance; found = i; }
              }
              cache.set(rgb, found);
            }
          }
          codes[y * cols + x] = found;
        }
    }
    // 三档大小：一像素合 1、1.5、2 格，宽按区块取整，高随宽的比例
    const sizes = IMPORT_SCALES.map(scale => {
      const x = Math.min(48, Math.max(4, Math.ceil(cols * scale / 64)));
      return [x, Math.min(48, Math.max(4, Math.ceil(rows * x / cols)))];
    });
    const info = { key, cols, rows, sizes, blank: !image };
    useGrid(makeGrid(cols, rows, codes, 0, 0, info));
    return info;
  }

  // 颜色对照表(map_colors.txt，同 WorldMapImporter.WriteLegend)
  function legendText() {
    const lines = ["导入地图颜色对照表：map.png 按最接近的颜色识别地形，灰度高程图无需对照。", "R,G,B\t地形"];
    for (const type of M.types) lines.push(previewColor(type).join(",") + "\t" + type);
    return lines.join("\r\n") + "\r\n";
  }

  // rivers.bytes：ECRIVER1、条数，每条 等级(1 字节)、点数(2 字节)、各点经纬度×100(各 2 字节)
  function loadRivers(bytes) {
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    if (new TextDecoder().decode(bytes.subarray(0, 8)) !== "ECRIVER1") return;
    const count = view.getInt32(8, true);
    let at = 12;
    M.rivers = [];
    for (let i = 0; i < count; i++) {
      const rank = bytes[at], points = view.getUint16(at + 1, true);
      at += 3;
      const line = new Float32Array(points * 2);
      for (let p = 0; p < points; p++) {
        line[p * 2] = view.getInt16(at, true) / 100;
        line[p * 2 + 1] = view.getInt16(at + 2, true) / 100;
        at += 4;
      }
      M.rivers.push({ rank, line });
    }
  }

  // 一个像素(x 自西向东，y 自南向北)换成某种地表；type 为空复原底图
  function setTexel(x, y, type) {
    const index = y * M.w + x;
    let color, cls;
    if (!type) { color = baseColor[index]; cls = baseClass[index]; workCode[index] = baseCode[index]; }
    else {
      const rgb = previewColor(type);
      color = 0xFF000000 | (rgb[2] << 16) | (rgb[1] << 8) | rgb[0];
      cls = classOf(type);
      // 剧本刷的写法不在地表数据的写法表里(旧剧本)：接在表尾
      let code = M.types.indexOf(type);
      if (code < 0) { M.types.push(type); code = M.types.length - 1; }
      workCode[index] = code;
    }
    workClass[index] = cls;
    new Uint32Array(terrainImage.data.buffer)[(M.h - 1 - y) * M.w + x] = color;
  }

  // 底图叠上刷的地形，整张重画(打开、撤销时；拖着刷时只改刷到的像素，见 paintTexels)
  function applyTerrain() {
    const pixels = new Uint32Array(terrainImage.data.buffer);
    for (let y = 0; y < M.h; y++) pixels.set(baseColor.subarray(y * M.w, (y + 1) * M.w), (M.h - 1 - y) * M.w);
    workClass.set(baseClass);
    workCode.set(baseCode);
    for (const [cell, type] of D.S.terrain) {
      const [i, j] = D.unpack(cell);
      const x = i - M.offX, y = j - M.offY;
      if (x >= 0 && y >= 0 && x < M.w && y < M.h) setTexel(x, y, type);
    }
    terrainCtx.putImageData(terrainImage, 0, 0);
    appliedTerrainRevision = D.S.terrainRevision;
    M.fillRevision = -1;
  }

  // 刷到的那几格地形换色(cells 为刷地形的格子编号)
  function paintTexels(cells) {
    let x0 = M.w, y0 = M.h, x1 = -1, y1 = -1;
    for (const cell of cells) {
      const [i, j] = D.unpack(cell);
      const x = i - M.offX, y = j - M.offY;
      if (x < 0 || y < 0 || x >= M.w || y >= M.h) continue;
      setTexel(x, y, D.S.terrain.get(cell) || null);
      const cy = M.h - 1 - y;
      if (x < x0) x0 = x; if (x > x1) x1 = x; if (cy < y0) y0 = cy; if (cy > y1) y1 = cy;
    }
    if (x1 >= x0) terrainCtx.putImageData(terrainImage, 0, 0, x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    appliedTerrainRevision = D.S.terrainRevision;
    M.fillRevision = -1;
  }

  // 油桶：从这一点出发、四向连着的同一种颜色的像素(同游戏的 BucketAt)；超过 limit 格返回 null(先用笔刷隔开再倒)。
  // 返回刷地形的格子编号与这一片现在的颜色
  function floodSame(lon, lat, limit) {
    const sx0 = Math.floor(mxOf(lon)), sy0 = Math.floor((lat - M.latS) / D.TERRAIN_STEP);
    if (sx0 < 0 || sy0 < 0 || sx0 >= M.w || sy0 >= M.h) return { cells: [], color: 0 };
    const pixels = new Uint32Array(terrainImage.data.buffer), w = M.w, h = M.h;
    const at = (x, y) => pixels[(h - 1 - y) * w + x];
    const from = at(sx0, sy0), seen = new Uint8Array(w * h), queue = [sy0 * w + sx0], cells = [];
    seen[queue[0]] = 1;
    for (let head = 0; head < queue.length; head++) {
      const index = queue[head], x = index % w, y = (index - x) / w;
      cells.push(D.pack(x + M.offX, y + M.offY));
      if (cells.length > limit) return null;
      if (x + 1 < w && !seen[index + 1] && at(x + 1, y) === from) { seen[index + 1] = 1; queue.push(index + 1); }
      if (x > 0 && !seen[index - 1] && at(x - 1, y) === from) { seen[index - 1] = 1; queue.push(index - 1); }
      if (y + 1 < h && !seen[index + w] && at(x, y + 1) === from) { seen[index + w] = 1; queue.push(index + w); }
      if (y > 0 && !seen[index - w] && at(x, y - 1) === from) { seen[index - w] = 1; queue.push(index - w); }
    }
    return { cells, color: from };
  }

  // ---------- 坐标 ----------
  const lonOf = mx => M.lonW + mx * D.TERRAIN_STEP;
  const latOf = my => M.latN - my * D.TERRAIN_STEP;
  const mxOf = lon => (lon - M.lonW) / D.TERRAIN_STEP;
  const myOf = lat => (M.latN - lat) / D.TERRAIN_STEP;
  const sx = lon => mxOf(lon) * M.scale + M.ox;
  const sy = lat => myOf(lat) * M.scale + M.oy;
  const screenToLonLat = (x, y) => [lonOf((x - M.ox) / M.scale), latOf((y - M.oy) / M.scale)];

  function inMap(lon, lat) {
    const x = Math.floor(mxOf(lon)), y = Math.floor((lat - M.latS) / D.TERRAIN_STEP);
    return x >= 0 && y >= 0 && x < M.w && y < M.h;
  }

  function isLand(lon, lat) {
    const x = Math.floor(mxOf(lon)), y = Math.floor((lat - M.latS) / D.TERRAIN_STEP);
    return x >= 0 && y >= 0 && x < M.w && y < M.h && workClass[y * M.w + x] !== 0;
  }

  // 这里现在是什么地表(刷过的按刷的)
  function typeAt(lon, lat) {
    const painted = D.S.terrain.get(D.cellKey(lon, lat, D.TERRAIN_STEP));
    if (painted) return painted;
    const x = Math.floor(mxOf(lon)), y = Math.floor((lat - M.latS) / D.TERRAIN_STEP);
    return x >= 0 && y >= 0 && x < M.w && y < M.h ? M.types[baseCode[y * M.w + x]] : "deep_ocean";
  }

  function fit() {
    if (!canvas || !M.ready) return;
    const frame = frameBounds();
    const [x0, y0, x1, y1] = frame ? [mxOf(frame.west), myOf(frame.north), mxOf(frame.east), myOf(frame.south)] : [0, 0, M.w, M.h];
    M.scale = Math.min(canvas.clientWidth / (x1 - x0), canvas.clientHeight / (y1 - y0)) * 0.96;
    M.ox = (canvas.clientWidth - (x0 + x1) * M.scale) / 2;
    M.oy = (canvas.clientHeight - (y0 + y1) * M.scale) / 2;
    redraw();
  }

  function zoomAt(x, y, factor) {
    const next = Math.min(40, Math.max(0.15, M.scale * factor));
    M.ox = x - (x - M.ox) * next / M.scale;
    M.oy = y - (y - M.oy) * next / M.scale;
    M.scale = next;
    redraw();
  }

  function pan(dx, dy) { M.ox += dx; M.oy += dy; redraw(); }

  function centerOn(lon, lat) {
    M.ox = canvas.clientWidth / 2 - mxOf(lon) * M.scale;
    M.oy = canvas.clientHeight / 2 - myOf(lat) * M.scale;
    redraw();
  }

  // 地图范围(剧本定了范围时画出框)：纬度跨度按格子方正由大小算出，没定大小按中档
  function frameBounds() {
    const era = D.S.era;
    if (era?.map) return null;
    const frame = era?.frame ? D.FRAMES.find(item => item.id === era.frame) : null;
    if (!frame) return null;
    const size = Array.isArray(era.size) && era.size[0] > 0 && era.size[1] > 0 ? era.size : frame.sizes[1];
    const degrees = (frame.east - frame.west) / (size[0] * 64), span = size[1] * 64 * degrees;
    const north = frame.top !== undefined ? frame.top : frame.centerLat + span / 2;
    return { west: frame.west, east: frame.east, north, south: north - span, degrees, size };
  }

  // ---------- 图层颜色 ----------
  // 城在这个图层算哪一块：国家 p:、独立的城 i:、王国法理 r:、帝国法理 k:、天下 t:、行政区 d:国家:下标(京畿 j:国家)
  function keyOfCity(item, layer) {
    const owner = D.ownerOf(item);
    switch (layer) {
      case "realm": return owner ? "p:" + owner.id : "i:" + item.id;
      case "kingdom": return item.r ? "r:" + item.r : null;
      case "empire": { const core = D.coreOfRegion(item.r); return core ? "k:" + core.id : null; }
      case "tianxia": { const core = D.coreOfRegion(item.r); return core ? "t:" + (core.tx || "~" + core.id) : null; }
      case "division": {
        if (!owner) return "i:" + item.id;
        const index = D.divisionOf(owner, item.id);
        return index >= 0 ? `d:${owner.id}:${index}` : "j:" + owner.id;
      }
    }
    return null;
  }

  // 刷的地块在这个图层算哪一块(p: 那国都城那一块；r: 那块王国法理；c: 那座城那一块)
  function keyOfPaint(value, layer) {
    if (value.startsWith("c:")) { const item = D.city(value.slice(2)); return item ? keyOfCity(item, layer) : null; }
    if (value.startsWith("p:")) {
      const owner = D.polity(value.slice(2));
      if (!owner) return null;
      if (layer === "realm") return "p:" + owner.id;
      if (layer === "division") return "j:" + owner.id;
      const seat = D.city(owner.cap);
      return seat ? keyOfCity(seat, layer) : null;
    }
    if (value.startsWith("r:")) return keyOfRegion(value.slice(2), layer);
    return null;
  }

  const keyOfRegion = (id, layer) => {
    if (layer === "kingdom") return "r:" + id;
    const core = D.coreOfRegion(id);
    if (layer === "empire") return core ? "k:" + core.id : null;
    if (layer === "tianxia") return core ? "t:" + (core.tx || "~" + core.id) : null;
    return null;
  };

  // 一片无人区在这个图层算哪一块：国界、行政区看它归谁，法理各层看它挂的王国法理；中立、补空小国的不上色只打斜线
  function keyOfPiece(piece, layer) {
    if (piece.neutral) return null;
    if (layer === "realm" || layer === "division") {
      if (!piece.owner || piece.owner === "f") return null;
      if (piece.owner.startsWith("i:")) return piece.owner;
      return (layer === "realm" ? "p:" : "j:") + piece.owner;
    }
    return piece.region ? keyOfRegion(piece.region, layer) : null;
  }

  const fillClass = (fx, fy) => workClass[Math.min(M.h - 1, fy * FILL) * M.w + Math.min(M.w - 1, fx * FILL)];
  const fillX = lon => Math.floor(lon / D.LAND_STEP) - M.offX / FILL;
  const fillY = lat => Math.floor(lat / D.LAND_STEP) - M.offY / FILL;

  // 照开局预演一遍(scenario-preview.js)。只看剧本、地形、大小档，换图层不用重算
  function runPreview() {
    const key = `${D.S.revision}|${D.S.terrainRevision}|${M.sizeIndex}|${M.gridStamp}`;
    if (M.previewKey === key) return M.preview;
    M.previewKey = key;
    M.preview = null;
    M.previewError = "";
    if (!window.SPV) { M.previewError = "预演模块没载入"; return null; }
    try {
      const started = performance.now();
      M.preview = window.SPV.compute({
        types: M.types, codes: workCode, w: M.w, h: M.h, lon0: current.lon0, lat0: current.lat0, step: D.TERRAIN_STEP,
        imported: M.imported ? { cols: M.imported.cols, rows: M.imported.rows, sizes: M.imported.sizes } : null,
        era: D.S.era, cities: D.cities(), regions: D.regions(), cores: D.cores(), land: D.S.land, develop: D.S.develop,
        rivers: M.imported ? [] : M.rivers, development: D.S.development || null, units: M.units.length ? M.units : null,
        boundaries: D.S.boundaries || [], custom: true, presets: D.S.presetIds || undefined,
        sizeIndex: M.sizeIndex, settings: { wilderness: true, jungleDeveloped: false }
      });
      M.previewMs = performance.now() - started;
      M.unplaced = new Map((M.preview.unplaced || []).map(item => [item.id, item.reason]));
      M.crowded = new Set(M.preview.crowded || []);
    } catch (error) {
      console.warn("开局预演算不出来，图层颜色退回近似", error);
      M.previewError = error.message;
      M.preview = null;
    }
    if (!M.preview) { M.unplaced = new Map(); M.crowded = new Set(); }
    return M.preview;
  }

  // 重铺图层颜色
  function computeFill() {
    const fw = M.fw, fh = M.fh;
    M.fillOwner.fill(-1);
    M.fillKeys = [];
    M.hatch = new Uint8Array(fw * fh);
    M.fillZone = null;
    M.fillRevision = D.S.revision;
    M.areas = [];
    if (!D.S.era || M.layer === "terrain") return;
    if (runPreview()) fillFromPreview(); else computeFillApprox();
  }

  // 各粗格(0.2°)落在预演的哪块地上：那块地归的城、挂的法理在当前图层算哪一块。
  // hatch：1 无人区(照归属上色加斜线)、2 中立(灰斜线)、3 补空小国(淡黄斜线)。
  // 平滑画法：粗格不直接取所在的那块地，按周围四块地的远近投票(同类的票加在一起，同游戏的 Vote)，国界就不是一格格的台阶
  function fillFromPreview() {
    const pv = M.preview, layer = M.layer, fw = M.fw, fh = M.fh, owner = M.fillOwner, hatch = M.hatch;
    const slots = new Map();
    const slotOf = key => {
      if (!key) return -1;
      if (!slots.has(key)) { slots.set(key, M.fillKeys.length); M.fillKeys.push(key); }
      return slots.get(key);
    };
    const seatCity = pv.seats.map(id => D.city(id));
    const seatSlot = seatCity.map(item => item && layer !== "wild" ? slotOf(keyOfCity(item, layer)) : -1);
    const pieceSlot = pv.pieces.map(piece => layer !== "wild" ? slotOf(keyOfPiece(piece, layer)) : -1);
    // 预演按块给了归属(zoneHolder："p:国编号"、"c:独立的城"、"f" 补空小国)和挂的法理(zoneRegion)的，照块上色，比整片取多数准
    const byZone = Array.isArray(pv.zoneHolder) && Array.isArray(pv.zoneRegion) && layer !== "wild";
    const wildSlots = new Map();
    const wildSlot = zone => {
      const holder = pv.zoneHolder[zone], region = pv.zoneRegion[zone];
      const id = layer === "realm" || layer === "division" ? holder : region;
      if (!id || id === "f") return -1;
      if (wildSlots.has(id)) return wildSlots.get(id);
      let key = null;
      if (layer === "realm" || layer === "division") key = id.startsWith("c:") ? "i:" + id.slice(2) : id.startsWith("p:") ? (layer === "realm" ? id : "j:" + id.slice(2)) : null;
      else key = keyOfRegion(id, layer);
      const slot = slotOf(key);
      wildSlots.set(id, slot);
      return slot;
    };
    // 每块地的标签：低 20 位是图层里的哪一块(+1，0 为没有)，再往上是斜线种类
    const zones = pv.cols * pv.rows, label = new Int32Array(zones);
    for (let zone = 0; zone < zones; zone++) {
      if (pv.zoneWild[zone]) {
        const piece = pv.zonePiece[zone];
        const neutral = byZone ? !!pv.zoneNeutral?.[zone] : piece >= 0 && pv.pieces[piece].neutral;
        const slot = neutral ? -1 : byZone ? wildSlot(zone) : piece >= 0 ? pieceSlot[piece] : -1;
        // 归了国(或挂了法理)的照它上色加斜线；补空小国的淡黄斜线；中立、没主的灰斜线
        const filler = !neutral && slot < 0 && (byZone ? pv.zoneHolder[zone] === "f" : piece >= 0 && pv.pieces[piece].owner === "f") && (layer === "realm" || layer === "division");
        label[zone] = (slot + 1) | ((slot >= 0 ? 1 : filler ? 3 : 2) << 20);
      } else {
        const seat = pv.zoneOwner[zone];
        if (seat >= 0) label[zone] = seatSlot[seat] + 1;
        else if (seat === -2) label[zone] = 3 << 20;
      }
    }
    const zoneOf = M.fillZone = new Int32Array(fw * fh).fill(-1);
    const half = D.LAND_STEP / 2, smooth = M.smooth && layer !== "wild";
    for (let fy = 0; fy < fh; fy++) {
      const v = ((fy + M.offY / FILL) * D.LAND_STEP + half - pv.bottom) / pv.zoneDegrees, zy = Math.floor(v);
      if (zy < 0 || zy >= pv.rows) continue;
      for (let fx = 0; fx < fw; fx++) {
        const u = ((fx + M.offX / FILL) * D.LAND_STEP + half - pv.west) / pv.zoneDegrees, zx = Math.floor(u);
        if (zx < 0 || zx >= pv.cols) continue;
        const index = fy * fw + fx, zone = zy * pv.cols + zx;
        zoneOf[index] = zone;
        let chosen = label[zone];
        if (smooth) {
          // 周围四块地(中心离得最近的)按远近投票
          const x0 = Math.floor(u - 0.5), y0 = Math.floor(v - 0.5), tx = u - 0.5 - x0, ty = v - 0.5 - y0;
          let best = 0, bestLabel = chosen;
          const tally = [];
          for (let k = 0; k < 4; k++) {
            const x = x0 + (k & 1), y = y0 + (k >> 1);
            if (x < 0 || y < 0 || x >= pv.cols || y >= pv.rows) continue;
            const weight = ((k & 1) ? tx : 1 - tx) * ((k >> 1) ? ty : 1 - ty), value = label[y * pv.cols + x];
            let entry = tally.find(item => item[0] === value);
            if (!entry) { entry = [value, 0]; tally.push(entry); }
            entry[1] += weight;
            if (entry[1] > best) { best = entry[1]; bestLabel = value; }
          }
          chosen = bestLabel;
        }
        owner[index] = (chosen & 0xFFFFF) - 1;
        hatch[index] = chosen >> 20;
      }
    }
    // 各块的名字写在辖下城的正中
    const sums = new Map();
    seatCity.forEach((item, seat) => {
      if (!item || seatSlot[seat] < 0) return;
      const key = M.fillKeys[seatSlot[seat]], sum = sums.get(key) || [0, 0, 0];
      sum[0] += item.x; sum[1] += item.y; sum[2]++;
      sums.set(key, sum);
    });
    M.areas = [...sums].map(([key, sum]) => ({ key, lon: sum[0] / sum[2], lat: sum[1] / sum[2], count: sum[2] }));
  }

  // 近似(预演模块用不了时的退路)：城心、刷的地块先定，再一起往外铺(八方向，斜走贵四成)；照原图分地的剧本城不往外铺
  function computeFillApprox() {
    const era = D.S.era, layer = M.layer, fw = M.fw, fh = M.fh, owner = M.fillOwner;
    owner.fill(-1);
    M.fillKeys = [];
    M.areas = [];
    for (const [cell, value] of D.S.land) {
      if (value !== "n") continue;
      const [i, j] = D.unpack(cell);
      const fx = i - M.offX / FILL, fy = j - M.offY / FILL;
      if (fx >= 0 && fy >= 0 && fx < fw && fy < fh) M.hatch[fy * fw + fx] = 2;
    }
    if (layer === "wild") return;
    const slots = new Map();
    const slotOf = key => {
      if (!slots.has(key)) { slots.set(key, M.fillKeys.length); M.fillKeys.push(key); }
      return slots.get(key);
    };
    const dist = new Float32Array(fw * fh).fill(Infinity);
    const heap = [];
    const push = (d, index) => {
      heap.push([d, index]);
      let i = heap.length - 1;
      while (i > 0) { const p = (i - 1) >> 1; if (heap[p][0] <= heap[i][0]) break; [heap[p], heap[i]] = [heap[i], heap[p]]; i = p; }
    };
    const pop = () => {
      const top = heap[0], last = heap.pop();
      if (heap.length > 0) {
        heap[0] = last;
        let i = 0;
        for (;;) {
          const l = i * 2 + 1, r = l + 1;
          let m = i;
          if (l < heap.length && heap[l][0] < heap[m][0]) m = l;
          if (r < heap.length && heap[r][0] < heap[m][0]) m = r;
          if (m === i) break;
          [heap[m], heap[i]] = [heap[i], heap[m]];
          i = m;
        }
      }
      return top;
    };
    const seed = (fx, fy, key) => {
      if (fx < 0 || fy < 0 || fx >= fw || fy >= fh || !key) return;
      const index = fy * fw + fx;
      if (dist[index] === 0) return;
      dist[index] = 0;
      owner[index] = slotOf(key);
      push(0, index);
    };
    for (const [cell, value] of D.S.land) {
      if (value === "n") continue;
      const [i, j] = D.unpack(cell);
      seed(i - M.offX / FILL, j - M.offY / FILL, keyOfPaint(value, layer));
    }
    const sums = new Map();
    for (const item of D.cities()) {
      if (!D.active(item)) continue;
      const key = keyOfCity(item, layer);
      if (!key) continue;
      let fx = fillX(item.x), fy = fillY(item.y);
      // 城点落在水里(海边、岛上)：挪到旁边最近的陆地格再铺
      if (fx >= 0 && fy >= 0 && fx < fw && fy < fh && fillClass(fx, fy) === 0) {
        let found = null;
        for (let r = 1; r <= 3 && !found; r++)
          for (let dy = -r; dy <= r && !found; dy++)
            for (let dx = -r; dx <= r && !found; dx++) {
              const x = fx + dx, y = fy + dy;
              if (x >= 0 && y >= 0 && x < fw && y < fh && fillClass(x, y) !== 0) found = [x, y];
            }
        if (found) [fx, fy] = found;
      }
      seed(fx, fy, key);
      const sum = sums.get(key) || [0, 0, 0];
      sum[0] += item.x; sum[1] += item.y; sum[2]++;
      sums.set(key, sum);
    }
    M.areas = [...sums].map(([key, sum]) => ({ key, lon: sum[0] / sum[2], lat: sum[1] / sum[2], count: sum[2] }));
    if (era.exact) return;
    const DX = [1, -1, 0, 0, 1, 1, -1, -1], DY = [0, 0, 1, -1, 1, -1, 1, -1];
    while (heap.length > 0) {
      const [d, index] = pop();
      if (d > dist[index]) continue;
      const x = index % fw, y = (index - x) / fw;
      for (let k = 0; k < 8; k++) {
        const nx = x + DX[k], ny = y + DY[k];
        if (nx < 0 || ny < 0 || nx >= fw || ny >= fh) continue;
        const cost = CLASS_COST[fillClass(nx, ny)];
        if (cost === 0) continue;
        const next = d + (k < 4 ? cost : cost * 1.4), target = ny * fw + nx;
        if (next >= dist[target] || next > REACH) continue;
        dist[target] = next;
        owner[target] = owner[index];
        push(next, target);
      }
    }
  }

  // 无人区各类型的颜色(图层上)
  const KIND_COLORS = { mountain: [120, 110, 125], desert: [225, 180, 90], tundra: [150, 190, 220], jungle: [30, 110, 60], swamp: [70, 100, 110],
    lava: [200, 70, 40], plateau: [150, 130, 95] };

  function drawFill() {
    const fw = M.fw, fh = M.fh, owner = M.fillOwner, hatch = M.hatch, pixels = new Uint32Array(fillImage.data.buffer);
    pixels.fill(0);
    if (M.layer === "terrain") { fillCtx.putImageData(fillImage, 0, 0); return; }
    const colors = M.fillKeys.map(key => D.colorOfKey(key));
    const high = M.fillKeys.indexOf(M.highlight);
    const rgba = (color, alpha) => ((alpha << 24) | (color[2] << 16) | (color[1] << 8) | color[0]) >>> 0;
    const pv = M.preview, zoneOf = M.fillZone;
    for (let y = 0; y < fh; y++)
      for (let x = 0; x < fw; x++) {
        const index = y * fw + x, slot = owner[index], out = (fh - 1 - y) * fw + x, mark = hatch[index];
        if (fillClass(x, y) === 0) continue;
        const stripe = (x + y) % 3 === 0;
        if (M.layer === "wild") {
          if (!pv || !zoneOf) { pixels[out] = mark === 2 ? (stripe ? 0xD0464646 : 0x50969696) : wildTexel(x, y); continue; }
          const zone = zoneOf[index], kind = zone >= 0 ? pv.zoneWild[zone] : 0;
          if (!kind) continue;
          const piece = pv.pieces[pv.zonePiece[zone]];
          // 开发度高的淡些，永久中立、没主的打灰斜线
          const fade = piece && piece.develop > 0 ? 1 - Math.min(100, piece.develop) / 140 : 1;
          const color = KIND_COLORS[pv.kinds[kind]] || D.colorOfKey("wild:" + pv.kinds[kind]);
          pixels[out] = piece && (piece.neutral || !piece.owner) && stripe ? 0xE0464646 : rgba(color, Math.round(((x + y) % 4 === 0 ? 0xE0 : 0xA8) * fade));
          continue;
        }
        if (mark === 2) { pixels[out] = stripe ? 0xD0464646 : 0x50969696; continue; }
        if (mark === 3) { pixels[out] = stripe ? rgba([215, 195, 130], 150) : rgba([215, 195, 130], 40); continue; }
        if (slot < 0) continue;
        const edge = x + 1 < fw && owner[index + 1] >= 0 && owner[index + 1] !== slot || y + 1 < fh && owner[index + fw] >= 0 && owner[index + fw] !== slot;
        if (edge) { pixels[out] = 0xE0141414; continue; }
        const color = colors[slot], alpha = slot === high ? 0xD8 : 0x8C;
        // 无人区：归属的颜色淡一些，打深色斜线
        pixels[out] = mark === 1 ? ((x + y) % 4 === 0 ? rgba([30, 30, 30], 0x78) : rgba(color, slot === high ? 0xB0 : 0x60)) : rgba(color, alpha);
      }
    fillCtx.putImageData(fillImage, 0, 0);
  }

  // 这一点落在预演的哪块地上：是不是无人区、哪一片、开局归哪座城(给悬停提示)
  function zoneInfoAt(lon, lat) {
    const pv = M.preview;
    if (!pv) return null;
    const zx = Math.floor((lon - pv.west) / pv.zoneDegrees), zy = Math.floor((lat - pv.bottom) / pv.zoneDegrees);
    if (zx < 0 || zy < 0 || zx >= pv.cols || zy >= pv.rows) return { outside: true };
    const zone = zy * pv.cols + zx, kind = pv.zoneWild[zone], seat = pv.zoneOwner[zone];
    return { kind: kind ? pv.kinds[kind] : "", piece: kind ? pv.pieces[pv.zonePiece[zone]] : null, seat: seat >= 0 ? pv.seats[seat] : null,
      filler: seat === -2, painted: !!pv.zonePainted?.[zone] };
  }

  // 无人区图层(粗略示意，不是开局的预测)：荒漠冻土、山地雪山、雨林大泽按类型上色打斜线；刷了开发度的按数淡下去
  function wildTexel(x, y) {
    const cls = fillClass(x, y);
    if (cls === 0) return 0;
    const type = typeAt(M.lonW + (x * FILL + 0.5) * D.TERRAIN_STEP, M.latS + (y * FILL + 0.5) * D.TERRAIN_STEP);
    let color = null;
    if (cls === 6) color = type.includes("permafrost") ? [150, 190, 220] : [225, 180, 90];
    else if (cls === 4 || cls === 5 || cls === 7) color = [120, 110, 120];
    else if (type.includes("jungle")) color = [30, 110, 60];
    else if (type.includes("swamp")) color = [70, 100, 110];
    if (!color) return 0;
    const dev = D.S.develop.get(D.pack(x + M.offX / FILL, y + M.offY / FILL));
    const fade = dev === undefined ? 1 : 1 - Math.min(100, Number(dev)) / 100;
    if (fade <= 0) return 0;
    const alpha = Math.round(((x + y) % 4 === 0 ? 0xE0 : 0x90) * fade);
    return (alpha << 24) | (color[2] << 16) | (color[1] << 8) | color[0];
  }

  // ---------- 画 ----------
  // 底图、图层颜色跟上剧本的改动。拖着刷时图层颜色至多四分之一秒重铺一次(铺一遍要走遍全图)，force 为真时不等
  function prepare(force) {
    if (appliedTerrainRevision !== D.S.terrainRevision) applyTerrain();
    const stale = M.fillRevision !== D.S.revision || M.fillLayer !== M.layer;
    if (stale && (force || !M.throttle || performance.now() - (M.fillAt || 0) > Math.max(250, (M.previewMs || 0) * 2.5))) {
      computeFill();
      M.fillLayer = M.layer;
      M.fillDrawn = null;
      M.fillAt = performance.now();
    } else if (stale) setTimeout(redraw, 130);
    const drawnKey = M.layer + "|" + M.highlight + "|" + M.fillRevision + "|" + M.smooth;
    if (M.fillDrawn !== drawnKey) { drawFill(); M.fillDrawn = drawnKey; }
  }

  function redraw() {
    if (needsDraw) return;
    needsDraw = true;
    requestAnimationFrame(() => { needsDraw = false; draw(); });
  }

  function resize() {
    if (!canvas) return;
    const ratio = window.devicePixelRatio || 1;
    canvas.width = Math.max(1, Math.round(canvas.clientWidth * ratio));
    canvas.height = Math.max(1, Math.round(canvas.clientHeight * ratio));
    redraw();
  }

  function draw() {
    if (!canvas || !M.ready) return;
    const ratio = window.devicePixelRatio || 1, era = D.S.era;
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
    ctx.fillStyle = "#0a0d0a";
    ctx.fillRect(0, 0, canvas.clientWidth, canvas.clientHeight);
    if (!era) return;
    prepare(false);
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(terrainCanvas, M.ox, M.oy, M.w * M.scale, M.h * M.scale);
    if (M.layer !== "terrain") ctx.drawImage(fillCanvas, M.ox, M.oy, M.fw * FILL * M.scale, M.fh * FILL * M.scale);
    drawRivers();
    drawFrame();
    if (M.layer === "wild") { if (M.preview) drawPieces(); else if (!M.imported) drawUnits(); }
    drawWalls();
    drawTribes();
    drawAreas();
    drawCities();
    if (overlayDraw) overlayDraw(ctx);
  }

  // 大河(只在地形图层和放大后画细线，帮着认地方)
  function drawRivers() {
    if (M.scale < 1.2 || M.rivers.length === 0 || M.imported) return;
    ctx.strokeStyle = "rgba(80,170,240,.85)";
    ctx.lineWidth = Math.min(2.5, Math.max(1, M.scale * 0.25));
    ctx.beginPath();
    const w = canvas.clientWidth, h = canvas.clientHeight;
    for (const river of M.rivers) {
      const line = river.line;
      let drawing = false;
      for (let p = 0; p < line.length; p += 2) {
        const x = sx(line[p]), y = sy(line[p + 1]);
        const visible = x > -200 && y > -200 && x < w + 200 && y < h + 200;
        if (!visible) { drawing = false; continue; }
        if (drawing) ctx.lineTo(x, y); else { ctx.moveTo(x, y); drawing = true; }
      }
    }
    ctx.stroke();
  }

  function drawFrame() {
    const frame = frameBounds();
    if (!frame) return;
    ctx.strokeStyle = "rgba(255,236,160,.95)";
    ctx.lineWidth = 2;
    ctx.setLineDash([8, 5]);
    ctx.strokeRect(sx(frame.west), sy(frame.north), (frame.east - frame.west) / D.TERRAIN_STEP * M.scale, (frame.north - frame.south) / D.TERRAIN_STEP * M.scale);
    ctx.setLineDash([]);
  }

  // 无人区的地理单元(wilderness.json：起名用的框；剧本写了史实归属的标上归哪国)
  function drawUnits() {
    if (M.scale < 1.5) return;
    const era = D.S.era;
    ctx.font = "12px 'Fusion Pixel 12px Proportional SC','Microsoft YaHei',sans-serif";
    ctx.textAlign = "center";
    for (const unit of M.units) {
      const box = unit.box?.[0];
      if (!box) continue;
      const x = sx((box[0] + box[2]) / 2), y = sy((box[1] + box[3]) / 2);
      if (x < -50 || y < -20 || x > canvas.clientWidth + 50 || y > canvas.clientHeight + 20) continue;
      const owner = D.polity(era.wilderness?.[unit.id]);
      label(unit.zh + (owner ? `(${D.nameOf(owner)})` : ""), x, y, owner ? "#ffe9a8" : "#cfd6d0");
    }
  }

  // 无人区图层写类型名：至少六块的那几片(同游戏)，有地理单元名的写地名
  const KIND_NAMES = { mountain: "山脉", desert: "沙漠", tundra: "冻原", jungle: "雨林", swamp: "大泽", lava: "熔岩", plateau: "高原" };

  const kindKey = kind => typeof kind === "number" ? (M.preview?.kinds[kind] || "") : String(kind || "");
  const kindLabel = kind => KIND_NAMES[kindKey(kind)] || kindKey(kind);

  function drawPieces() {
    const pv = M.preview;
    if (M.scale < 1.2) return;
    ctx.font = "12px 'Fusion Pixel 12px Proportional SC','Microsoft YaHei',sans-serif";
    ctx.textAlign = "center";
    const names = new Map(M.units.map(unit => [unit.id, unit.zh]));
    for (const piece of pv.pieces) {
      if (piece.zones < (M.scale < 3 ? 12 : 6)) continue;
      const x = sx(piece.lon), y = sy(piece.lat);
      if (x < -50 || y < -20 || x > canvas.clientWidth + 50 || y > canvas.clientHeight + 20) continue;
      const kind = kindLabel(piece.kind);
      label((names.get(piece.unit) || kind) + (piece.neutral ? "(中立)" : ""), x, y, piece.neutral || !piece.owner ? "#cfd6d0" : "#ffe9a8");
    }
  }

  function label(text, x, y, color) {
    ctx.lineWidth = 3;
    ctx.strokeStyle = "rgba(0,0,0,.8)";
    ctx.strokeText(text, x, y);
    ctx.fillStyle = color;
    ctx.fillText(text, x, y);
  }

  function drawWalls() {
    const era = D.S.era;
    ctx.lineJoin = "round";
    (era.walls || []).forEach((wall, index) => {
      const points = wall.pts || [];
      if (points.length === 0) return;
      const selected = M.selection?.wall === index;
      ctx.strokeStyle = selected ? "#ffd65a" : "#78462a";
      ctx.lineWidth = selected ? 4 : 3;
      ctx.beginPath();
      points.forEach((point, i) => { if (i === 0) ctx.moveTo(sx(point[0]), sy(point[1])); else ctx.lineTo(sx(point[0]), sy(point[1])); });
      ctx.stroke();
      if (selected) {
        ctx.fillStyle = "#ffd65a";
        for (const point of points) ctx.fillRect(sx(point[0]) - 3, sy(point[1]) - 3, 6, 6);
      }
    });
    (era.passes || []).forEach((pass, index) => {
      if (!pass.at) return;
      const x = sx(pass.at[0]), y = sy(pass.at[1]), selected = M.selection?.pass === index;
      ctx.fillStyle = "#faebb4";
      ctx.strokeStyle = selected ? "#ffd65a" : "#3a2a12";
      ctx.lineWidth = selected ? 3 : 1.5;
      ctx.beginPath();
      ctx.moveTo(x, y - 6); ctx.lineTo(x + 5, y + 4); ctx.lineTo(x - 5, y + 4); ctx.closePath();
      ctx.fill(); ctx.stroke();
      if (M.scale >= 5 || selected) {
        ctx.font = "12px 'Fusion Pixel 12px Proportional SC','Microsoft YaHei',sans-serif";
        ctx.textAlign = "center";
        label(D.first(pass.name), x, y - 9, "#faebb4");
      }
    });
  }

  function drawTribes() {
    (D.S.era.tribes || []).forEach((item, index) => {
      if (!item?.at) return;
      const selected = M.selection?.tribe === index;
      const color = D.colorOfKey("tribe:" + (item.culture || ""));
      ctx.beginPath();
      ctx.arc(sx(item.at[0]), sy(item.at[1]), item.radius / D.TERRAIN_STEP * M.scale, 0, Math.PI * 2);
      ctx.fillStyle = `rgba(${color[0]},${color[1]},${color[2]},.18)`;
      ctx.fill();
      ctx.strokeStyle = selected ? "#ffd65a" : `rgb(${color[0]},${color[1]},${color[2]})`;
      ctx.lineWidth = selected ? 3 : 2;
      ctx.setLineDash([6, 4]);
      ctx.stroke();
      ctx.setLineDash([]);
    });
  }

  // 各块的名字(国名、法理名、帝国法理名、天下名、行政区名)写在辖下城的正中
  function areaName(key) {
    const body = key.slice(2);
    switch (key[0]) {
      case "p": return D.nameOf(D.polity(body));
      case "r": return D.regionTitle(D.region(body));
      case "k": return D.coreName(D.core(body));
      case "t": return body.startsWith("~") ? D.coreName(D.core(body.slice(1))) : D.tianxiaName(body);
      case "j": return D.nameOf(D.polity(body)) + "京畿";
      case "d": { const cut = body.lastIndexOf(":"); return D.divisionName(D.polity(body.slice(0, cut)), Number(body.slice(cut + 1))); }
    }
    return "";
  }

  function drawAreas() {
    if (M.layer === "terrain" || M.layer === "wild" || !M.areas || M.scale < 1.1) return;
    ctx.textAlign = "center";
    const size = M.scale >= 4 ? 24 : 12;
    ctx.font = `${size}px 'Fusion Pixel 12px Proportional SC','Microsoft YaHei',sans-serif`;
    const minCount = M.scale < 2.2 ? 3 : 1;
    for (const area of M.areas) {
      if (area.key[0] === "i" || area.count < minCount) continue;
      const x = sx(area.lon), y = sy(area.lat);
      if (x < -60 || y < -30 || x > canvas.clientWidth + 60 || y > canvas.clientHeight + 30) continue;
      label(areaName(area.key), x, y - size * 0.9, area.key === M.highlight ? "#ffd65a" : "#fff2bf");
    }
  }

  function drawCities() {
    const era = D.S.era, sel = M.selection || {};
    const w = canvas.clientWidth, h = canvas.clientHeight;
    const showNames = M.scale >= 3.2;
    ctx.font = "12px 'Fusion Pixel 12px Proportional SC','Microsoft YaHei',sans-serif";
    ctx.textAlign = "center";
    const capitals = new Set(era.polities.map(item => item.cap));
    for (const item of D.cities()) {
      const x = sx(item.x), y = sy(item.y);
      if (x < -20 || y < -20 || x > w + 20 || y > h + 20) continue;
      const live = D.active(item);
      if (!live && !M.showInactive) continue;
      const owner = live ? D.ownerOf(item) : null;
      const capital = capitals.has(item.id) && owner?.cap === item.id;
      const rank = D.rankOf(item);
      const size = (capital ? 5.5 : rank <= 1 ? 4.5 : rank === 2 ? 3.5 : 2.6) * Math.min(1.6, Math.max(0.8, M.scale / 2.5));
      const color = !live ? "rgba(120,120,120,.55)" : owner ? `rgb(${D.colorOfKey("p:" + owner.id).join(",")})` : "#ebebeb";
      ctx.fillStyle = color;
      ctx.strokeStyle = "#141414";
      ctx.lineWidth = 1.5;
      if (capital) {
        ctx.beginPath();
        for (let i = 0; i < 10; i++) {
          const a = -Math.PI / 2 + i * Math.PI / 5, r = i % 2 === 0 ? size * 1.5 : size * 0.7;
          if (i === 0) ctx.moveTo(x + Math.cos(a) * r, y + Math.sin(a) * r); else ctx.lineTo(x + Math.cos(a) * r, y + Math.sin(a) * r);
        }
        ctx.closePath(); ctx.fill(); ctx.stroke();
      } else {
        ctx.fillRect(x - size, y - size, size * 2, size * 2);
        ctx.strokeRect(x - size, y - size, size * 2, size * 2);
      }
      // 法理首都的小标记：王国法理青、帝国法理紫
      if (live && (M.layer === "kingdom" || M.layer === "empire")) {
        const mark = M.layer === "kingdom" ? D.region(item.r)?.cap === item.id : D.coreOfRegion(item.r)?.cap === item.id;
        if (mark) {
          ctx.strokeStyle = M.layer === "kingdom" ? "#5ae6ff" : "#ff6ee6";
          ctx.lineWidth = 2;
          ctx.strokeRect(x - size - 3, y - size - 3, size * 2 + 6, size * 2 + 6);
        }
      }
      if (live && M.unplaced?.has(item.id)) {
        ctx.strokeStyle = "#eb4637";
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(x - size - 3, y - size - 3); ctx.lineTo(x + size + 3, y + size + 3);
        ctx.moveTo(x + size + 3, y - size - 3); ctx.lineTo(x - size - 3, y + size + 3);
        ctx.stroke();
      }
      if (sel.city === item.id) {
        ctx.strokeStyle = "#ffd65a";
        ctx.lineWidth = 2.5;
        ctx.beginPath(); ctx.arc(x, y, size + 6, 0, Math.PI * 2); ctx.stroke();
      }
      if (live && (showNames || sel.city === item.id || capital && M.scale >= 2)) label(D.cityName(item), x, y - size - 5, sel.city === item.id ? "#ffd65a" : "#f3efe2");
    }
  }

  // 屏幕上这一点最近的城(reach 像素以内；当年在的优先)
  function cityAt(x, y, reach = 10) {
    let best = null, bestDistance = reach * reach;
    for (const item of D.cities()) {
      const live = D.active(item);
      if (!live && !M.showInactive) continue;
      const dx = sx(item.x) - x, dy = sy(item.y) - y;
      const distance = (dx * dx + dy * dy) * (live ? 1 : 2.5);
      if (distance > bestDistance) continue;
      bestDistance = distance;
      best = item;
    }
    return best;
  }

  function passAt(x, y, reach = 9) {
    let found = -1, best = reach * reach;
    (D.S.era.passes || []).forEach((pass, index) => {
      if (!pass.at) return;
      const dx = sx(pass.at[0]) - x, dy = sy(pass.at[1]) - y, distance = dx * dx + dy * dy;
      if (distance > best) return;
      best = distance;
      found = index;
    });
    return found;
  }

  // 这一点落在图层的哪一块(铺出来的颜色归谁)
  function fillKeyAt(lon, lat) {
    const fx = fillX(lon), fy = fillY(lat);
    if (fx < 0 || fy < 0 || fx >= M.fw || fy >= M.fh) return null;
    const slot = M.fillOwner[fy * M.fw + fx];
    return slot >= 0 ? M.fillKeys[slot] : null;
  }

  // 缩略图 preview.png(同游戏：整张地表每 6 像素取一个，图层不是地形时叠一半图层颜色)
  function thumbnail() {
    prepare(true);
    const step = 6, w = Math.floor(M.w / step), h = Math.floor(M.h / step);
    const out = document.createElement("canvas");
    out.width = w; out.height = h;
    const context = out.getContext("2d"), image = context.createImageData(w, h), pixels = new Uint32Array(image.data.buffer);
    const terrain = new Uint32Array(terrainImage.data.buffer);
    const colors = M.fillKeys.map(key => D.colorOfKey(key));
    const tint = M.layer !== "terrain" && M.layer !== "wild";
    for (let y = 0; y < h; y++)
      for (let x = 0; x < w; x++) {
        const tx = x * step, ty = y * step;
        let color = terrain[ty * M.w + tx];
        const fx = Math.floor(tx / FILL), fy = M.fh - 1 - Math.floor(ty / FILL);
        const slot = tint && fx < M.fw && fy >= 0 ? M.fillOwner[fy * M.fw + fx] : -1;
        if (slot >= 0 && workClass[(M.h - 1 - ty) * M.w + tx] !== 0) {
          const c = colors[slot];
          const r = ((color & 0xFF) + c[0]) >> 1, g = (((color >> 8) & 0xFF) + c[1]) >> 1, b = (((color >> 16) & 0xFF) + c[2]) >> 1;
          color = 0xFF000000 | (b << 16) | (g << 8) | r;
        }
        pixels[y * w + x] = color;
      }
    context.putImageData(image, 0, 0);
    const binary = atob(out.toDataURL("image/png").split(",")[1]);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes;
  }

  function mount(element) {
    canvas = element;
    ctx = canvas.getContext("2d");
    new ResizeObserver(resize).observe(canvas);
    resize();
  }

  window.SM = {
    M, previewColor, classOf, loadEarth, loadRivers, useEarth, useImport, legendText, floodSame, zoneInfoAt, KIND_NAMES, kindLabel, mount, redraw, resize, fit, zoomAt, pan, centerOn, frameBounds,
    lonOf, latOf, sx, sy, screenToLonLat, inMap, isLand, typeAt, cityAt, passAt, fillKeyAt, keyOfCity, paintTexels, thumbnail,
    setOverlay: draw => { overlayDraw = draw; },
    setUnits: units => { M.units = units || []; },
    invalidate: () => { M.fillRevision = -1; redraw(); },
    setSizeIndex: index => { M.sizeIndex = index; M.fillRevision = -1; redraw(); },
    setSmooth: on => { M.smooth = on; M.fillRevision = -1; redraw(); }
  };
})();
