(() => {
    type HostMessage = { type: string; id: string; kind: string; url: string; text?: string; extension: string; query?: string; backward?: boolean; assets?:Record<string,string>;baseUrl?:string };
    type Cell = { row: number; col: number; value: string; numeric: boolean; formulaOnly: boolean; spanRows?: number; spanCols?: number };
    type SheetPage = { type: string; names: string[]; sheet: number; start: number; rows: number; columns: number; totalRows: number; totalColumns: number; firstColumn: number; cells: Cell[]; formulaMissing: number; found?: boolean; foundIndex?: number; foundCount?: number; selected?: {row:number;col:number}; error?:string };
    const bridge = (window as unknown as { chrome: { webview: { postMessage(data: unknown): void; addEventListener(type:string, fn:(event:{data:HostMessage})=>void):void } } }).chrome.webview;
    const container = document.getElementById("document")!;
    let requestId = "", serial = 0, worker: Worker | undefined, sheet = 0, start = 0, firstColumn = 0;
    function send(type: string, message = "") { bridge.postMessage({ type, id: requestId, message }); }
    function script(url: string): Promise<void> {
        return new Promise((resolve,reject)=> { const el=document.createElement("script"); el.src=url; el.onload=()=>resolve(); el.onerror=()=>reject(new Error("预览组件加载失败")); document.head.append(el); });
    }
    function sanitize(root: HTMLElement) {
        root.querySelectorAll("script,iframe,frame,object,embed,form,input,button,textarea,link,meta,base,svg,math,video,audio").forEach(e=>e.remove());
        root.querySelectorAll("*").forEach(el=>{
            for (const attr of [...el.attributes]) {
                const name=attr.name.toLowerCase(), value=attr.value.trim().toLowerCase();
                if(name.startsWith("on") || name==="srcdoc" || name==="action" || name==="formaction") el.removeAttribute(attr.name);
                if(["href","src","xlink:href"].includes(name) && !(value.startsWith("data:image/") || value.startsWith("blob:"))) el.removeAttribute(attr.name);
            }
        });
    }
    function columnName(index: number): string { let name=""; for(let n=index+1;n>0;n=Math.floor((n-1)/26))name=String.fromCharCode(65+(n-1)%26)+name; return name; }
    function displaySheet(page: SheetPage) {
        container.replaceChildren(); sheet=page.sheet; start=page.start; firstColumn=page.firstColumn;
        const toolbar=document.createElement("div"); toolbar.className="sheet-header";
        const selector=document.createElement("select"); selector.setAttribute("aria-label","工作表");
        page.names.forEach((name,i)=>{ const option=document.createElement("option"); option.textContent=name; option.value=String(i); option.selected=i===sheet; selector.append(option); });
        selector.onchange=()=>worker?.postMessage({type:"page",sheet:Number(selector.value),start:0,firstColumn:0}); toolbar.append(selector);
        function button(label:string,enabled:boolean,fn:()=>void) { const b=document.createElement("button");b.textContent=label;b.disabled=!enabled;b.onclick=fn;toolbar.append(b); }
        button("上一组行",start>0,()=>worker?.postMessage({type:"page",sheet,start:Math.max(0,start-200),firstColumn}));
        button("下一组行",start+page.rows<page.totalRows,()=>worker?.postMessage({type:"page",sheet,start:start+200,firstColumn}));
        button("左侧列",firstColumn>0,()=>worker?.postMessage({type:"page",sheet,start,firstColumn:Math.max(0,firstColumn-50)}));
        button("右侧列",firstColumn+page.columns<page.totalColumns,()=>worker?.postMessage({type:"page",sheet,start,firstColumn:firstColumn+50}));
        const summary=document.createElement("span");summary.textContent=`第 ${page.rows ? start+1:0}–${start+page.rows} 行 / ${page.totalRows} 行`;toolbar.append(summary);container.append(toolbar);
        if(page.formulaMissing>0){const note=document.createElement("div");note.className="sheet-note";note.textContent="部分公式没有已保存的结果，显示公式内容";container.append(note);}
        const wrapper=document.createElement("div");wrapper.className="sheet-table-wrap";
        const table=document.createElement("table");table.className="sheet-table";const head=document.createElement("tr");const corner=document.createElement("th");corner.className="row-number";head.append(corner);
        for(let c=firstColumn;c<firstColumn+page.columns;c++){const th=document.createElement("th");th.textContent=columnName(c);head.append(th);}table.append(head);
        const byRow=new Map<number,Cell[]>();page.cells.forEach(c=>{if(!byRow.has(c.row))byRow.set(c.row,[]);byRow.get(c.row)!.push(c);});
        for(let r=start;r<start+page.rows;r++){
            const row=document.createElement("tr");const number=document.createElement("td");number.className="row-number";number.textContent=String(r+1);row.append(number);
            for(const cell of byRow.get(r)??[]){const td=document.createElement("td");td.textContent=cell.value;td.dataset.row=String(cell.row);td.dataset.col=String(cell.col);if(cell.numeric)td.classList.add("number");if(cell.formulaOnly)td.classList.add("formula");if(cell.spanRows)td.rowSpan=cell.spanRows;if(cell.spanCols)td.colSpan=cell.spanCols;if(page.selected?.row===cell.row&&page.selected?.col===cell.col)td.classList.add("selected");row.append(td);}table.append(row);
        }
        wrapper.append(table);container.append(wrapper); send("status",`${page.names[sheet]} · ${page.totalRows} 行 × ${page.totalColumns} 列`);
        if(page.found!==undefined)send("find",`${page.foundIndex??0}/${page.foundCount??0}`);if(page.selected)container.querySelector(".selected")?.scrollIntoView({block:"center"});
    }
    async function open(message: HostMessage) {
        const generation=++serial; requestId=message.id;worker?.terminate();worker=undefined;container.replaceChildren();
        try {
            if(message.kind==="word"){
                if(!(window as unknown as {JSZip?:unknown}).JSZip)await script("/component/word/jszip.js");
                if(!(window as unknown as {docx?:unknown}).docx)await script("/component/word/docx-preview.js");
                const response=await fetch(message.url);if(!response.ok)throw new Error("读取文档失败");const blob=await response.blob();if(generation!==serial)return;
                const renderer=(window as unknown as {docx:{renderAsync(data:Blob,body:HTMLElement,styles:HTMLElement|null,options:unknown):Promise<void>}}).docx;
                const temporary=document.createElement("div");await renderer.renderAsync(blob,temporary,null,{inWrapper:true,ignoreWidth:false,ignoreHeight:false,breakPages:true,renderHeaders:true,renderFooters:true,renderFootnotes:true,useBase64URL:true});
                if(generation!==serial)return;sanitize(temporary);container.append(...temporary.childNodes);
                const pages=[...container.querySelectorAll<HTMLElement>('section.docx')];
                if(pages.length>1){
                    const nav=document.createElement('div');nav.className='doc-page-nav';const select=document.createElement('select');select.setAttribute('aria-label','跳转页码');
                    pages.forEach((_,i)=>{const option=document.createElement('option');option.textContent=`第 ${i+1} 页`;option.value=String(i);select.append(option);});
                    select.onchange=()=>pages[Number(select.value)].scrollIntoView({block:'start'});nav.append(select);
                    const total=document.createElement('span');total.textContent=`共 ${pages.length} 页`;nav.append(total);container.prepend(nav);
                }
                send("rendered");
            }else if(message.kind==="spreadsheet"){
                worker=new Worker("/sheet-worker.js");const target=worker;
                let first=true;target.onmessage=(event:MessageEvent<SheetPage>)=>{if(generation!==serial)return;if(event.data.error){send("error",event.data.error);return;}displaySheet(event.data);if(first){first=false;send("rendered");}};
                target.onerror=()=>{if(generation===serial)send("error","表格组件处理失败");};target.postMessage({type:"open",url:message.url,extension:message.extension});
            }else if(message.kind==="html"){
                const parsed=new DOMParser().parseFromString(message.text??"","text/html");
                const resolve=(url:string)=>{let asset=message.assets?.[url];if(!asset&&message.baseUrl){try{asset=message.assets?.[new URL(url,message.baseUrl).href];}catch{}}return asset;};
                parsed.querySelectorAll("img[src]").forEach(img=>{const asset=resolve(img.getAttribute("src")!);if(asset?.startsWith("data:image/"))img.setAttribute("src",asset);});
                parsed.querySelectorAll("link[rel=stylesheet]").forEach(link=>{const asset=resolve(link.getAttribute("href")??"");if(asset?.startsWith("css:")){const style=document.createElement("style");style.textContent=asset.slice(4);parsed.body.prepend(style);}});
                parsed.head.querySelectorAll("style").forEach(style=>parsed.body.prepend(style));sanitize(parsed.body);container.className="html-document";container.append(...parsed.body.childNodes);send("rendered");
            }else{
                const pre=document.createElement("pre");pre.className="xml-document";pre.textContent=message.text??"";container.append(pre);send("rendered");
            }
        }catch(error){if(generation===serial)send("error",error instanceof Error?error.message:"预览失败");}
    }
    bridge.addEventListener("message",event=>{
        if(event.data.type==="open"){container.className="";void open(event.data);}
        if(event.data.type==="find"){
            if(worker)worker.postMessage({type:"find",query:event.data.query,sheet,backward:!!event.data.backward});
            else {const found=(window as unknown as {find(q:string,c:boolean,b:boolean,w:boolean):boolean}).find(event.data.query??"",false,!!event.data.backward,true);if(!found)send("find","0/0");}
        }
    });
    bridge.postMessage({type:"ready"});
})();
