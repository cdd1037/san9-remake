(function (root) {
    'use strict';
    const terrain = {
        water: { name: '水域', color: '#397f9a', passable: false },
        plain: { name: '平原', color: '#a8b879', passable: true },
        forest: { name: '森林', color: '#568865', passable: true },
        mountain: { name: '山地', color: '#978c77', passable: false },
        road: { name: '道路', color: '#d7bc83', passable: true }
    };
    function hash(x, y, seed) {
        let n = Math.imul(x + seed, 374761393) ^ Math.imul(y, 668265263);
        n = Math.imul(n ^ (n >>> 13), 1274126177);
        return ((n ^ (n >>> 16)) >>> 0) / 4294967295;
    }
    function noise(x, y, seed) {
        const a = Math.floor(x), b = Math.floor(y);
        const smooth = t => t * t * (3 - 2 * t);
        const u = smooth(x - a), v = smooth(y - b);
        const mix = (p, q, t) => p + (q - p) * t;
        return mix(mix(hash(a, b, seed), hash(a + 1, b, seed), u),
            mix(hash(a, b + 1, seed), hash(a + 1, b + 1, seed), u), v);
    }
    function generate(width, height, seed, sea = 0.36) {
        if (![width, height].every(n => Number.isInteger(n) && n >= 16 && n <= 256) ||
            !Number.isSafeInteger(seed) || !Number.isFinite(sea) || sea < 0 || sea > 1)
            throw new Error('尺寸须为 16–256 的整数，种子须为安全整数。');
        const cells = [];
        for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
            const edge = Math.min(x / width, y / height, (width - 1 - x) / width, (height - 1 - y) / height);
            const value = 0.7 * noise(x / 24, y / 24, seed) + 0.3 * noise(x / 8, y / 8, seed + 71)
                - Math.max(0, 0.13 - edge) * 2;
            const kind = value < sea ? 'water' : value > 0.68 ? 'mountain' : noise(x / 5, y / 5, seed + 137) > 0.58 ? 'forest' : 'plain';
            cells.push({ terrain: kind, elevation: kind === 'water' ? 0 : Math.max(1, Math.round(value * 12)) });
        }
        return { format: 'san9-map-authoring-experiment', version: 1, name: '未命名山河', width, height, seed, cells, cities: [] };
    }
    function validate(doc) {
        if (!doc || doc.format !== 'san9-map-authoring-experiment' || doc.version !== 1) throw new Error('不是受支持的实验工程。');
        if (![doc.width, doc.height].every(n => Number.isInteger(n) && n >= 16 && n <= 256)) throw new Error('地图尺寸不合法。');
        if (typeof doc.name !== 'string' || doc.name.length > 100 || !doc.name.trim() || !Number.isSafeInteger(doc.seed)) throw new Error('工程名称或种子不合法。');
        if (!Array.isArray(doc.cells) || doc.cells.length !== doc.width * doc.height || doc.cells.some(c =>
            !c || !Object.hasOwn(terrain, c.terrain) || !Number.isInteger(c.elevation) || c.elevation < 0 || c.elevation > 12 ||
            (c.terrain === 'water' && c.elevation !== 0))) throw new Error('地形或高程数据不合法。');
        if (!Array.isArray(doc.cities) || doc.cities.length > doc.cells.length) throw new Error('城市列表不合法。');
        const ids = new Set(), locations = new Set();
        for (const city of doc.cities) {
            if (!city || typeof city.id !== 'string' || !city.id || ids.has(city.id) || typeof city.name !== 'string' ||
                !city.name.trim() || city.name.length > 60 || !Number.isInteger(city.x) || !Number.isInteger(city.y) ||
                city.x < 0 || city.y < 0 || city.x >= doc.width || city.y >= doc.height) throw new Error('城市属性或引用不合法。');
            const location = city.y * doc.width + city.x;
            if (locations.has(location)) throw new Error('城市位置重叠。');
            ids.add(city.id); locations.add(location);
        }
        return doc;
    }
    function paint(doc, from, to, radius, kind) {
        if (!Object.hasOwn(terrain, kind)) throw new Error('未知地形。');
        const steps = Math.max(Math.abs(to.x - from.x), Math.abs(to.y - from.y), 1);
        for (let s = 0; s <= steps; s++) {
            const cx = Math.round(from.x + (to.x - from.x) * s / steps), cy = Math.round(from.y + (to.y - from.y) * s / steps);
            for (let y = Math.max(0, cy - radius); y <= Math.min(doc.height - 1, cy + radius); y++)
                for (let x = Math.max(0, cx - radius); x <= Math.min(doc.width - 1, cx + radius); x++) {
                    if ((x - cx) ** 2 + (y - cy) ** 2 > radius ** 2) continue;
                    const cell = doc.cells[y * doc.width + x];
                    cell.terrain = kind;
                    cell.elevation = kind === 'water' ? 0 : kind === 'mountain' ? 9 : Math.max(1, Math.min(7, cell.elevation));
                }
        }
    }
    function neighbors(i, width, height) {
        const result = [], x = i % width, y = Math.floor(i / width);
        if (x > 0) result.push(i - 1);
        if (x + 1 < width) result.push(i + 1);
        if (y > 0) result.push(i - width);
        if (y + 1 < height) result.push(i + width);
        return result;
    }
    // 实验采用四邻接陆路；地域按多源广度优先传播派生，不写入作者工程。
    function analyze(doc) {
        validate(doc);
        const regions = new Int32Array(doc.cells.length).fill(-1), queue = [], issues = [];
        doc.cities.forEach((city, index) => {
            const i = city.y * doc.width + city.x;
            if (!terrain[doc.cells[i].terrain].passable) issues.push({ x: city.x, y: city.y, message: `${city.name}位于不可通行地形` });
            else { regions[i] = index; queue.push(i); }
        });
        for (let head = 0; head < queue.length; head++) {
            const i = queue[head];
            for (const next of neighbors(i, doc.width, doc.height))
                if (regions[next] === -1 && terrain[doc.cells[next].terrain].passable) {
                    regions[next] = regions[i]; queue.push(next);
                }
        }
        if (doc.cities.length < 2) issues.push({ message: '至少放置两座城市，才能检验城市之间的陆路。' });
        const start = doc.cities.find(c => terrain[doc.cells[c.y * doc.width + c.x].terrain].passable);
        if (start) {
            const visited = new Uint8Array(doc.cells.length), flood = [start.y * doc.width + start.x];
            visited[flood[0]] = 1;
            for (let head = 0; head < flood.length; head++)
                for (const n of neighbors(flood[head], doc.width, doc.height))
                    if (!visited[n] && terrain[doc.cells[n].terrain].passable) { visited[n] = 1; flood.push(n); }
            for (const city of doc.cities)
                if (terrain[doc.cells[city.y * doc.width + city.x].terrain].passable && !visited[city.y * doc.width + city.x])
                    issues.push({ x: city.x, y: city.y, message: `${city.name}与${start.name}没有陆路连接` });
        }
        const unassigned = doc.cells.filter((c, i) => terrain[c.terrain].passable && regions[i] === -1).length;
        if (unassigned) issues.push({ message: `${unassigned} 个可通行格没有城市可达，尚未划入地域。` });
        return { regions, issues };
    }
    const api = { terrain, generate, validate, paint, analyze };
    if (typeof module !== 'undefined') module.exports = api;
    else root.MapMaker = api;
})(globalThis);
