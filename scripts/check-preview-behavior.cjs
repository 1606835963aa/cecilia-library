const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ts = require('typescript');
const root = path.resolve(__dirname, '..');
const results = [];
const compile = name => ts.transpileModule(fs.readFileSync(path.join(root, 'src/preview', name), 'utf8'), {
  compilerOptions: { target: ts.ScriptTarget.ES2020 }, reportDiagnostics: true
}).outputText;
const delay = () => new Promise(resolve => setTimeout(resolve, 5));
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; };
async function check(name, run) {
  try { await run(); results.push({ name, status: 'PASS' }); }
  catch (error) { results.push({ name, status: 'FAIL', error: error.stack }); }
}
function decodeCell(value) {
  const match = /^([A-Z]+)(\d+)$/.exec(value); let c = 0;
  for (const letter of match[1]) c = c * 26 + letter.charCodeAt(0) - 64;
  return { r: Number(match[2]) - 1, c: c - 1 };
}
function encodeCell({ r, c }) {
  let prefix = ''; for (let n = c + 1; n > 0; n = Math.floor((n - 1) / 26)) prefix = String.fromCharCode(65 + (n - 1) % 26) + prefix;
  return prefix + (r + 1);
}
function makeWorker(book, fetchOverride, realAPI) {
  const messages = []; let formatted = 0, reads = 0, readOptions;
  const context = {
    AbortController, importScripts() {}, postMessage: value => messages.push(value),
    fetch: fetchOverride || (async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(0) })),
    XLSX: { read: (data, options) => { reads++; readOptions = options; return realAPI ? realAPI.read(data, options) : book; }, utils: realAPI ? {
      ...realAPI.utils, format_cell: cell => { formatted++; return realAPI.utils.format_cell(cell); }
    } : {
      encode_cell: encodeCell, decode_cell: decodeCell,
      decode_range: value => { const [start, end = start] = value.split(':'); return { s: decodeCell(start), e: decodeCell(end) }; },
      format_cell: cell => { formatted++; return String(cell.v ?? ''); }
    } }
  };
  vm.createContext(context); vm.runInContext(compile('sheet-worker.ts'), context);
  return { messages, send: data => context.onmessage({ data }), last: () => messages[messages.length - 1], formatted: () => formatted, reads: () => reads, readOptions: () => readOptions };
}
const book = { SheetNames: ['第一表', '第二表', '无匹配表'], Sheets: {
  '第一表': { '!ref': 'A1:A2', A1: { v: 'target one' }, A2: { v: 'target two' } },
  '第二表': { '!ref': 'A1:A3', A1: { v: 'target three' }, A2: { v: 'target four' }, A3: { v: 'other' } },
  '无匹配表': { '!ref': 'A1', A1: { v: 'other' } }
} };
class Element {
  constructor(tagName) {
    this.tagName = tagName; this.childNodes = []; this.parent = undefined; this.dataset = {}; this.style = {};
    this.attributes = []; this.className = ''; this.textContent = ''; this.scrollLeft = 0; this.scrollTop = 0; this.colSpan = 1; this.rowSpan = 1;
    this.listeners = {}; this.classList = { add: name => { this.className += ' ' + name; } };
  }
  append(...items) { for (const item of items) { item.remove(); item.parent = this; this.childNodes.push(item); } }
  prepend(...items) { for (const item of [...items].reverse()) { item.remove(); item.parent = this; this.childNodes.unshift(item); } }
  remove() { if (this.parent) { this.parent.childNodes = this.parent.childNodes.filter(item => item !== this); this.parent = undefined; } }
  replaceChildren(...items) { for (const item of this.childNodes) item.parent = undefined; this.childNodes = []; this.append(...items); }
  setAttribute(name, value) { this.attributes.push({ name, value }); }
  removeAttribute(name) { this.attributes = this.attributes.filter(attr => attr.name !== name); }
  addEventListener(name, handler) { this.listeners[name] = handler; }
  scrollIntoView() {}
  querySelectorAll(selector) {
    const matches = element => selector === '*' || selector.split(',').some(part => {
      if (part.startsWith('.')) return element.className.split(' ').includes(part.slice(1));
      const [tagName, className] = part.split('.');
      return element.tagName === tagName && (!className || element.className.split(' ').includes(className));
    });
    const all = []; for (const item of this.childNodes) { if (matches(item)) all.push(item); all.push(...item.querySelectorAll(selector)); } return all;
  }
  querySelector(selector) { return this.querySelectorAll(selector)[0]; }
}
function makeReader(options = {}) {
  const container = new Element('div'), head = new Element('head'), messages = [], workers = [], timers = new Set();
  let receive; const listeners = {};
  const window = {
    scrollX: 0, scrollY: 0, JSZip: options.components === false ? undefined : {},
    chrome: { webview: { postMessage: message => messages.push(message), addEventListener: (_, handler) => { receive = handler; } } },
    addEventListener: (name, handler) => { listeners[name] = handler; }, getSelection: () => ({ removeAllRanges() {} }),
    scrollTo(x, y) { window.scrollX = x; window.scrollY = y; },
    setTimeout(handler, milliseconds) { const timer = setTimeout(() => { timers.delete(timer); handler(); }, milliseconds); timers.add(timer); return timer; },
    docx: options.components === false ? undefined : { renderAsync: options.render || (async (_, target) => { const p = new Element('p'); p.textContent = 'word'; target.append(p); }) }
  };
  class Worker {
    constructor() { this.messages = []; this.terminated = false; workers.push(this); }
    postMessage(message) { this.messages.push(message); }
    terminate() { this.terminated = true; }
  }
  const context = { window, document: { head, getElementById: () => container, createElement: tag => new Element(tag) }, Worker,
    AbortController, clearTimeout: timer => { clearTimeout(timer); timers.delete(timer); },
    fetch: options.fetch || (async () => ({ ok: true, blob: async () => ({}) }))
  };
  vm.createContext(context); vm.runInContext(compile('reader.ts'), context);
  return { container, head, messages, workers, window, listeners, send: message => receive({ data: message }),
    dispose: () => { listeners.pagehide(); for (const timer of timers) clearTimeout(timer); timers.clear(); } };
}
const open = (id, kind = 'spreadsheet', position) => ({ type: 'open', id, kind, url: id, extension: kind === 'word' ? 'docx' : 'xlsx', position });
async function main() {
  const realAPI = require(path.join(root, 'components/sheet/0.20.3/xlsx.js'));
  const asArray = data => data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength);
  const cellValue = cell => !cell ? '' : cell.v === undefined || cell.v === null ? (cell.f ? '=' + cell.f : '') : (cell.w ?? realAPI.utils.format_cell(cell));
  const fixtures = fs.readdirSync(path.join(root, 'tests/fixtures')).filter(filename => /\.(csv|xls|xlsx|xlt|xltx|xlsm|xltm|xla|xlam|dbf|prn|dif)$/.test(filename));
  for (const filename of fixtures) await check('real SheetJS lazy formatting matches eager output: ' + filename, async () => {
    const extension = path.extname(filename).slice(1), bytes = asArray(fs.readFileSync(path.join(root, 'tests/fixtures', filename)));
    const before = realAPI.read(bytes, { type: 'array', cellDates: true, cellNF: true, cellText: true, raw: extension === 'csv', codepage: 936 });
    const worker = makeWorker(undefined, async () => ({ ok: true, arrayBuffer: async () => bytes }), realAPI);
    await worker.send({ ...open('fixture'), extension }); assert.equal(worker.readOptions().cellText, false);
    assert.equal(worker.readOptions().cellNF, true); assert.equal(worker.readOptions().cellDates, true);
    for (let sheet = 0; sheet < before.SheetNames.length; sheet++) {
      const original = before.Sheets[before.SheetNames[sheet]], range = original['!ref'] ? realAPI.utils.decode_range(original['!ref']) : { e: { r: 0, c: 0 } };
      for (let start = 0; start <= range.e.r; start += 200) for (let firstColumn = 0; firstColumn <= range.e.c; firstColumn += 50) {
        await worker.send({ type: 'page', sheet, start, firstColumn }); const page = worker.last(); assert.equal(page.error, undefined);
        for (const cell of page.cells) {
          const merge = (original['!merges'] || []).find(merge => cell.row >= merge.s.r && cell.row <= merge.e.r && cell.col >= merge.s.c && cell.col <= merge.e.c);
          const address = merge ? merge.s : { r: cell.row, c: cell.col };
          assert.equal(cell.value, cellValue(original[realAPI.utils.encode_cell(address)]), filename + ':' + before.SheetNames[sheet] + ':' + encodeCell(address));
        }
      }
    }
  });
  for (const date1904 of [false, true]) await check('real SheetJS dates, number formats, leading zeroes and formula cache match with date1904=' + date1904, async () => {
    const book = realAPI.utils.book_new(), sheet = realAPI.utils.aoa_to_sheet([['0123', 1234.5, 0, 42, 0, undefined]]);
    const date = new Date(Date.UTC(2026, 9, 12)), origin = Date.UTC(date1904 ? 1904 : 1899, date1904 ? 0 : 11, date1904 ? 1 : 30);
    book.Workbook = { WBProps: { date1904 } }; sheet.B1.z = '#,##0.00';
    sheet.C1 = { t: 'n', v: (date.getTime() - origin) / 86400000, z: 'yyyy-mm-dd' };
    sheet.D1.f = 'SUM(20,22)'; sheet.E1.f = '1-1'; sheet.F1 = { t: 'n', f: 'SUM(20,22)', z: '#,##0.00' }; sheet['!ref'] = 'A1:F1';
    realAPI.utils.book_append_sheet(book, sheet, 'format'); const bytes = asArray(realAPI.write(book, { type: 'buffer', bookType: 'xlsx' }));
    const worker = makeWorker(undefined, async () => ({ ok: true, arrayBuffer: async () => bytes }), realAPI); await worker.send(open('formats'));
    assert.deepEqual(Array.from(worker.last().cells, cell => cell.value), ['0123', '1,234.50', '2026-10-12', '42', '0', '=SUM(20,22)']);
    assert.equal(worker.last().formulaMissing, 1); assert.equal(worker.last().cells[5].formulaOnly, true);
  });
  await check('search iterates forward and backward across worksheets with wraparound', async () => {
    const worker = makeWorker(book); await worker.send(open('test'));
    for (const [backward, expected] of [[false, 1], [false, 2], [false, 3], [false, 4], [false, 1], [true, 4]]) {
      await worker.send({ type: 'find', query: 'TARGET', sheet: worker.last().sheet, backward });
      assert.equal(worker.last().foundIndex, expected); assert.equal(worker.last().foundCount, 4);
    }
  });
  await check('manual worksheet switch relocates the same query to the current worksheet', async () => {
    const worker = makeWorker(book); await worker.send(open('test')); await worker.send({ type: 'find', query: 'target', sheet: 0 });
    await worker.send({ type: 'page', sheet: 1, start: 0, firstColumn: 0 });
    await worker.send({ type: 'find', query: 'target', sheet: 1 }); assert.equal(worker.last().sheet, 1); assert.equal(worker.last().foundIndex, 3);
    await worker.send({ type: 'page', sheet: 1, start: 0, firstColumn: 0 });
    await worker.send({ type: 'find', query: 'target', sheet: 1, backward: true }); assert.equal(worker.last().foundIndex, 4);
  });
  await check('first backward search uses the last match in the current worksheet', async () => {
    const worker = makeWorker(book); await worker.send(open('test')); await worker.send({ type: 'find', query: 'target', sheet: 0, backward: true });
    assert.equal(worker.last().foundIndex, 2);
  });
  await check('query cache avoids rescanning other worksheets on repeated searches', async () => {
    const worker = makeWorker(book); await worker.send(open('test')); await worker.send({ type: 'find', query: 'target', sheet: 0 });
    const before = worker.formatted(); await worker.send({ type: 'find', query: 'target', sheet: 0 });
    assert.equal(worker.formatted() - before, 2); // Only the two visible cells are formatted again.
  });
  await check('empty search yields 0/0 without relocating the current page', async () => {
    const worker = makeWorker(book); await worker.send(open('test', 'spreadsheet', { sheet: 1, start: 2 }));
    await worker.send({ type: 'find', query: '', sheet: 1 });
    assert.equal(worker.last().foundCount, 0); assert.equal(worker.last().start, 2); assert.equal(worker.last().selected, undefined);
  });
  await check('saved sheet/row/column position is used on the first worker page', async () => {
    const worker = makeWorker({ SheetNames: ['long'], Sheets: { long: { '!ref': 'A1:BG400', A1: { v: 'start' }, BG400: { v: 'end' } } } });
    await worker.send(open('test', 'spreadsheet', { sheet: 0, start: 200, firstColumn: 50 }));
    assert.equal(worker.last().start, 200); assert.equal(worker.last().firstColumn, 50); assert.equal(worker.last().totalColumns, 59);
  });
  await check('clipped merged cells retain their source value and remaining span', async () => {
    const worker = makeWorker({ SheetNames: ['merged'], Sheets: { merged: { '!ref': 'A1:H205', A199: { v: 'merged content' }, '!merges': [{ s: { r: 198, c: 0 }, e: { r: 202, c: 2 } }] } } });
    await worker.send(open('test', 'spreadsheet', { start: 200, firstColumn: 1 }));
    const cell = worker.last().cells.find(cell => cell.row === 200 && cell.col === 1);
    assert.equal(cell.value, 'merged content'); assert.equal(cell.spanRows, 3); assert.equal(cell.spanCols, 2);
  });
  await check('expired worker fetch cannot replace a newer workbook', async () => {
    const stale = deferred(); let firstSignal;
    const worker = makeWorker(book, (url, options) => { if (url === 'old') { firstSignal = options.signal; return stale.promise; } return Promise.resolve({ ok: true, arrayBuffer: async () => new ArrayBuffer(0) }); });
    const first = worker.send(open('old')); await worker.send(open('new')); stale.resolve({ ok: true, arrayBuffer: async () => new ArrayBuffer(0) }); await first;
    assert.equal(firstSignal.aborted, true); assert.equal(worker.reads(), 1); assert.equal(worker.messages.length, 1);
  });
  await check('switching files terminates and detaches the previous table worker', async () => {
    const reader = makeReader(); try {
      reader.send(open('old')); const old = reader.workers[0], staleHandler = old.onmessage;
      reader.send(open('new')); assert.equal(old.terminated, true); assert.equal(old.onmessage, null); assert.equal(old.onerror, null);
      await staleHandler({ data: { error: 'old error' } }); assert.equal(reader.messages.some(message => message.id === 'new' && message.type === 'error'), false);
    } finally { reader.dispose(); }
  });
  await check('old document search requests do not reach the active worker', async () => {
    const reader = makeReader(); try {
      reader.send(open('new')); reader.send({ type: 'find', id: 'old', query: 'target' }); assert.equal(reader.workers[0].messages.length, 1);
      reader.send({ type: 'find', id: 'new', query: 'target' }); assert.equal(reader.workers[0].messages.length, 2);
    } finally { reader.dispose(); }
  });
  await check('late DOCX renderer cannot overwrite a newer text view', async () => {
    const stale = deferred(); let rendering;
    const reader = makeReader({ render: async (_, target) => { const p = new Element('p'); p.textContent = 'stale word'; target.append(p); rendering = target; await stale.promise; } });
    try {
      reader.send(open('old', 'word')); await delay(); reader.send({ ...open('new', 'xml'), text: 'new text' }); await delay(); stale.resolve(); await delay();
      assert.equal(reader.container.childNodes[0].textContent, 'new text'); assert.equal(rendering.childNodes.length, 0);
      assert.equal(reader.messages.some(message => message.id === 'old' && message.type === 'rendered'), false);
    } finally { reader.dispose(); }
  });
  await check('late DOCX download is cancelled before its data is rendered', async () => {
    const stale = deferred(); let signal, rendered = 0;
    const reader = makeReader({ fetch: (_, options) => { signal = options.signal; return stale.promise; }, render: async () => { rendered++; } });
    try {
      reader.send(open('old', 'word')); reader.send({ ...open('new', 'xml'), text: 'new text' });
      stale.resolve({ ok: true, blob: async () => ({}) }); await delay(); assert.equal(signal.aborted, true); assert.equal(rendered, 0);
    } finally { reader.dispose(); }
  });
  await check('component script load is shared and obsolete opens do not start downloading', async () => {
    let downloads = 0; const reader = makeReader({ components: false, fetch: async () => { downloads++; return { ok: true, blob: async () => ({}) }; } });
    try {
      reader.send(open('old', 'word')); reader.send(open('new', 'word')); assert.equal(reader.head.childNodes.length, 1);
      reader.window.JSZip = {}; reader.head.childNodes[0].onload(); await delay(); assert.equal(reader.head.childNodes.length, 2);
      reader.window.docx = { renderAsync: async () => {} }; reader.head.childNodes[1].onload(); await delay(); assert.equal(downloads, 1);
    } finally { reader.dispose(); }
  });
  await check('blank table cells retain every grid cell and its row/column coordinates', async () => {
    const reader = makeReader(); try {
      reader.send(open('new')); const cells = []; for (let r = 0; r < 200; r++) for (let c = 0; c < 50; c++) cells.push({ row: r, col: c, value: '', numeric: false, formulaOnly: false });
      await reader.workers[0].onmessage({ data: { type: 'page', names: ['Sheet'], sheet: 0, start: 0, firstColumn: 0, rows: 200, columns: 50, totalRows: 200, totalColumns: 50, cells, formulaMissing: 0 } });
      const table = reader.container.querySelector('table'); assert.equal(table.childNodes.length, 201); assert.equal(table.childNodes[0].childNodes.length, 51);
      assert.equal(table.querySelectorAll('td').length, 10200);
      for (let r = 0; r < 200; r++) {
        const row = table.childNodes[r + 1]; assert.equal(row.childNodes.length, 51);
        for (let c = 0; c < 50; c++) { const cell = row.childNodes[c + 1]; assert.equal(cell.colSpan, 1); assert.equal(cell.dataset.row, String(r)); assert.equal(cell.dataset.col, String(c)); }
      }
    } finally { reader.dispose(); }
  });
  await check('merged, selected and formula cells preserve their original grid semantics', async () => {
    const reader = makeReader(); try {
      reader.send(open('new')); await reader.workers[0].onmessage({ data: { type: 'page', names: ['Sheet'], sheet: 0, start: 0, firstColumn: 0, rows: 1, columns: 8, totalRows: 1, totalColumns: 8, formulaMissing: 1, selected: { row: 0, col: 4 }, cells: [
        { row: 0, col: 0, value: 'merged', numeric: false, formulaOnly: false, spanCols: 3 },
        ...[3, 4, 5].map(col => ({ row: 0, col, value: '', numeric: false, formulaOnly: false })),
        { row: 0, col: 6, value: '=SUM(A1)', numeric: false, formulaOnly: true }, { row: 0, col: 7, value: '', numeric: false, formulaOnly: false }
      ] } });
      const cells = reader.container.querySelector('table').childNodes[1].childNodes;
      assert.equal(cells[1].colSpan, 3); assert.equal(reader.container.querySelector('.selected').dataset.col, '4');
      assert.equal(reader.container.querySelector('.formula').dataset.col, '6'); assert.equal(cells.reduce((sum, cell) => sum + cell.colSpan, 0), 9);
    } finally { reader.dispose(); }
  });
  await check('saved table/window scroll is restored and position messages carry the current id', async () => {
    const reader = makeReader(); try {
      const position = { sheet: 1, start: 200, firstColumn: 50, scrollX: 12, scrollY: 600, tableScrollLeft: 220, tableScrollTop: 40 };
      reader.send(open('new', 'spreadsheet', position)); assert.equal(reader.workers[0].messages[0].position, position);
      await reader.workers[0].onmessage({ data: { type: 'page', names: ['One', 'Two'], sheet: 1, start: 200, firstColumn: 50, rows: 0, columns: 0, totalRows: 400, totalColumns: 59, cells: [], formulaMissing: 0 } });
      const sent = reader.messages.filter(message => message.type === 'position').at(-1); assert.equal(sent.id, 'new');
      assert.deepEqual(JSON.parse(sent.message), position);
      for (let i = 0; i < 100; i++) reader.listeners.scroll(); assert.ok(reader.messages.filter(message => message.type === 'position').length <= 2);
    } finally { reader.dispose(); }
  });
  const report = { ranAt: new Date().toISOString(), kind: 'Node VM behavior checks; no browser layout or mouse verification', results };
  const outputIndex = process.argv.indexOf('--output');
  if (outputIndex >= 0) {
    const output = path.resolve(root, process.argv[outputIndex + 1]); const relative = path.relative(root, output);
    if (!relative.startsWith('tmp' + path.sep)) throw new Error('Temporary output must be inside project tmp');
    fs.mkdirSync(path.dirname(output), { recursive: true }); fs.writeFileSync(output, JSON.stringify(report, null, 2) + '\n');
  }
  console.log(JSON.stringify(report, null, 2)); if (results.some(result => result.status !== 'PASS')) process.exitCode = 1;
}
main().catch(error => { console.error(error); process.exitCode = 1; });
