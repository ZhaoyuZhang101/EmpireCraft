// EmpireCraft 终端模式的联机大厅，Cloudflare 上的那一版(Workers + Durable Objects)。
// 与同目录 lobby_server.py 是同一套协议的两种实现，游戏那头见 Scripts/Terminal/TerminalRelay.cs：
//   · 网页入口：GET / 是大厅页(lobby.html)，GET /api/rooms 是房间列表，GET /api/join?room= 给公开房间的配对码；
//   · 房主接入：游戏用 WebSocket 连 /host?room=房间号&key=钥匙，发 HELLO / UPDATE 报房间信息；
//   · 中转：手机访问 /r/房间号/…，转成文本帧 "REQ\n编号\n方法\n路径\n令牌\n正文" 发给房主，等它回二进制帧
//     "RES 编号 状态 类型\n" 加正文，再交还手机。
// 世界在房主的电脑上算，这里不存任何游戏数据。
//   · Room：一个房间一个实例，握着房主那条连接(可休眠，闲着不计时长)，记着钥匙(别人拿着房间号顶替不了房主)；
//   · Lobby：全站一个实例，一张房间表(SQLite)，每个地址至多 MAX_PER_IP 间，久不报到的自动撤下。
import LOBBY_PAGE from "./lobby.html";
// 大厅页自己的图标、像素框、桌面清单(房间里的那几样由房主那头给，见 STATIC)
import ICON_192 from "./icon-192.png";
import ICON_512 from "./icon-512.png";
import ICON_APPLE from "./apple-touch-icon.png";
import FRAME from "./FactionFrame.png";
import FRAME_GOLD from "./FactionFrame_dominate.png";
import MANIFEST from "./manifest.json";

const LOBBY_FILES = {
  "/icon-192.png": ICON_192, "/icon-512.png": ICON_512, "/apple-touch-icon.png": ICON_APPLE, "/favicon.ico": ICON_192,
  "/FactionFrame.png": FRAME, "/FactionFrame_dominate.png": FRAME_GOLD,
};

const ROOM = /^[0-9a-f]{16}$/, KEY = /^[0-9a-f]{32}$/, TOKEN = /^[0-9a-f]{0,64}$/;
const RELAY = /^\/r\/([0-9a-f]{16})(\/.*)?$/;
// 房主那边不常变的几个文件：每个房间各存一份，省得每个来人都穿一趟
const STATIC = new Set(["/manifest.json", "/icon-192.png", "/icon-512.png", "/apple-touch-icon.png", "/FactionFrame.png", "/FactionFrame_dominate.png"]);
const MAX_BODY = 4096, REPLY_MS = 8000, MAX_NAME = 24, MAX_ROOMS = 500, MAX_PER_IP = 3;
// 房主每 4 分钟报到一次(见 TerminalHost.Lobby 的 RoomInfoSeconds)；12 分钟没报到的从列表撤下
const PUT_EVERY_MS = 4 * 60 * 1000, STALE_MS = 12 * 60 * 1000, LIST_CACHE_SECONDS = 5;

const BASE_HEADERS = { "cache-control": "no-store", "x-content-type-options": "nosniff" };
const json = (value, status = 200) =>
  new Response(JSON.stringify(value), { status, headers: { ...BASE_HEADERS, "content-type": "application/json; charset=utf-8" } });
const fail = (status, code) => json({ error: code }, status);
const lobby = (env) => env.LOBBY.get(env.LOBBY.idFromName("lobby"));
const roomOf = (env, id) => env.ROOM.get(env.ROOM.idFromName(id));

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url), path = url.pathname, method = request.method;
    try {
      if (path === "/host") {
        if ((request.headers.get("Upgrade") || "").toLowerCase() !== "websocket") return fail(426, "upgrade");
        const room = url.searchParams.get("room") || "", key = url.searchParams.get("key") || "";
        if (!ROOM.test(room) || !KEY.test(key)) return fail(400, "bad_room");
        return roomOf(env, room).fetch(request);
      }
      if (method === "GET" && (path === "/" || path === "/index.html"))
        return new Response(LOBBY_PAGE, { headers: { ...BASE_HEADERS, "content-type": "text/html; charset=utf-8" } });
      if (method === "GET" && LOBBY_FILES[path])
        return new Response(LOBBY_FILES[path], { headers: { "content-type": "image/png", "cache-control": "public, max-age=86400" } });
      if (method === "GET" && path === "/manifest.json")
        return new Response(JSON.stringify(MANIFEST), { headers: { "content-type": "application/manifest+json", "cache-control": "public, max-age=3600" } });
      if (method === "GET" && path === "/api/rooms") return rooms(url, env, ctx);
      if (method === "GET" && path === "/api/join") {
        const room = url.searchParams.get("room") || "";
        return ROOM.test(room) ? roomOf(env, room).fetch("https://room/join") : fail(404, "offline");
      }
      if (path === "/healthz") return new Response("ok");
      const match = RELAY.exec(path);
      if (!match) return fail(404, "not_found");
      if (!match[2]) return Response.redirect(url.origin + path + "/" + url.search, 302);
      if (method !== "GET" && method !== "POST") return fail(405, "method");
      const body = method === "POST" ? await request.text() : "";
      if (body.length > MAX_BODY) return fail(400, "bad_request");
      const token = request.headers.get("x-terminal-token") || "";
      return roomOf(env, match[1]).fetch("https://room/relay", {
        method: "POST",
        headers: { "x-method": method, "x-path": match[2].replace(/[\r\n]/g, "").slice(0, 200), "x-token": TOKEN.test(token) ? token : "" },
        body,
      });
    } catch (error) {
      return fail(500, "failed");
    }
  },
};

// 房间列表在边缘存 LIST_CACHE_SECONDS 秒：许多人同时开着大厅页时不必回回去问
async function rooms(url, env, ctx) {
  const key = new Request(url.origin + "/api/rooms");
  let cached;
  try {
    cached = await caches.default.match(key);
  } catch (error) {
    cached = undefined;
  }
  if (cached) return cached;
  const fresh = await lobby(env).fetch("https://lobby/list");
  const answer = new Response(fresh.body, fresh);
  answer.headers.set("cache-control", "public, max-age=" + LIST_CACHE_SECONDS);
  try {
    ctx.waitUntil(caches.default.put(key, answer.clone()));
  } catch (error) {
    // 没有边缘缓存(本机调试)：不存
  }
  return answer;
}

function parseInfo(text) {
  const fields = {};
  for (const line of text.split("\n")) {
    const cut = line.indexOf("=");
    if (cut > 0) fields[line.slice(0, cut).trim()] = line.slice(cut + 1);
  }
  const number = (name, low, high) => {
    const value = parseInt(fields[name] || "0", 10);
    return Number.isFinite(value) ? Math.max(low, Math.min(high, value)) : low;
  };
  const name = Array.from(fields.name || "").filter((ch) => ch >= " " && ch !== "<" && ch !== ">" && ch !== "\x7f").join("").trim().slice(0, MAX_NAME);
  const open = fields.open === "1";
  return {
    name: name || "EmpireCraft", open, used: number("used", 0, 64), max: number("max", 1, 64),
    code: open && /^\d{6}$/.test(fields.code || "") ? fields.code : "", ver: number("ver", 0, 100000),
  };
}

export class Room {
  constructor(ctx, env) {
    this.ctx = ctx;
    this.env = env;
    // 等房主回话的请求(编号 → 回调)；实例休眠后清空，那时也没有在等的
    this.pending = new Map();
    this.seq = 0;
    this.cache = new Map();
    this.info = undefined;
    this.listed = "";
    this.listedAt = 0;
  }

  host() {
    const open = this.ctx.getWebSockets().filter((socket) => socket.readyState === 1);
    return open.length ? open[open.length - 1] : null;
  }

  async fetch(request) {
    const url = new URL(request.url);
    if (url.pathname === "/host") return this.accept(request, url);
    if (url.pathname === "/relay") return this.relay(request);
    if (url.pathname === "/join") {
      const info = this.info ?? (await this.ctx.storage.get("info"));
      if (!info || !this.host()) return fail(404, "offline");
      return json({ open: info.open, code: info.open ? info.code : "" });
    }
    return fail(404, "not_found");
  }

  async accept(request, url) {
    const room = url.searchParams.get("room"), key = url.searchParams.get("key");
    const saved = await this.ctx.storage.get("key");
    if (saved && saved !== key) return fail(403, "key");
    await this.ctx.storage.put({ key, room, ip: request.headers.get("cf-connecting-ip") || "?" });
    // 同一位房主重连：旧的那条连接作废
    for (const old of this.ctx.getWebSockets()) {
      try {
        old.close(4000, "replaced");
      } catch (error) {
        // 已经断了
      }
    }
    const pair = new WebSocketPair();
    this.ctx.acceptWebSocket(pair[1]);
    this.cache.clear();
    this.info = undefined;
    this.listed = "";
    return new Response(null, { status: 101, webSocket: pair[0] });
  }

  async relay(request) {
    const socket = this.host();
    if (!socket) return fail(503, "host_offline");
    const method = request.headers.get("x-method") || "GET", path = request.headers.get("x-path") || "/";
    const token = request.headers.get("x-token") || "", body = await request.text();
    const cacheable = method === "GET" && STATIC.has(path);
    if (cacheable && this.cache.has(path)) return this.reply(this.cache.get(path), true);
    const id = String(++this.seq);
    const result = await new Promise((resolve) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        resolve(null);
      }, REPLY_MS);
      this.pending.set(id, { resolve, timer });
      try {
        socket.send(["REQ", id, method, path, token, body].join("\n"));
      } catch (error) {
        clearTimeout(timer);
        this.pending.delete(id);
        resolve(undefined);
      }
    });
    if (result === undefined) return fail(503, "host_offline");
    if (result === null) return fail(504, "host_timeout");
    if (cacheable && result.status === 200) this.cache.set(path, result);
    return this.reply(result, cacheable);
  }

  reply(result, cacheable) {
    const status = result.status >= 200 && result.status <= 599 ? result.status : 500;
    return new Response(result.body, {
      status,
      headers: { ...BASE_HEADERS, "content-type": result.type, "cache-control": cacheable ? "public, max-age=3600" : "no-store" },
    });
  }

  async webSocketMessage(socket, message) {
    if (typeof message === "string") {
      const cut = message.indexOf("\n"), head = cut < 0 ? message : message.slice(0, cut);
      if (head === "HELLO" || head === "UPDATE") await this.report(parseInfo(cut < 0 ? "" : message.slice(cut + 1)), socket);
      return;
    }
    const bytes = new Uint8Array(message), cut = bytes.indexOf(10);
    if (cut < 0) return;
    const parts = new TextDecoder().decode(bytes.subarray(0, cut)).split(" ");
    if (parts[0] !== "RES" || parts.length < 3) return;
    const waiting = this.pending.get(parts[1]);
    if (!waiting) return;
    this.pending.delete(parts[1]);
    clearTimeout(waiting.timer);
    waiting.resolve({ status: parseInt(parts[2], 10) || 500, type: parts.slice(3).join(" ") || "text/plain", body: bytes.slice(cut + 1) });
  }

  // 房间信息：记下；列表上看得到的几项变了、或隔了一阵，就报给大厅(配对码不进列表)
  async report(info, socket) {
    this.info = info;
    await this.ctx.storage.put("info", info);
    const listed = JSON.stringify([info.name, info.open, info.used, info.max, info.ver]), now = Date.now();
    if (listed === this.listed && now - this.listedAt < PUT_EVERY_MS) return;
    this.listed = listed;
    this.listedAt = now;
    const saved = await this.ctx.storage.get(["room", "ip"]);
    const answer = await lobby(this.env).fetch("https://lobby/put", {
      method: "POST",
      body: JSON.stringify({ id: saved.get("room"), ip: saved.get("ip"), name: info.name, open: info.open, used: info.used, max: info.max, ver: info.ver }),
    });
    if (answer.status !== 429) return;
    // 这个地址开的房间太多、或大厅满了：回绝
    this.listed = "";
    try {
      socket.close(4029, (await answer.json()).error || "too_many");
    } catch (error) {
      // 已经断了
    }
  }

  async webSocketClose(socket, code, reason) {
    try {
      socket.close(code === 1005 || code === 1006 ? 1000 : code, reason);
    } catch (error) {
      // 已经关了
    }
    await this.gone(socket);
  }

  async webSocketError(socket) {
    await this.gone(socket);
  }

  // 房主那条连接没了(又没有新的顶上)：在等的请求都放掉，房间从大厅撤下
  async gone(socket) {
    if (this.ctx.getWebSockets().some((other) => other !== socket && other.readyState === 1)) return;
    for (const waiting of this.pending.values()) {
      clearTimeout(waiting.timer);
      waiting.resolve(undefined);
    }
    this.pending.clear();
    this.listed = "";
    const room = await this.ctx.storage.get("room");
    if (room) await lobby(this.env).fetch("https://lobby/drop?id=" + room);
  }
}

export class Lobby {
  constructor(ctx, env) {
    this.sql = ctx.storage.sql;
    this.sql.exec(
      "CREATE TABLE IF NOT EXISTS rooms (id TEXT PRIMARY KEY, ip TEXT, name TEXT, open INTEGER, taken INTEGER, seats INTEGER, ver INTEGER, since INTEGER, at INTEGER)"
    );
  }

  async fetch(request) {
    const url = new URL(request.url), now = Date.now();
    if (url.pathname === "/list") {
      this.sql.exec("DELETE FROM rooms WHERE at < ?", now - STALE_MS);
      const rows = this.sql.exec("SELECT id, name, open, taken, seats, ver FROM rooms ORDER BY since DESC LIMIT 200").toArray();
      return json({ rooms: rows.map((row) => ({ id: row.id, name: row.name, open: !!row.open, used: row.taken, max: row.seats, ver: row.ver })) });
    }
    if (url.pathname === "/put") {
      const room = await request.json();
      if (!ROOM.test(room.id || "")) return fail(400, "bad_room");
      if (this.sql.exec("SELECT 1 FROM rooms WHERE id = ?", room.id).toArray().length === 0) {
        const fresh = now - STALE_MS;
        if (this.sql.exec("SELECT COUNT(*) AS n FROM rooms WHERE at >= ?", fresh).one().n >= MAX_ROOMS) return fail(429, "lobby_full");
        if (this.sql.exec("SELECT COUNT(*) AS n FROM rooms WHERE ip = ? AND at >= ?", room.ip || "?", fresh).one().n >= MAX_PER_IP)
          return fail(429, "too_many");
      }
      this.sql.exec(
        "INSERT INTO rooms (id, ip, name, open, taken, seats, ver, since, at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?) " +
          "ON CONFLICT(id) DO UPDATE SET ip = excluded.ip, name = excluded.name, open = excluded.open, taken = excluded.taken, " +
          "seats = excluded.seats, ver = excluded.ver, at = excluded.at",
        room.id, room.ip || "?", String(room.name || "EmpireCraft"), room.open ? 1 : 0, room.used | 0, room.max | 0, room.ver | 0, now, now
      );
      return json({ ok: true });
    }
    if (url.pathname === "/drop") {
      this.sql.exec("DELETE FROM rooms WHERE id = ?", url.searchParams.get("id") || "");
      return json({ ok: true });
    }
    return fail(404, "not_found");
  }
}
