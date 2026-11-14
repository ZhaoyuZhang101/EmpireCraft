(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  const [width, height] = PORTRAIT.cell;
  const stage = $('preview');
  const canvas = $('garment');
  const [cw, ch] = [canvas.width, canvas.height];
  const ctx = canvas.getContext('2d');
  const clamp = (n, max) => Math.max(-max, Math.min(max, Math.round(n)));
  const copy = data => JSON.parse(JSON.stringify(data));
  const status = (id, text, error = false) => {
    $(id).textContent = text;
    $(id).toggleAttribute('data-error', error);
  };
  const options = (select, items, selected) => {
    select.replaceChildren(...items.map(([value, label]) => new Option(label, value)));
    if (items.some(([value]) => value === selected)) select.value = selected;
  };
  const localPoint = (event, element, w, h) => {
    const box = element.getBoundingClientRect();
    return [(event.clientX - box.left) * w / box.width, (event.clientY - box.top) * h / box.height];
  };
  const request = async (path, data) => {
    const response = await fetch(path, {
      method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify(data),
    });
    const result = await response.json();
    if (!response.ok) throw Error(result.error);
    return result;
  };
  const layerNames = {
    body: '衣身', base: '底身', head: '头', back: '后发', hair: '前发',
    hat: '帽子', beard: '胡须', mark: '面纹',
  };
  const heads = {
    eastern: '华夏男', japanese: '日本男', steppe: '游牧男', western: '西方男',
    arabian: '阿拉伯男', elder_eastern: '年长华夏男', elder_western: '年长西方男',
    eastern_woman: '华夏女', eastern_lady: '东亚女', steppe_woman: '游牧女',
    western_woman: '西方女', arabian_woman: '阿拉伯女',
  };
  const outfitKey = p => 'body:' + p.body + ':' + p.sex;
  const presets = new Map();
  for (const p of PORTRAIT.presets) if (!presets.has(outfitKey(p))) presets.set(outfitKey(p), p);
  const sexes = {m: '男版', f: '女版'};
  const clothing = PORTRAIT.wardrobe;
  let parts, selectedOutfit, draft = copy(PORTRAIT.alignment || {}), saved = JSON.stringify(draft);
  let revision = FIT.revision, pending = false, binding = false, drag, generation = 0, drawFrame;
  PORTRAIT.alignment = draft;

  function chosenOffset() {
    return draft[selectedOutfit]?.[$('layer').value] || [0, 0];
  }
  function syncLive() {
    const [x, y] = chosenOffset();
    $('dx').value = x;
    $('dy').value = y;
    const garment = $('layer').value === selectedOutfit;
    $('dx').disabled = $('dy').disabled = garment || pending;
    $('save').disabled = pending || JSON.stringify(draft) === saved;
    $('live-guide').hidden = !$('show-guide').checked;
    $('bind').disabled = pending;
  }
  function changed() {
    syncLive();
    window.cancelAnimationFrame(drawFrame);
    const serial = ++generation;
    drawFrame = window.requestAnimationFrame(async () => {
      const current = {...parts};
      let layers = ptHdLayers(current);
      if ($('solo').checked && !binding) {
        const key = $('layer').value;
        layers = layers.filter(l => key === '@head' ? l.key !== selectedOutfit : l.key === key);
      }
      try {
        const images = await Promise.all(layers.map(l => ptHdPage(l.page)));
        if (serial === generation) $('portrait').src = ptHdDraw(current, layers, images);
      } catch {
        if (serial === generation) status('live-status', '高清图层载入失败', true);
      }
    });
  }
  function outfitOffset(key, value) {
    const offsets = draft[selectedOutfit] ||= {};
    if (value) offsets[key] = value;
    else delete offsets[key];
    if (!Object.keys(offsets).length) delete draft[selectedOutfit];
  }
  function setOffset(x, y) {
    const key = $('layer').value;
    if (key === selectedOutfit || pending || !Number.isFinite(x + y)) return;
    const delta = [clamp(x, width), clamp(y, height)];
    outfitOffset(key, delta.some(Boolean) ? delta : null);
    changed();
  }
  function updateLayers(preferred) {
    const active = ptLayers(parts).map(l => [l.key, layerNames[l.key.split(':')[0]]]);
    options($('layer'), [['@head', '头部整组'], ...active], preferred || '@head');
    changed();
  }
  function chooseOutfit() {
    selectedOutfit = $('outfit').value;
    parts = {...presets.get(selectedOutfit)};
    binding = false;
    const faceList = Object.entries(heads).filter(([key]) =>
      ['eastern_woman', 'eastern_lady', 'steppe_woman', 'western_woman', 'arabian_woman'].includes(key) === (parts.sex === 'f'));
    options($('head'), faceList, parts.head);
    for (const slot of ['hair', 'hat']) {
      const items = Object.entries(clothing[slot]).filter(([, g]) => ptFits(g, parts.sex))
        .map(([key, g]) => [key, g.name]);
      if (slot === 'hat') items.unshift(['none', '不戴帽']);
      options($(slot), items, parts[slot]);
    }
    options($('beard'), [['none','无'],['moustache','髭'],['goatee','山羊须'],['long','长须'],['boxed','短髯']], parts.beard);
    options($('mark'), [['none','无'],['scar','伤疤'],['mole','痣'],['rouge','胭脂'],['stubble','胡茬']], parts.mark);
    $('live-guide').src = '/templates/terminal_' + parts.sex + '.png';
    status('live-status', '');
    updateLayers();
  }
  options($('outfit'), [...presets].map(([key,p]) => [key, clothing.body[p.body].name + ' · ' + sexes[p.sex]]), 'body:work_deel:m');
  $('outfit').onchange = chooseOutfit;
  for (const key of ['head','hair','hat','beard','mark']) $(key).onchange = () => {
    parts[key] = $(key).value;
    updateLayers($('layer').value);
  };
  for (const key of ['layer','solo','show-guide']) $(key).onchange = changed;
  for (const key of ['dx','dy']) $(key).onchange = () => setOffset(Number($('dx').value), Number($('dy').value));
  $('reset').onclick = () => {
    if (pending) return;
    if ($('layer').value === selectedOutfit) outfitOffset('@neck', null);
    outfitOffset($('layer').value, null);
    changed();
  };
  $('bind').onclick = () => {
    binding = true;
    $('layer').value = selectedOutfit;
    status('live-status', '请点衣身与颈根相接的中点。');
    changed();
  };
  async function bindGarment(point) {
    binding = false;
    const outfit = selectedOutfit;
    const offset = draft[outfit]?.[outfit] || [0,0];
    const origin = point.map((n, i) => Math.floor(n) - offset[i]);
    const delta = FIT.neck.map((n, i) => n - origin[i]);
    try {
      const {soles} = await request('/soles', {outfit});
      if (outfit !== selectedOutfit) return;
      const placed = soles.map(y => y + delta[1]);
      if (placed.some(y => y !== FIT.sole))
        throw Error('颈根对齐后，脚底会落在 y=' + placed.join('、') + '。模板要求 y=' + FIT.sole + '，请先用服装模板调整原画。');
      if (origin.some((v,i) => v < 0 || v >= [width,height][i])) throw Error('标点在画布外');
      outfitOffset('@neck', origin);
      outfitOffset(outfit, delta.some(Boolean) ? delta : null);
      status('live-status', '衣身颈根和脚底已对齐。');
    } catch (error) { status('live-status', error.message, true); }
    changed();
  }
  stage.onpointerdown = event => {
    if (pending || event.button !== 0) return;
    stage.focus();
    const point = localPoint(event, stage, width, height);
    if (binding) { bindGarment(point); return; }
    if ($('layer').value === selectedOutfit) return;
    drag = {point, offset: chosenOffset(), id: event.pointerId};
    stage.setPointerCapture(event.pointerId);
  };
  stage.onpointermove = event => {
    if (!drag || drag.id !== event.pointerId) return;
    const [x,y] = localPoint(event, stage, width, height);
    setOffset(drag.offset[0] + x - drag.point[0], drag.offset[1] + y - drag.point[1]);
  };
  stage.onpointerup = stage.onpointercancel = stage.onlostpointercapture = () => { drag = null; };
  stage.onkeydown = event => {
    const v = {ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]}[event.key];
    if (!v) return;
    event.preventDefault();
    const [x,y] = chosenOffset(), n = event.shiftKey ? 4 : 1;
    setOffset(x + n*v[0], y + n*v[1]);
  };
  $('save').onclick = async () => {
    pending = true;
    const sent = copy(draft);
    syncLive();
    try {
      const result = await request('/save', {revision, alignment: sent});
      revision = result.revision;
      saved = JSON.stringify(result.alignment);
      if (JSON.stringify(draft) === JSON.stringify(sent)) PORTRAIT.alignment = draft = result.alignment;
      status('live-status', '已保存到本地终端，刷新终端网页可查看。');
    } catch (error) { status('live-status', error.message, true); }
    finally { pending = false; changed(); }
  };
  chooseOutfit();

  let data, template, guide, image, artName = '服装', marks = {};
  let transform = {x:0, y:0, width:cw, height:ch}, bands;
  let templateGeneration = 0, imageGeneration = 0;
  const transformKeys = ['x','y','width','height'];
  const joints = [['chin'], ['neck'], ['shoulder_l','shoulder_r'], ['waist'],
                  ['hip_l','hip_r'], ['crotch'], ['knee_l','knee_r'], ['foot_l','foot_r']];
  function between(value, axis) {
    const end = value <= bands[0][axis] ? bands[0] : value >= bands.at(-1)[axis] ? bands.at(-1) : null;
    if (end) {
      const delta = value - end[axis];
      return {...end, sy:end.sy+delta, ty:end.ty+delta};
    }
    const next = bands.findIndex(band => band[axis] > value);
    const a = bands[next-1], b = bands[next];
    const t = (value - a[axis]) / (b[axis] - a[axis]);
    return {
      sy: a.sy + (b.sy - a.sy) * t, ty: a.ty + (b.ty - a.ty) * t,
      scale: a.scale + (b.scale - a.scale) * t, dx: a.dx + (b.dx - a.dx) * t,
    };
  }
  function displayPoint([x,y]) {
    if (bands) {
      const row = between(y, 'sy');
      return [row.dx + x * row.scale, row.ty];
    }
    return [transform.x + x * transform.width / image.width,
            transform.y + y * transform.height / image.height];
  }
  function paintArtwork(paint) {
    paint.imageSmoothingEnabled = false;
    if (!image) return;
    if (!bands) {
      paint.drawImage(image, transform.x, transform.y, transform.width, transform.height);
      return;
    }
    for (let y = 0; y < ch; y++) {
      const row = between(y + .5, 'ty');
      if (row.sy < 0 || row.sy >= image.height) continue;
      paint.drawImage(image, 0, Math.floor(row.sy), image.width, 1,
                      row.dx, y, image.width * row.scale, 1);
    }
  }
  const tolerance = 2;
  function fitCheck() {
    if (!template) return ['正在载入模板'];
    if (!image) return ['请载入服装 PNG'];
    const missing = Object.keys(template.marks).filter(key => !marks[key]).map(key => data.landmarks[key]);
    if (missing.length) return ['待标记 ' + missing.join('、')];
    return Object.keys(template.marks).filter(key =>
      displayPoint(marks[key]).some((v, i) => Math.abs(v - template.marks[key][i]) > tolerance))
      .map(key => data.landmarks[key] + '偏离模板');
  }
  function drawFit(message) {
    ctx.clearRect(0,0,cw,ch);
    paintArtwork(ctx);
    if (guide) ctx.drawImage(guide, 0, 0);
    if (image && template) for (const [key, point] of Object.entries(marks)) {
      const [x,y] = displayPoint(point), [tx,ty] = template.marks[key];
      ctx.strokeStyle = Math.abs(x-tx) <= tolerance && Math.abs(y-ty) <= tolerance ? '#99f6b8' : '#ff9283';
      ctx.lineWidth = 2;
      ctx.beginPath();ctx.moveTo(tx,ty);ctx.lineTo(x,y);ctx.stroke();
      ctx.beginPath();ctx.arc(x,y,5,0,Math.PI*2);ctx.stroke();
    }
    for (const key of transformKeys) {
      $('art-'+key).value = Math.round(transform[key]*100)/100;
      $('art-'+key).disabled = !!bands;
    }
    const errors = fitCheck();
    for (const key of ['export-art','export-marks','export-guide']) $(key).disabled = !!errors.length;
    status('fit-status', message || (errors.length ? errors.join('、') :
      bands ? '已按各关节分段对齐，可以导出。' : '全部标点已对齐，可以导出。'));
  }
  // The global fit gives incomplete annotations a preview.
  function axisFit(source, target) {
    const n = source.length, x = source.reduce((a,b) => a+b,0)/n, y = target.reduce((a,b) => a+b,0)/n;
    const spread = source.reduce((sum,v) => sum+(v-x)**2,0);
    if (!spread) return null;
    const scale = source.reduce((sum,v,i) => sum+(v-x)*(target[i]-y),0)/spread;
    return scale > 0 ? [y-x*scale, scale] : null;
  }
  function autoFit() {
    bands = null;
    if (!image || !template) return false;
    const keys = Object.keys(marks).filter(key => template.marks[key]);
    if (keys.length < 3) return false;
    const fit = [0,1].map(i => axisFit(keys.map(key => marks[key][i]), keys.map(key => template.marks[key][i])));
    if (fit.some(v => !v)) return false;
    transform = {x:fit[0][0], y:fit[1][0], width:image.width*fit[0][1], height:image.height*fit[1][1]};
    if (joints.flat().some(key => !marks[key])) return true;
    const rows = joints.map(keys => {
      const source = keys.map(key => marks[key]);
      const target = keys.map(key => template.marks[key]);
      const middle = (pair, i) => pair.reduce((sum,p) => sum+p[i],0)/pair.length;
      const scale = keys.length === 2 ? (target[1][0]-target[0][0])/(source[1][0]-source[0][0]) : null;
      return {sy:middle(source,1), ty:middle(target,1), sx:middle(source,0), tx:middle(target,0), scale};
    });
    if (rows.some((row,i) => (row.scale !== null && !(row.scale > 0 && Number.isFinite(row.scale))) ||
        (i > 0 && (row.sy <= rows[i-1].sy || row.ty <= rows[i-1].ty)))) return true;
    for (const [i,row] of rows.entries()) {
      if (row.scale === null) {
        const before = rows.slice(0,i).findLast(r => r.scale !== null);
        const after = rows.slice(i+1).find(r => r.scale !== null);
        row.scale = before && after ?
          before.scale + (after.scale-before.scale)*(row.sy-before.sy)/(after.sy-before.sy) :
          (before || after).scale;
      }
      row.dx = row.tx - row.sx * row.scale;
    }
    bands = rows;
    return true;
  }
  async function chooseTemplate() {
    template = data.templates.find(t => t.id === $('template').value);
    $('template-info').textContent = template.cm + ' cm 名义身高 · ' + template.heads + ' 头身 · 352 × 544';
    const url = '/templates/' + template.id + '.png';
    $('template-download').href = url;
    const next = new Image(), serial = ++templateGeneration;
    next.src = url;
    guide = null;
    bands = null;
    if ($('auto-fit').checked) autoFit();
    drawFit();
    try {
      await next.decode();
      if (serial === templateGeneration) { guide = next; drawFit(); }
    } catch { if (serial === templateGeneration) status('fit-status', '模板图片载入失败', true); }
  }
  async function loadTemplates() {
    const response = await fetch('/templates.json');
    if (!response.ok) throw Error('无法读取人体模板');
    data = await response.json();
    options($('template'), data.templates.map(t => [t.id,t.label]), 'terminal_m');
    options($('point'), Object.entries(data.landmarks), 'neck');
    await chooseTemplate();
  }
  $('template').onchange = chooseTemplate;
  $('art-file').onchange = async () => {
    const file = $('art-file').files[0];
    if (!file) return;
    const serial = ++imageGeneration;
    const next = new Image(), url = URL.createObjectURL(file);
    try {
      next.src = url; await next.decode();
      if (serial !== imageGeneration) return;
      image = next;
      marks = {};
      bands = null;
      artName = file.name.replace(/\.png$/i,'');
      const scale = Math.min(cw / image.width, ch / image.height, 1);
      transform = {x:(cw-image.width*scale)/2,y:ch-image.height*scale,width:image.width*scale,height:image.height*scale};
      drawFit();
    } catch { status('fit-status','无法读取这张 PNG',true); }
    finally { URL.revokeObjectURL(url); }
  };
  $('marks-file').onchange = async () => {
    const file = $('marks-file').files[0];
    if (!file || !image || !template) { status('fit-status','请先载入对应的服装 PNG 和模板',true); return; }
    try {
      const result = JSON.parse(await file.text());
      const stored = result.landmarks;
      if (!stored || !Array.isArray(result.canvas) || result.canvas[0] !== image.width || result.canvas[1] !== image.height ||
          !Object.keys(data.landmarks).every(key => Array.isArray(stored[key]) && stored[key].length === 2 &&
          stored[key].every(Number.isFinite))) throw Error('标点文件与 PNG 尺寸不符，或有未标记的关键点');
      marks = Object.fromEntries(Object.keys(data.landmarks).map(key => [key,stored[key]]));
      if ($('auto-fit').checked) autoFit();
      drawFit();
    } catch (error) { status('fit-status',error.message,true); }
    finally { $('marks-file').value = ''; }
  };
  canvas.onpointerdown = event => {
    if (!image || !template || event.button !== 0) return;
    const key = $('point').value;
    const [x,y] = localPoint(event, canvas, cw, ch);
    const row = bands && between(y, 'ty');
    marks[key] = row ? [(x-row.dx)/row.scale,row.sy] :
      [(x-transform.x)*image.width/transform.width,(y-transform.y)*image.height/transform.height];
    const next = Object.keys(template.marks).find(k => !marks[k]);
    if (next) $('point').value = next;
    if (!next && $('auto-fit').checked) autoFit();
    drawFit();
  };
  $('fit').onclick = () => { drawFit(autoFit() ? '' : '请标出能确定宽度和高度的关键点'); };
  $('auto-fit').onchange = () => { if ($('auto-fit').checked) autoFit(); else bands = null; drawFit(); };
  for (const key of transformKeys) $('art-'+key).onchange = () => {
    const n = Number($('art-'+key).value);
    if (Number.isFinite(n) && (!(key === 'width' || key === 'height') || n > 0)) transform[key] = n;
    $('auto-fit').checked = false;
    drawFit();
  };
  function download(blob, name) {
    const url = URL.createObjectURL(blob), link = document.createElement('a');
    link.href = url;link.download = name;link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  $('export-art').onclick = () => {
    if (fitCheck().length) return;
    const out = document.createElement('canvas');out.width=cw;out.height=ch;
    const paint = out.getContext('2d');
    paintArtwork(paint);
    out.toBlob(blob => { if (blob) download(blob,artName+'-'+template.id+'.png'); },'image/png');
  };
  $('export-marks').onclick = () => {
    if (fitCheck().length) return;
    const landmarks = Object.fromEntries(Object.keys(data.landmarks).map(key =>
      [key, displayPoint(marks[key]).map(n => Math.round(n*100)/100)]));
    download(new Blob([JSON.stringify({template:template.id,canvas:[cw,ch],landmarks},null,2)+'\n'],
                       {type:'application/json'}),artName+'-'+template.id+'.json');
  };
  $('export-guide').onclick = () => {
    if (fitCheck().length) return;
    canvas.toBlob(blob => { if (blob) download(blob,artName+'-'+template.id+'-标注.png'); },'image/png');
  };
  let templatesLoaded;
  function tab(fit) {
    $('live').hidden = fit;
    $('fitting').hidden = !fit;
    $('live-tab').setAttribute('aria-pressed', !fit);
    $('fit-tab').setAttribute('aria-pressed', fit);
    if (fit && !templatesLoaded) templatesLoaded = loadTemplates().catch(error => {
      status('fit-status', error.message, true);
      templatesLoaded = null;
    });
  }
  $('live-tab').onclick = () => tab(false);
  $('fit-tab').onclick = () => tab(true);
  window.addEventListener('beforeunload', event => {
    if (JSON.stringify(draft) !== saved) { event.preventDefault(); event.returnValue = ''; }
  });
})();
