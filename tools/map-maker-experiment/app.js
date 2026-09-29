'use strict';
const $ = id => document.getElementById(id), M = MapMaker;
const canvas = $('map'), ctx = canvas.getContext('2d'), raster = document.createElement('canvas');
const storageKey = 'san9-map-maker-experiment-v1';
let doc = M.generate(128, 96, 927), analysis, tool = 'plain', history = [], future = [];
let camera = { x: 0, y: 0, scale: 5 }, gesture = null, saveTimer;
const regionColors = ['#e2bb6b', '#cf889b', '#8cacdf', '#b8cc73', '#b895d0', '#6cbcb3'];
function status(message) { $('status').textContent = message; }
function remember() { history.push(JSON.stringify(doc)); if (history.length > 20) history.shift(); future = []; }
function persist() {
    clearTimeout(saveTimer);
    saveTimer = setTimeout(() => {
        try { localStorage.setItem(storageKey, JSON.stringify(doc)); $('storage').textContent = '已自动保存到本机 · 建议下载工程备份'; }
        catch { $('storage').textContent = '本机保存不可用，请下载工程备份'; }
    }, 250);
}
function synchronize() {
    $('name').value = doc.name; $('width').value = doc.width; $('height').value = doc.height; $('seed').value = doc.seed;
    $('map-title').textContent = doc.name; $('dimensions').textContent = `${doc.width} × ${doc.height}`;
    $('city-count').textContent = doc.cities.length; $('undo').disabled = !history.length; $('redo').disabled = !future.length;
}
function rebuild() {
    analysis = M.analyze(doc);
    raster.width = doc.width; raster.height = doc.height;
    const r = raster.getContext('2d');
    doc.cells.forEach((cell, i) => {
        const x = i % doc.width, y = Math.floor(i / doc.width);
        r.fillStyle = M.terrain[cell.terrain].color; r.fillRect(x, y, 1, 1);
        if ($('regions').checked && analysis.regions[i] >= 0) {
            r.globalAlpha = 0.52; r.fillStyle = regionColors[analysis.regions[i] % regionColors.length]; r.fillRect(x, y, 1, 1); r.globalAlpha = 1;
        }
        const above = y ? doc.cells[i - doc.width].elevation : cell.elevation;
        const delta = cell.elevation - above;
        r.fillStyle = delta > 0 ? `rgba(255,249,215,${Math.min(.27, delta * .06)})` : `rgba(0,25,20,${Math.min(.25, -delta * .06)})`;
        r.fillRect(x, y, 1, 1);
    });
    synchronize(); renderIssues(); draw();
}
function draw() {
    const width = canvas.clientWidth, height = canvas.clientHeight, ratio = devicePixelRatio || 1;
    if (canvas.width !== Math.round(width * ratio) || canvas.height !== Math.round(height * ratio)) {
        canvas.width = Math.round(width * ratio); canvas.height = Math.round(height * ratio);
    }
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0); ctx.clearRect(0, 0, width, height);
    ctx.save(); ctx.translate(camera.x, camera.y); ctx.scale(camera.scale, camera.scale); ctx.imageSmoothingEnabled = false;
    ctx.drawImage(raster, 0, 0);
    ctx.lineWidth = 1 / camera.scale; ctx.strokeStyle = '#d5dcbb88'; ctx.beginPath();
    for (let y = 0; y < doc.height; y++) for (let x = 0; x < doc.width; x++) {
        const i = y * doc.width + x, water = doc.cells[i].terrain === 'water';
        if (x + 1 < doc.width && water !== (doc.cells[i + 1].terrain === 'water')) { ctx.moveTo(x + 1, y); ctx.lineTo(x + 1, y + 1); }
        if (y + 1 < doc.height && water !== (doc.cells[i + doc.width].terrain === 'water')) { ctx.moveTo(x, y + 1); ctx.lineTo(x + 1, y + 1); }
    }
    ctx.stroke();
    if ($('grid').checked && camera.scale >= 5) {
        ctx.strokeStyle = '#102a2733'; ctx.beginPath();
        for (let x = 0; x <= doc.width; x++) { ctx.moveTo(x, 0); ctx.lineTo(x, doc.height); }
        for (let y = 0; y <= doc.height; y++) { ctx.moveTo(0, y); ctx.lineTo(doc.width, y); }
        ctx.stroke();
    }
    ctx.restore();
    for (const city of doc.cities) {
        const x = camera.x + (city.x + .5) * camera.scale, y = camera.y + (city.y + .5) * camera.scale;
        ctx.fillStyle = '#eee0b8'; ctx.strokeStyle = '#27362b'; ctx.lineWidth = 2;
        ctx.beginPath(); ctx.arc(x, y, 5, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
        ctx.font = '12px "Microsoft YaHei", sans-serif'; ctx.textAlign = 'center'; ctx.lineWidth = 4;
        ctx.strokeText(city.name, x, y - 12); ctx.fillText(city.name, x, y - 12);
    }
}
function fit() {
    camera.scale = Math.max(.5, Math.min((canvas.clientWidth - 60) / doc.width, (canvas.clientHeight - 90) / doc.height));
    camera.x = (canvas.clientWidth - doc.width * camera.scale) / 2;
    camera.y = (canvas.clientHeight - doc.height * camera.scale) / 2; draw();
}
function renderIssues() {
    $('issues').replaceChildren();
    if (!analysis.issues.length) $('issues').textContent = '✓ 实验陆路检查通过。所有城市互通，可通行格均已划入地域。';
    analysis.issues.forEach(issue => {
        const el = document.createElement(issue.x === undefined ? 'p' : 'button'); el.textContent = issue.message;
        if (issue.x !== undefined) el.onclick = () => {
            camera.x = canvas.clientWidth / 2 - (issue.x + .5) * camera.scale;
            camera.y = canvas.clientHeight / 2 - (issue.y + .5) * camera.scale; draw();
        };
        $('issues').append(el);
    });
}
for (const [id, item] of Object.entries({ ...M.terrain, city: { name: '设城', color: '#efddb0' }, erase: { name: '移除城市', color: '#c88776' } })) {
    const button = document.createElement('button'); button.dataset.tool = id;
    const swatch = document.createElement('span'); swatch.className = 'swatch'; swatch.style.background = item.color;
    button.append(swatch, item.name); button.onclick = () => {
        tool = id; document.querySelectorAll('[data-tool]').forEach(b => b.classList.toggle('active', b.dataset.tool === tool));
    }; button.classList.toggle('active', id === tool); $('tools').append(button);
}
function point(event) {
    const rect = canvas.getBoundingClientRect();
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
}
function cellAt(p) { return { x: Math.floor((p.x - camera.x) / camera.scale), y: Math.floor((p.y - camera.y) / camera.scale) }; }
function inside(c) { return c.x >= 0 && c.y >= 0 && c.x < doc.width && c.y < doc.height; }
canvas.oncontextmenu = event => event.preventDefault();
canvas.onpointerdown = event => {
    if (gesture) return;
    const p = point(event), cell = cellAt(p);
    if (event.button === 1 || event.button === 2) { gesture = { pan: true, p }; canvas.setPointerCapture(event.pointerId); return; }
    if (event.button !== 0 || !inside(cell)) return;
    const index = doc.cities.findIndex(c => c.x === cell.x && c.y === cell.y);
    if (tool === 'city') {
        if (!M.terrain[doc.cells[cell.y * doc.width + cell.x].terrain].passable) { status('城市需要可通行陆地，请先刷平原或道路。'); return; }
        const name = $('city-name').value.trim();
        if (index >= 0 && !name) { $('city-name').value = doc.cities[index].name; status('已选中城市，修改名称后再次点击该城。'); return; }
        remember();
        if (index >= 0) doc.cities[index].name = name;
        else doc.cities.push({ id: crypto.randomUUID(), name: name || `新城 ${doc.cities.length + 1}`, ...cell });
        rebuild(); persist(); return;
    }
    if (tool === 'erase') { if (index >= 0) { remember(); doc.cities.splice(index, 1); rebuild(); persist(); } return; }
    remember(); gesture = { pan: false, last: cell }; canvas.setPointerCapture(event.pointerId);
    M.paint(doc, cell, cell, Number($('radius').value), tool); rebuild();
};
canvas.onpointermove = event => {
    const p = point(event), cell = cellAt(p);
    $('position').textContent = inside(cell) ? `${cell.x}, ${cell.y} · ${M.terrain[doc.cells[cell.y * doc.width + cell.x].terrain].name}` : '地图之外';
    if (!gesture) return;
    if (gesture.pan) { camera.x += p.x - gesture.p.x; camera.y += p.y - gesture.p.y; gesture.p = p; draw(); }
    else if (inside(cell)) { M.paint(doc, gesture.last, cell, Number($('radius').value), tool); gesture.last = cell; rebuild(); }
};
function endGesture() { if (gesture && !gesture.pan) { persist(); status('已完成一笔，可撤销。'); } gesture = null; }
canvas.onpointerup = endGesture; canvas.onpointercancel = endGesture; canvas.onlostpointercapture = endGesture;
canvas.addEventListener('wheel', event => {
    event.preventDefault(); const p = point(event), old = camera.scale;
    camera.scale = Math.max(.5, Math.min(40, old * Math.exp(-event.deltaY * .001)));
    camera.x = p.x - (p.x - camera.x) * camera.scale / old; camera.y = p.y - (p.y - camera.y) * camera.scale / old; draw();
}, { passive: false });
$('generate').onclick = () => {
    try {
        const next = M.generate(Number($('width').value), Number($('height').value), Number($('seed').value), Number($('sea').value) / 100);
        next.name = $('name').value.trim() || '未命名山河'; remember(); doc = next; rebuild(); fit(); persist(); status('新地貌已生成；可撤销回到上一幅地图。');
    } catch (error) { status(error.message); }
};
$('name').onchange = () => { remember(); doc.name = $('name').value.trim() || '未命名山河'; synchronize(); persist(); };
$('sea').oninput = () => $('sea-value').textContent = `${$('sea').value}%`;
$('radius').oninput = () => $('radius-value').textContent = `${$('radius').value} 格`;
$('regions').onchange = rebuild; $('grid').onchange = draw; $('fit').onclick = fit;
$('check').onclick = () => { rebuild(); status(`检查完成：${analysis.issues.length} 项待确认。`); };
function travel(source, target) {
    if (!source.length) return; target.push(JSON.stringify(doc)); doc = JSON.parse(source.pop()); rebuild(); persist();
}
$('undo').onclick = () => travel(history, future); $('redo').onclick = () => travel(future, history);
document.addEventListener('keydown', event => {
    if (event.target.matches('input,textarea') || gesture) return;
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') { event.preventDefault(); (event.shiftKey ? $('redo') : $('undo')).click(); }
});
$('save').onclick = () => {
    const text = JSON.stringify(M.validate(doc), null, 2), url = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
    const link = document.createElement('a'); link.href = url; link.download = 'map-project.json'; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    status('工程已交给浏览器下载，可用“打开工程”继续编辑。');
};
$('load').onclick = () => $('file').click();
$('file').onchange = async event => {
    const file = event.target.files[0]; if (!file) return;
    try {
        if (file.size > 12 * 1024 * 1024) throw new Error('实验工程文件不能超过 12 MiB。');
        const next = M.validate(JSON.parse(await file.text())); remember(); doc = next; rebuild(); fit(); persist(); status('工程已打开。');
    } catch (error) { status(`打开失败：${error.message}`); }
    event.target.value = '';
};
try {
    const saved = localStorage.getItem(storageKey);
    if (saved) { doc = M.validate(JSON.parse(saved)); status('已恢复上次自动保存的工程。'); }
} catch { $('storage').textContent = '未能恢复本机工程，请使用下载备份'; }
new ResizeObserver(draw).observe($('viewport'));
rebuild(); requestAnimationFrame(fit);
