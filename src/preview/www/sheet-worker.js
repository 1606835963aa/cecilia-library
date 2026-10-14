"use strict";
(() => {
    const context = globalThis;
    context.importScripts("/component/sheet/xlsx.js");
    const api = context.XLSX;
    let book, searchCursor = -1, lastQuery = "", matches = [], matchCount = 0, openSerial = 0, searchSerial = 0, load;
    let currentSheet = 0, currentStart = 0, currentFirstColumn = 0;
    function integer(value) { return typeof value === "number" && Number.isFinite(value) ? Math.max(0, Math.floor(value)) : 0; }
    function matchAt(index) {
        for (const set of matches) {
            if (index < set.positions.length) {
                const code = set.positions[index], r = Math.floor(code / set.stride);
                return { sheet: set.sheet, address: { r, c: code - r * set.stride } };
            }
            index -= set.positions.length;
        }
        throw new Error("查找位置不存在");
    }
    // SheetJS caches formatted text on the supplied cell. Keep that cache temporary,
    // so searching a whole workbook does not retain a second set of cell strings.
    function value(cell) { if (!cell)
        return ""; if (cell.v === undefined || cell.v === null)
        return cell.f ? "=" + cell.f : ""; return cell.w ?? api.utils.format_cell({ ...cell }); }
    function cellAt(sheet, address) { return sheet["!data"] ? sheet["!data"]?.[address.r]?.[address.c] : sheet[api.utils.encode_cell(address)]; }
    function* populatedCells(sheet) {
        const data = sheet["!data"];
        if (data) {
            // Array keys enumerate populated rows/columns without expanding a sparse
            // sheet's entire declared rectangle or allocating all cell addresses.
            for (const rowKey in data) {
                const r = Number(rowKey), row = data[r];
                if (!row || !Number.isInteger(r) || r < 0)
                    continue;
                for (const columnKey in row) {
                    const c = Number(columnKey), cell = row[c];
                    if (cell && Number.isInteger(c) && c >= 0)
                        yield { address: { r, c }, cell };
                }
            }
        }
        else
            for (const key in sheet)
                if (/^[A-Z]+[1-9][0-9]*$/.test(key)) {
                    const cell = sheet[key];
                    if (cell)
                        yield { address: api.utils.decode_cell(key), cell };
                }
    }
    function page(index, start, firstColumn, selected, found, foundIndex = 0, foundCount = 0) {
        if (!book)
            return;
        index = Math.min(integer(index), book.SheetNames.length - 1);
        start = integer(start);
        firstColumn = integer(firstColumn);
        const sheet = book.Sheets[book.SheetNames[index]];
        if (!sheet)
            throw new Error("工作表不存在");
        const range = sheet["!ref"] ? api.utils.decode_range(sheet["!ref"]) : { s: { r: 0, c: 0 }, e: { r: -1, c: -1 } };
        const totalRows = Math.max(0, range.e.r + 1), totalColumns = Math.max(0, range.e.c + 1);
        start = Math.max(0, Math.min(start, Math.max(0, totalRows - 1)));
        firstColumn = Math.max(0, Math.min(firstColumn, Math.max(0, totalColumns - 1)));
        const rows = Math.min(200, totalRows - start), columns = Math.min(50, totalColumns - firstColumn);
        const cells = [];
        let formulaMissing = 0;
        const merged = new Map();
        for (const merge of sheet["!merges"] ?? []) {
            const sr = Math.max(start, merge.s.r), sc = Math.max(firstColumn, merge.s.c), er = Math.min(start + rows - 1, merge.e.r), ec = Math.min(firstColumn + columns - 1, merge.e.c);
            if (sr > er || sc > ec)
                continue;
            for (let r = sr; r <= er; r++)
                for (let c = sc; c <= ec; c++)
                    merged.set(`${r},${c}`, r === sr && c === sc ? { source: merge.s, spanRows: er - sr + 1, spanCols: ec - sc + 1 } : { skip: true });
        }
        for (let r = start; r < start + rows; r++)
            for (let c = firstColumn; c < firstColumn + columns; c++) {
                const merge = merged.get(`${r},${c}`);
                if (merge?.skip)
                    continue;
                const cell = cellAt(sheet, merge?.source ?? { r, c });
                const formulaOnly = !!cell?.f && cell.v === undefined;
                if (formulaOnly)
                    formulaMissing++;
                cells.push({ row: r, col: c, value: value(cell), numeric: cell?.t === "n", formulaOnly, spanRows: merge?.spanRows, spanCols: merge?.spanCols });
            }
        currentSheet = index;
        currentStart = start;
        currentFirstColumn = firstColumn;
        context.postMessage({ type: "page", names: book.SheetNames, sheet: index, start, rows, columns, totalRows, totalColumns, firstColumn, cells, formulaMissing, found, foundIndex, foundCount, selected: selected ? { row: selected.r, col: selected.c } : undefined });
    }
    context.onmessage = async (event) => {
        const message = event.data;
        let generation = openSerial;
        try {
            if (message.type === "open") {
                generation = ++openSerial;
                ++searchSerial;
                load?.abort();
                const controller = new AbortController();
                load = controller;
                book = undefined;
                matches = [];
                matchCount = 0;
                searchCursor = -1;
                lastQuery = "";
                const response = await fetch(message.url, { signal: controller.signal });
                if (!response.ok)
                    throw new Error("读取表格失败");
                if (generation !== openSerial)
                    return;
                const data = await response.arrayBuffer();
                if (generation !== openSerial)
                    return;
                book = api.read(data, { type: "array", dense: true, cellDates: true, cellNF: true, cellText: false, raw: message.extension === "csv", codepage: 936 });
                if (book.SheetNames.length === 0)
                    throw new Error("文件没有可显示的工作表");
                page(integer(message.position?.sheet), integer(message.position?.start), integer(message.position?.firstColumn));
                if (load === controller)
                    load = undefined;
            }
            else if (message.type === "page") {
                ++searchSerial;
                searchCursor = -1;
                page(message.sheet, message.start, message.firstColumn);
            }
            else if (message.type === "find") {
                if (!book)
                    return;
                const search = ++searchSerial;
                const query = String(message.query ?? "").toLocaleLowerCase(), index = Math.min(integer(message.sheet), book.SheetNames.length - 1);
                if (!query) {
                    lastQuery = "";
                    matches = [];
                    matchCount = 0;
                    searchCursor = -1;
                    page(currentSheet, currentStart, currentFirstColumn, undefined, false);
                    return;
                }
                if (query !== lastQuery) {
                    searchCursor = -1;
                    lastQuery = "";
                    matches = [];
                    matchCount = 0;
                    const found = [];
                    let total = 0, visited = 0;
                    for (let sheetIndex = 0; sheetIndex < book.SheetNames.length; sheetIndex++) {
                        const target = book.Sheets[book.SheetNames[sheetIndex]];
                        const stride = target["!ref"] ? Math.max(1, api.utils.decode_range(target["!ref"]).e.c + 1) : 1;
                        let positions = new Float64Array(0), length = 0;
                        for (const entry of populatedCells(target)) {
                            if (value(entry.cell).toLocaleLowerCase().includes(query)) {
                                const code = entry.address.r * stride + entry.address.c;
                                if (!Number.isSafeInteger(code) || entry.address.c >= stride)
                                    throw new Error("工作表范围与单元格位置不一致");
                                if (length === positions.length) {
                                    const grown = new Float64Array(Math.max(1024, length * 2));
                                    grown.set(positions);
                                    positions = grown;
                                }
                                positions[length++] = code;
                            }
                            // Allow newer searches, clearing and page switches to cancel
                            // an unfinished scan without publishing its partial results.
                            if (++visited % 4096 === 0) {
                                await new Promise(resolve => setTimeout(resolve, 0));
                                if (search !== searchSerial || generation !== openSerial)
                                    return;
                            }
                        }
                        if (length) {
                            const used = positions.subarray(0, length);
                            used.sort();
                            found.push({ sheet: sheetIndex, stride, positions: used });
                            total += length;
                        }
                    }
                    matches = found;
                    matchCount = total;
                    lastQuery = query;
                }
                if (matchCount) {
                    if (searchCursor < 0) {
                        let offset = 0;
                        searchCursor = message.backward ? matchCount - 1 : 0;
                        for (const set of matches) {
                            if (message.backward) {
                                if (set.sheet <= index)
                                    searchCursor = offset + set.positions.length - 1;
                            }
                            else if (set.sheet >= index) {
                                searchCursor = offset;
                                break;
                            }
                            offset += set.positions.length;
                        }
                    }
                    else
                        searchCursor = (searchCursor + (message.backward ? -1 : 1) + matchCount) % matchCount;
                    const match = matchAt(searchCursor);
                    page(match.sheet, Math.floor(match.address.r / 200) * 200, Math.floor(match.address.c / 50) * 50, match.address, true, searchCursor + 1, matchCount);
                }
                else
                    page(currentSheet, currentStart, currentFirstColumn, undefined, false);
            }
        }
        catch (error) {
            if (generation === openSerial)
                context.postMessage({ error: error instanceof Error ? error.message : "无法读取表格" });
        }
    };
})();
