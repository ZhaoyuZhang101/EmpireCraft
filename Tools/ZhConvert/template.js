// 简繁转换，给编辑器自动补全另一种中文用：zhToTrad(简体) → 繁体(台湾用字)，zhToSimp(繁体) → 简体。
// 字典取自 OpenCC(https://github.com/BYVoid/OpenCC，Apache License 2.0)的单字表与词组表；
// 另加本模组繁体文案的惯用词(預設、模組設定、介面、劃進、煉鋼……)。由 Tools/ZhConvert/gen_zh_convert.py 生成，改词表请改 custom_s2t.json 再重新生成。
(function (root) {
  "use strict";
  const D = /*DATA*/;
  const own = Object.prototype.hasOwnProperty;
  function longest(map) {
    let n = 1;
    for (const k in map) { const len = Array.from(k).length; if (len > n) n = len; }
    return n;
  }
  const spMax = longest(D.sp), tpMax = longest(D.tp);
  function mapChars(cps, table) {
    return cps.map(c => (own.call(table, c) ? table[c] : c));
  }
  function convert(cps, phrases, max, chars) {
    let out = "";
    for (let i = 0; i < cps.length;) {
      let hit = 0;
      for (let n = Math.min(max, cps.length - i); n >= 2; n--) {
        const word = cps.slice(i, i + n).join("");
        if (own.call(phrases, word)) { out += phrases[word]; hit = n; break; }
      }
      if (hit) { i += hit; continue; }
      const c = cps[i++];
      out += own.call(chars, c) ? chars[c] : c;
    }
    return out;
  }
  function zhToTrad(text) {
    if (text === undefined || text === null || text === "") return text ?? "";
    const t = convert(Array.from(String(text)), D.sp, spMax, D.sc);
    return mapChars(Array.from(t), D.tv).join("");
  }
  function zhToSimp(text) {
    if (text === undefined || text === null || text === "") return text ?? "";
    const pre = mapChars(Array.from(String(text)), D.tvr);
    return convert(pre, D.tp, tpMax, D.tc);
  }
  root.zhToTrad = zhToTrad;
  root.zhToSimp = zhToSimp;
  if (typeof module !== "undefined" && module.exports) module.exports = { zhToTrad, zhToSimp };
})(typeof window !== "undefined" ? window : globalThis);
