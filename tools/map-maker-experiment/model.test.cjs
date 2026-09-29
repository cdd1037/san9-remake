const { test } = require('node:test');
const assert = require('node:assert/strict');
const M = require('./model.js');
function flat() { const d = M.generate(16, 16, 1); d.cells.forEach(c => { c.terrain = 'plain'; c.elevation = 1; }); return d; }
test('同输入可复现，工程 JSON 往返保持全部作者状态', () => {
    const d = M.generate(80, 48, 927); d.cities.push({ id: 'a', name: '测试城', x: 3, y: 4 });
    assert.deepEqual(M.validate(JSON.parse(JSON.stringify(d))), d);
    assert.deepEqual(M.generate(80, 48, 927).cells, d.cells);
    assert.notDeepEqual(M.generate(80, 48, 928).cells, d.cells);
});
test('快速拖动不漏格，笔刷在地图边缘裁剪', () => {
    const d = flat(); M.paint(d, { x: 0, y: 0 }, { x: 15, y: 15 }, 0, 'road');
    for (let n = 0; n < 16; n++) assert.equal(d.cells[n * 17].terrain, 'road');
    M.paint(d, { x: 0, y: 0 }, { x: 0, y: 0 }, 3, 'water');
    assert.equal(d.cells.length, 256); assert.equal(d.cells[3].terrain, 'water');
    assert.equal(d.cells[4].terrain, 'plain'); M.validate(d);
});
test('陆路被水隔断会报错，架设实验道路后重新连通并重算地域', () => {
    const d = flat(); d.cities = [{ id: 'a', name: '西城', x: 2, y: 5 }, { id: 'b', name: '东城', x: 12, y: 5 }];
    M.paint(d, { x: 8, y: 0 }, { x: 8, y: 15 }, 0, 'water');
    assert.ok(M.analyze(d).issues.some(i => i.message.includes('没有陆路')));
    M.paint(d, { x: 8, y: 5 }, { x: 8, y: 5 }, 0, 'road');
    assert.equal(M.analyze(d).issues.length, 0);
    assert.equal(M.analyze(d).regions[5 * 16 + 12], 1);
});
test('对角陆地不视为相连，孤岛不被静默分配地域', () => {
    const d = flat(); d.cells.forEach(c => { c.terrain = 'water'; c.elevation = 0; });
    for (const i of [0, 17]) d.cells[i] = { terrain: 'plain', elevation: 1 };
    d.cities = [{ id: 'a', name: '孤城', x: 0, y: 0 }];
    assert.equal(M.analyze(d).regions[17], -1);
    assert.ok(M.analyze(d).issues.some(i => i.message.includes('1 个可通行格')));
});
test('拒绝不完整工程、非法地形、重叠城市和重复标识', () => {
    for (const mutate of [d => d.cells.pop(), d => d.cells[0].terrain = '__proto__', d => d.width = 16.5,
        d => d.cells[0].elevation = -1, d => d.cities.push({ id: 'a', name: '越界', x: 16, y: 0 }),
        d => d.cities.push({ id: 'a', name: '一', x: 0, y: 0 }, { id: 'a', name: '二', x: 1, y: 0 }),
        d => d.cities.push({ id: 'a', name: '一', x: 0, y: 0 }, { id: 'b', name: '二', x: 0, y: 0 })]) {
        const d = flat(); mutate(d); assert.throws(() => M.validate(d));
    }
});
test('绘制淹没城市保留作者对象，并定位阻断问题', () => {
    const d = flat(); d.cities = [{ id: 'a', name: '城', x: 2, y: 2 }];
    M.paint(d, { x: 2, y: 2 }, { x: 2, y: 2 }, 0, 'water');
    assert.equal(d.cities.length, 1); assert.ok(M.analyze(d).issues.some(i => i.x === 2 && i.y === 2));
});
