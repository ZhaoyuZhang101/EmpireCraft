// 编辑器在线更新(用户 2026-11-16 "政体编辑器我希望能在线更新")。
// 更新包就是安装程序本身，和模组更新放在同一个更新服务上(发布见 publish-editor.ps1)：
//   <更新服务>/releases/EmpireCraftEditor.json   清单 { version, fileName, sha256, size, notes, parts? }
//   <更新服务>/releases/<fileName>               安装程序；太大、一回传不上去时切成几片(parts: [{ fileName, size }]，按顺序接起来)
// 这里做三件事：查有没有新版 → 下到临时目录(边下边算校验值，接好后大小和 sha256 都对上才算数) → 悄悄跑安装程序、
// 退出编辑器，安装程序装回原来的位置后再把编辑器开起来。
// 只有装好的编辑器能自己更新：便携版是一个每回解到临时目录的 exe，没处可装；用源码跑的也不更新。
const fs = require("node:fs");
const path = require("node:path");
const https = require("node:https");
const http = require("node:http");
const crypto = require("node:crypto");
const { spawn } = require("node:child_process");

const MANIFEST = "EmpireCraftEditor.json";
// 同模组更新用的两个地址(见 Scripts/Diagnostics/EmpireCraftUpdateService.cs)，前一个不通试后一个
const BASES = [
  "https://update.cmhgamedev.dpdns.org/releases/",
  "https://empirecraft-update.zhangzhaoyu101.workers.dev/releases/"
];

// 版本号按数字一段一段比("0.5.10" 比 "0.5.9" 新)；段里不是数字的部分不管
function compare(a, b) {
  const parts = text => String(text || "").split(".").map(part => parseInt(part, 10) || 0);
  const left = parts(a), right = parts(b);
  for (let i = 0; i < Math.max(left.length, right.length); i++) {
    const diff = (left[i] || 0) - (right[i] || 0);
    if (diff !== 0) return diff > 0 ? 1 : -1;
  }
  return 0;
}

function get(url, redirects = 3) {
  return new Promise((resolve, reject) => {
    const request = (url.startsWith("http:") ? http : https).get(url, { headers: { "User-Agent": "EmpireCraftEditor" } }, response => {
      const status = response.statusCode || 0;
      if (status >= 300 && status < 400 && response.headers.location && redirects > 0) {
        response.resume();
        resolve(get(new URL(response.headers.location, url).toString(), redirects - 1));
        return;
      }
      if (status !== 200) {
        response.resume();
        reject(new Error(`服务器回了 ${status}`));
        return;
      }
      resolve(response);
    });
    request.setTimeout(15000, () => request.destroy(new Error("连接超时")));
    request.on("error", reject);
  });
}

async function fetchJson(url) {
  const response = await get(url);
  const chunks = [];
  for await (const chunk of response) chunks.push(chunk);
  return JSON.parse(Buffer.concat(chunks).toString("utf8").replace(/^﻿/, ""));
}

// 查新版。bases 可以换(测试用)。返回 { current, version, available, notes, size, base, manifest } 或 { current, error }
async function check(current, bases = BASES) {
  let last = "";
  for (const base of bases) {
    try {
      const manifest = await fetchJson(base + MANIFEST);
      if (!manifest || !manifest.version || !manifest.fileName) throw new Error("清单不完整");
      return {
        current, version: String(manifest.version), available: compare(manifest.version, current) > 0,
        notes: String(manifest.notes || ""), size: Number(manifest.size) || 0, base, manifest
      };
    } catch (error) {
      last = error.message;
    }
  }
  return { current, error: "查不到更新：" + last };
}

// 把安装程序下到 folder 里。onProgress(已收字节, 总字节)。大小或校验值对不上就删掉、报错
async function download(found, folder, onProgress) {
  const manifest = found.manifest;
  const pieces = Array.isArray(manifest.parts) && manifest.parts.length > 0 ? manifest.parts : [{ fileName: manifest.fileName }];
  const total = Number(manifest.size) || 0;
  const target = path.join(folder, path.basename(String(manifest.fileName)));
  fs.mkdirSync(folder, { recursive: true });
  const hash = crypto.createHash("sha256");
  const out = fs.createWriteStream(target);
  let received = 0, lastTold = 0;
  try {
    for (const piece of pieces) {
      const response = await get(found.base + encodeURIComponent(String(piece.fileName)));
      for await (const chunk of response) {
        hash.update(chunk);
        received += chunk.length;
        if (!out.write(chunk)) await new Promise(resolve => out.once("drain", resolve));
        if (onProgress && Date.now() - lastTold > 150) {
          lastTold = Date.now();
          onProgress(received, total);
        }
      }
    }
    await new Promise((resolve, reject) => out.end(error => error ? reject(error) : resolve()));
    if (onProgress) onProgress(received, total || received);
    if (total > 0 && received !== total) throw new Error(`下下来的大小不对(${received}，该是 ${total})`);
    const digest = hash.digest("hex");
    if (manifest.sha256 && digest !== String(manifest.sha256).toLowerCase()) throw new Error("下下来的文件校验不过，可能没下全");
    return target;
  } catch (error) {
    out.destroy();
    try {
      fs.rmSync(target, { force: true });
    } catch {
      // 删不掉就留着，下回会覆盖
    }
    throw error;
  }
}

// 跑安装程序：/S 不出界面，装回原来的位置(安装程序自己记着)；--force-run 装完把编辑器再开起来。调用的人随后退出编辑器
function install(file) {
  const child = spawn(file, ["--updated", "/S", "--force-run"], { detached: true, stdio: "ignore", windowsHide: false });
  child.unref();
}

module.exports = { compare, check, download, install, MANIFEST, BASES };
