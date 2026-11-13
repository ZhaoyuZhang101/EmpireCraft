// 发到服务器：把一份自定剧本(整个文件夹)传给独立服务器，让它照着开局。接口由独立服务器(.server/)提供：
//   登入      POST {server}/api/admin/pair       正文 {"user","password"} → {"token","admin":true}；403 wrong_admin、429 locked
//             (服务器没设账号密码时，账号留空、密码框里填它启动时打印的管理员码)
//   上传开局  POST {server}/api/admin/scenario   头 X-Terminal-Token；正文 {"name","files":[{"path","text"|"base64"}],"start":true}
//             → {"ok":true,"name","starting":true}；400 格式不对、401 没登入或令牌失效(重新登入一次再发)、413 太大
//   进度      GET  {server}/api/admin/scenario   → {"state":"idle|loading|ready|failed","name","step","detail"}，每秒问一次
// 服务器地址：网页就是从独立服务器打开的(对当前源 GET /api/server 回 {"server":"empirecraft"})就用当前源，不用填；
// 否则填地址，只写了地址的补上 http:// 和端口 8765。地址、账号和登入拿到的令牌记在浏览器里，密码不记。
(function () {
  const D = window.SD, UI = window.UI;
  const $ = id => document.getElementById(id);
  const DEFAULT_PORT = 8765, BODY_LIMIT = 16 * 1024 * 1024;
  const KEY_SERVER = "ec.scenario.send.server", KEY_USER = "ec.scenario.send.user", KEY_TOKEN = "ec.scenario.send.token:";

  const store = {
    get(key) { try { return localStorage.getItem(key) || ""; } catch { return ""; } },
    set(key, value) { try { if (value) localStorage.setItem(key, value); else localStorage.removeItem(key); } catch { /* 存不了就每回都填 */ } }
  };

  // 网页是不是独立服务器托管出来的
  let hosted = null;
  function detectHosted() {
    if (hosted) return hosted;
    hosted = (async () => {
      if (!/^https?:$/.test(location.protocol)) return false;
      try {
        const response = await fetch(location.origin + "/api/server", { cache: "no-store" });
        return response.ok && (await response.json()).server === "empirecraft";
      } catch { return false; }
    })();
    return hosted;
  }

  // 只写了地址的补上 http:// 和端口；写不成地址的返回 null
  function normalizeServer(text) {
    let value = String(text || "").trim().replace(/\/+$/, "");
    if (!value) return null;
    if (!/^[a-z][a-z0-9+.-]*:\/\//i.test(value)) value = "http://" + value;
    let url;
    try { url = new URL(value); } catch { return null; }
    if (!/^https?:$/.test(url.protocol) || !url.hostname) return null;
    // 有没有写端口看原文(http://x:80 的 80 会被 URL 吃掉，不能看 url.port)
    const authority = value.replace(/^[a-z][a-z0-9+.-]*:\/\//i, "").split(/[/?#]/)[0];
    const wrotePort = /:\d+$/.test(authority.replace(/^\[[^\]]*\]/, ""));
    if (!wrotePort) url.port = String(DEFAULT_PORT);
    return url.origin;
  }

  // 剧本文件夹名在服务器上只用小写字母、数字、下划线、短横
  // (中文之类的字符换成下划线；换过的在后面缀一小段按原名算的编号，两个中文名的剧本才不会在服务器上撞成同一个)
  function serverName(folder) {
    const text = String(folder || ""), plain = text.toLowerCase().replace(/[^a-z0-9_-]+/g, "_").replace(/^_+|_+$/g, "");
    if (plain === text) return plain || "scenario";
    let hash = 5381;
    for (let i = 0; i < text.length; i++) hash = (Math.imul(hash, 33) ^ text.charCodeAt(i)) >>> 0;
    return (plain || "scenario") + "_" + hash.toString(36).slice(0, 5);
  }

  const ERRORS = {
    wrong_admin: "管理员账号或密码不对", locked: "连错太多次，服务器暂时不收了，过一会儿再试",
    unauthorized: "没登入或登入过期了", unpaired: "没登入或登入过期了", too_large: "剧本太大，服务器不收(上限 16MB)",
    bad_request: "服务器说发过去的格式不对", bad_name: "服务器不认这个剧本名", no_files: "服务器说没收到文件",
    bad_path: "服务器不认其中一个文件的路径", no_scenario: "发过去的文件里没有 scenario.json", write_failed: "服务器把文件写盘时出错"
  };

  class ServerError extends Error {
    constructor(status, code, detail) {
      super((ERRORS[code] || code || `服务器回了 ${status}`) + (detail ? "：" + detail : ""));
      this.status = status;
      this.code = code;
    }
  }

  async function call(server, method, path, { token, body, timeout = 30000 } = {}) {
    const control = new AbortController(), timer = setTimeout(() => control.abort(), timeout);
    const headers = {};
    if (token) headers["X-Terminal-Token"] = token;
    if (body !== undefined) headers["Content-Type"] = "application/json";
    let response;
    try {
      response = await fetch(server + path, { method, headers, body, signal: control.signal, cache: "no-store" });
    } catch (error) {
      throw new Error(error.name === "AbortError" ? `${server} 半天没回话`
        : `连不上 ${server}：看看地址和端口对不对、服务器开着没有、防火墙放行没有`);
    } finally {
      clearTimeout(timer);
    }
    let data = null;
    try { data = await response.json(); } catch { /* 不是 JSON 的按状态码说 */ }
    if (!response.ok) throw new ServerError(response.status, data?.error || (response.status === 401 ? "unauthorized" : response.status === 413 ? "too_large" : ""), data?.detail);
    return data || {};
  }

  let run = 0, current = null;

  function log(text, kind) {
    const el = $("sendLog");
    const line = document.createElement("div");
    line.textContent = text;
    if (kind === "err") line.style.color = "var(--danger)";
    if (kind === "ok") line.style.color = "var(--good)";
    el.append(line);
    el.scrollTop = el.scrollHeight;
  }

  // 同一步的进度只改最后一行，不一秒刷一行
  function progress(text) {
    const el = $("sendLog"), last = el.lastElementChild;
    if (last && last.dataset.progress) last.textContent = text;
    else { log(text); el.lastElementChild.dataset.progress = "1"; }
  }

  async function open(folder, name) {
    current = { folder, name };
    run++;
    const isHosted = await detectHosted();
    $("sendWhat").textContent = `剧本「${name || folder}」(文件夹 ${folder})整个传过去，服务器照它开局。服务器上存成「${serverName(folder)}」。`;
    $("sendServerRow").hidden = isHosted;
    $("sendServerRow").style.display = isHosted ? "none" : "";
    $("sendHosted").hidden = !isHosted;
    $("sendHosted").textContent = isHosted ? `服务器：就是打开这个网页的这台(${location.origin})` : "";
    if (!$("sendServer").value) $("sendServer").value = store.get(KEY_SERVER);
    if (!$("sendUser").value) $("sendUser").value = store.get(KEY_USER);
    $("sendPass").value = "";
    $("sendLog").replaceChildren();
    $("sendGo").disabled = false;
    refreshTokenNote();
    if (!$("sendDialog").open) $("sendDialog").showModal();
  }

  async function serverOf() {
    return (await detectHosted()) ? location.origin : normalizeServer($("sendServer").value);
  }

  // 账号密码怎么填：问一下服务器是设了账号密码(adminLogin: password)还是只有管理员码(code)；问不到就两样都说
  let noteRun = 0;
  async function refreshTokenNote() {
    const mine = ++noteRun, server = await serverOf(), note = $("sendTokenNote");
    const remembered = server && store.get(KEY_TOKEN + server) ? "上回在这台服务器登入过：账号密码可以不填，登入过期了再填。" : "";
    note.textContent = remembered || "服务器没设账号密码的，账号留空、密码框里填它启动时打印的管理员码。";
    if (!server) return;
    try {
      const info = await call(server, "GET", "/api/server", { timeout: 4000 });
      if (mine !== noteRun || info.server !== "empirecraft") return;
      const how = info.adminLogin === "code" ? "这台服务器没设管理员账号密码：账号留空，密码框里填它启动时打印的管理员码。"
        : info.adminLogin === "password" ? "填这台服务器设的管理员账号和密码。" : "";
      note.textContent = [remembered, how].filter(Boolean).join(" ");
    } catch { /* 连不上的等点上传时再说 */ }
  }

  async function login(server) {
    const user = $("sendUser").value.trim(), password = $("sendPass").value;
    if (!password) throw new Error("填上管理员账号和密码再上传");
    log("登入…");
    const data = await call(server, "POST", "/api/admin/pair", { body: JSON.stringify({ user, password }) });
    if (!data.token) throw new Error("服务器没给令牌");
    store.set(KEY_TOKEN + server, data.token);
    store.set(KEY_USER, user);
    return data.token;
  }

  async function upload() {
    const mine = ++run, alive = () => mine === run && $("sendDialog").open;
    $("sendLog").replaceChildren();
    $("sendGo").disabled = true;
    try {
      const server = await serverOf();
      if (!server) throw new Error("服务器地址没写对：写成 1.2.3.4 或 http://1.2.3.4:8765 这样");
      if (location.protocol === "https:" && server.startsWith("http://"))
        throw new Error("这个网页是用 https 打开的，浏览器不让它连 http 的服务器：换成用 http 打开编辑器(本机的桌面版或「启动剧本编辑器(浏览器).bat」)再发");
      if (!(await detectHosted())) store.set(KEY_SERVER, $("sendServer").value.trim());
      // 正编着的这份有没存的改动：先存盘，发的是盘上的文件
      if (D.S.era && D.S.folder === current.folder && D.S.dirty) {
        log("先保存没存的改动…");
        await UI.save();
        if (D.S.dirty) throw new Error("保存没成，先把保存的问题解决");
      }
      const files = await UI.IO.files(current.folder);
      const body = JSON.stringify({
        name: serverName(current.folder), start: true,
        files: files.map(file => file.text !== undefined ? { path: file.path, text: file.text } : { path: file.path, base64: file.base64 })
      });
      const size = new Blob([body]).size;
      log(`${files.length} 个文件(${files.map(file => file.path).join("、")})，共 ${(size / 1024).toFixed(0)} KB`);
      if (size > BODY_LIMIT) throw new Error("剧本太大，超过服务器一次收的 16MB");

      let token = store.get(KEY_TOKEN + server);
      if (!token) token = await login(server);
      if (!alive()) return;
      log(`上传到 ${server} …`);
      let result;
      try {
        result = await call(server, "POST", "/api/admin/scenario", { token, body, timeout: 120000 });
      } catch (error) {
        if (!(error instanceof ServerError) || error.status !== 401) throw error;
        // 令牌失效：重新登入一次再发
        store.set(KEY_TOKEN + server, "");
        if (!$("sendPass").value) throw new Error("登入过期了：填上管理员账号和密码再点上传");
        token = await login(server);
        result = await call(server, "POST", "/api/admin/scenario", { token, body, timeout: 120000 });
      }
      if (!alive()) return;
      log(`服务器收下了「${result.name || serverName(current.folder)}」${result.starting ? "，正在开局" : ""}`);
      await watch(server, token, alive);
    } catch (error) {
      log(error.message, "err");
    } finally {
      if (mine === run) { $("sendGo").disabled = false; $("sendPass").value = ""; refreshTokenNote(); }
    }
  }

  // 每秒问一次进度，到开好局或失败为止
  async function watch(server, token, alive) {
    const started = Date.now();
    let idleSince = Date.now(), misses = 0;
    while (alive()) {
      await new Promise(resolve => setTimeout(resolve, 1000));
      if (!alive()) return;
      let status;
      try {
        status = await call(server, "GET", "/api/admin/scenario", { token, timeout: 10000 });
        misses = 0;
      } catch (error) {
        // 开局时服务器忙，偶尔一两回问不到不算
        if (++misses >= 8) throw new Error("问不到进度了：" + error.message);
        continue;
      }
      const seconds = Math.round((Date.now() - started) / 1000);
      if (status.state === "ready") { log(`服务器已照「${status.name || serverName(current.folder)}」开好局(用了 ${seconds} 秒)`, "ok"); return; }
      if (status.state === "failed") throw new Error("服务器开局没成" + (status.detail ? "：" + status.detail : ""));
      if (status.state === "loading") { idleSince = Date.now(); progress(`服务器开局中：${status.step || "…"}(${seconds} 秒)`); }
      else {
        progress(`等服务器开始…(${seconds} 秒)`);
        if (Date.now() - idleSince > 60000) throw new Error("服务器一分钟了还没开始开局，到服务器的控制台看看");
      }
      if (Date.now() - started > 20 * 60000) throw new Error("二十分钟了还没开好局，不再等了；到服务器的控制台看看");
    }
  }

  function init() {
    $("sendGo").onclick = upload;
    $("sendClose").onclick = () => { run++; $("sendDialog").close(); };
    $("sendDialog").addEventListener("close", () => { run++; });
    $("sendServer").addEventListener("change", refreshTokenNote);
    $("sendPass").addEventListener("keydown", event => { if (event.key === "Enter") upload(); });
    detectHosted();
  }

  window.SS = { open, normalizeServer, serverName };
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init); else init();
})();
