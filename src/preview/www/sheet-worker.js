"use strict";
(() => {
    const context = globalThis;
    context.importScripts("/component/sheet/xlsx.js");
    const api = context.XLSX;
    let book, searchCursor = -1, lastQuery = "", matches = [], openSerial = 0, load;
    let currentSheet = 0, currentStart = 0, currentFirstColumn = 0;
    function integer(value) { return typeof value === "number" && Number.isFinite(value) ? Math.max(0, Math.floor(value)) : 0; }
    function value(cell) { if (!cell)
        return ""; if (cell.v === undefined || cell.v === null)
        return cell.f ? "=" + cell.f : ""; return cell.w ?? api.utils.format_cell(cell); }
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
                const cell = sheet[api.utils.encode_cell(merge?.source ?? { r, c })];
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
                load?.abort();
                const controller = new AbortController();
                load = controller;
                book = undefined;
                matches = [];
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
                book = api.read(data, { type: "array", cellDates: true, cellNF: true, cellText: false, raw: message.extension === "csv", codepage: 936 });
                if (book.SheetNames.length === 0)
                    throw new Error("文件没有可显示的工作表");
                page(integer(message.position?.sheet), integer(message.position?.start), integer(message.position?.firstColumn));
                if (load === controller)
                    load = undefined;
            }
            else if (message.type === "page") {
                searchCursor = -1;
                page(message.sheet, message.start, message.firstColumn);
            }
            else if (message.type === "find") {
                if (!book)
                    return;
                const query = String(message.query ?? "").toLocaleLowerCase(), index = Math.min(integer(message.sheet), book.SheetNames.length - 1);
                if (!query) {
                    lastQuery = "";
                    matches = [];
                    searchCursor = -1;
                    page(currentSheet, currentStart, currentFirstColumn, undefined, false);
                    return;
                }
                if (query !== lastQuery) {
                    searchCursor = -1;
                    lastQuery = query;
                    matches = [];
                    for (let sheetIndex = 0; sheetIndex < book.SheetNames.length; sheetIndex++) {
                        const target = book.Sheets[book.SheetNames[sheetIndex]];
                        const addresses = Object.keys(target).filter(key => !key.startsWith("!") && value(target[key]).toLocaleLowerCase().includes(query)).map(key => api.utils.decode_cell(key)).sort((a, b) => a.r - b.r || a.c - b.c);
                        for (const address of addresses)
                            matches.push({ address, sheet: sheetIndex });
                    }
                }
                if (matches.length) {
                    if (searchCursor < 0) {
                        if (message.backward) {
                            searchCursor = matches.length - 1;
                            for (let i = matches.length - 1; i >= 0; i--)
                                if (matches[i].sheet <= index) {
                                    searchCursor = i;
                                    break;
                                }
                        }
                        else {
                            const first = matches.findIndex(match => match.sheet >= index);
                            searchCursor = first < 0 ? 0 : first;
                        }
                    }
                    else
                        searchCursor = (searchCursor + (message.backward ? -1 : 1) + matches.length) % matches.length;
                    const match = matches[searchCursor];
                    page(match.sheet, Math.floor(match.address.r / 200) * 200, Math.floor(match.address.c / 50) * 50, match.address, true, searchCursor + 1, matches.length);
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
