// 剧本编辑器的浏览器版：不装 Electron、不开游戏也能用(独立服务器那台机器上只要有 Node)。
// 用法：node scenario-server.js [--port 8777] [--host 127.0.0.1] [--mod <模组目录>] [--scenarios <自定剧本文件夹>]
// 然后用浏览器打开 http://127.0.0.1:8777/scenario.html
// 页面用相对路径找这些接口(api/...)，所以整套网页挂在别的前缀下(独立服务器的 /editor/)也行，只要那个前缀下有同样的接口：
//   GET  api/info                      → {modRoot, scenarioDir, exists, mode}
//   GET  api/scenarios                 → {dir, exists, custom:[{folder, zh, year, polities, cities, imported, author, base, desc}], presets:[{id, zh, year, polities, desc}]}
//   GET  api/scenario?folder=          → {folder, path, text}            (自定剧本的 scenario.json)
//   GET  api/preset?id=                → {id, path, text}                (WorldMaps/eras/<id>.json)
//   GET  api/cultures                  → ["Huaxia", ...]                 (Locales/Cultures/Culture_<键>)
//   GET  api/naming                    → {文化: {rule, groups}}           (各文化的城名规则与词库，放城起名用)
//   GET  api/world?file=               → 原始字节                         (WorldMaps 下的 cities.json、earth.bytes、rivers.bytes、wilderness.json)
//   GET  api/files?folder=             → [{path, text | base64, size}]   (整个剧本文件夹，发到服务器用)
//   GET  api/file?folder=&name=        → {exists, base64}                (剧本文件夹里的 map.png、map_colors.txt)
//   PUT  api/file?folder=&name=        正文为文件内容 → {path}
//   DELETE api/scenario?folder=        → {folder}                        (删掉整个剧本文件夹)
//   POST api/reserve?base=             → {folder, path}                  (占一个没用过的文件夹名)
//   PUT  api/scenario?folder=          正文为 scenario.json 全文 → {folder, path}
//   PUT  api/preview?folder=           正文为 PNG → {path}
// 默认只听本机；要从别的机器连(在服务器上编剧本)写 --host 0.0.0.0，自己管好防火墙——能连上的人都能改剧本文件夹。
const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");
const core = require("./scenario-core");

function arg(name, fallback) {
  const at = process.argv.indexOf("--" + name);
  return at >= 0 && process.argv[at + 1] ? process.argv[at + 1] : fallback;
}

const port = Number(arg("port", process.env.PORT || 8777));
const host = arg("host", "127.0.0.1");
const modRoot = path.resolve(arg("mod", path.join(__dirname, "..")));
const scenarioDir = core.resolveScenarioDir(modRoot, arg("scenarios", ""));

if (!fs.existsSync(path.join(modRoot, "WorldMaps", "cities.json"))) {
  console.error(`这里不像模组目录(找不到 WorldMaps/cities.json)：${modRoot}\n用 --mod <模组目录> 指明`);
  process.exit(1);
}

const TYPES = {
  ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8", ".png": "image/png", ".ico": "image/x-icon", ".woff2": "font/woff2", ".txt": "text/plain; charset=utf-8"
};

function send(res, status, body, type = "application/json; charset=utf-8") {
  const data = Buffer.isBuffer(body) ? body : Buffer.from(typeof body === "string" ? body : JSON.stringify(body));
  res.writeHead(status, { "Content-Type": type, "Content-Length": data.length, "Cache-Control": "no-store" });
  res.end(data);
}

function body(req, limit = 64 * 1024 * 1024) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on("data", chunk => {
      size += chunk.length;
      if (size > limit) { reject(new Error("内容太大")); req.destroy(); return; }
      chunks.push(chunk);
    });
    req.on("end", () => resolve(Buffer.concat(chunks)));
    req.on("error", reject);
  });
}

async function api(req, res, url) {
  const q = name => url.searchParams.get(name) || "";
  const route = req.method + " " + url.pathname;
  switch (route) {
    // 独立服务器才有这个接口(回 {"server":"empirecraft"})；这里明说不是，页面就知道要另填服务器地址
    case "GET /api/server": return send(res, 200, { server: "scenario-editor" });
    case "GET /api/info": return send(res, 200, { modRoot, scenarioDir, exists: fs.existsSync(scenarioDir), mode: "http" });
    case "GET /api/scenarios": return send(res, 200, await core.listScenarios(modRoot, scenarioDir));
    case "GET /api/scenario": return send(res, 200, await core.readScenario(scenarioDir, q("folder")));
    case "GET /api/preset": return send(res, 200, await core.readPreset(modRoot, q("id")));
    case "GET /api/naming": return send(res, 200, await core.cityNaming(modRoot));
    case "GET /api/cultures": return send(res, 200, await core.listCultureKeys(modRoot));
    case "GET /api/files": return send(res, 200, await core.readFolderFiles(scenarioDir, q("folder")));
    case "GET /api/file": return send(res, 200, await core.readFolderFile(scenarioDir, q("folder"), q("name")));
    case "PUT /api/file": return send(res, 200, await core.writeFolderFile(scenarioDir, q("folder"), q("name"), await body(req)));
    case "DELETE /api/scenario": return send(res, 200, await core.deleteScenario(scenarioDir, q("folder")));
    case "GET /api/world": return send(res, 200, await core.readWorldFile(modRoot, q("file")), "application/octet-stream");
    case "POST /api/reserve": return send(res, 200, await core.reserveFolder(scenarioDir, q("base")));
    case "PUT /api/scenario": return send(res, 200, await core.writeScenario(scenarioDir, q("folder"), (await body(req)).toString("utf8")));
    case "PUT /api/preview": return send(res, 200, await core.writePreview(scenarioDir, q("folder"), await body(req)));
  }
  return send(res, 404, { error: "没有这个接口" });
}

function serveFile(res, url) {
  const rel = decodeURIComponent(url.pathname === "/" ? "/scenario.html" : url.pathname);
  const file = path.join(__dirname, rel);
  const inside = path.relative(__dirname, file);
  if (inside.startsWith("..") || path.isAbsolute(inside) || /^(node_modules|dist)[\\/]/.test(inside) ||
      !fs.existsSync(file) || !fs.statSync(file).isFile()) return send(res, 404, "not found", "text/plain; charset=utf-8");
  send(res, 200, fs.readFileSync(file), TYPES[path.extname(file).toLowerCase()] || "application/octet-stream");
}

http.createServer(async (req, res) => {
  const url = new URL(req.url, "http://localhost");
  try {
    if (url.pathname.startsWith("/api/")) {
      // 改文件的请求只收本页面发来的(同源)：别的网页拿不到这里的写接口
      const origin = req.headers.origin;
      if (req.method !== "GET" && origin && new URL(origin).host !== req.headers.host) return send(res, 403, { error: "跨站请求不收" });
      return await api(req, res, url);
    }
    if (req.method !== "GET") return send(res, 405, "method not allowed", "text/plain; charset=utf-8");
    serveFile(res, url);
  } catch (error) {
    send(res, 500, { error: error.message });
  }
}).listen(port, host, () => {
  console.log(`剧本编辑器：http://${host === "0.0.0.0" ? "127.0.0.1" : host}:${port}/scenario.html`);
  console.log(`模组目录：${modRoot}`);
  console.log(`自定剧本：${scenarioDir}${fs.existsSync(scenarioDir) ? "" : " (还没有，保存时新建)"}`);
});
