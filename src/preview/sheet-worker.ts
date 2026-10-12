(() => {
    type Address={r:number;c:number};type Range={s:Address;e:Address};type SheetCell={t?:string;v?:unknown;w?:string;f?:string};type Sheet={ [key:string]:unknown; "!ref"?:string; "!merges"?:Range[] };
    type Book={SheetNames:string[];Sheets:Record<string,Sheet>};type API={read(data:ArrayBuffer,options:unknown):Book;utils:{decode_range(value:string):Range;encode_cell(a:Address):string;decode_cell(value:string):Address;format_cell(c:SheetCell):string}};
    const context=globalThis as unknown as {importScripts(...urls:string[]):void;XLSX:API;onmessage:(event:MessageEvent)=>void;postMessage(message:unknown):void};
    context.importScripts("/component/sheet/xlsx.js");const api=context.XLSX;let book:Book,searchCursor=-1,lastQuery="";
    function value(cell:SheetCell|undefined):string {if(!cell)return "";if(cell.v===undefined||cell.v===null)return cell.f?"="+cell.f:"";return cell.w??api.utils.format_cell(cell);}
    function page(index:number,start:number,firstColumn:number,selected?:Address,found?:boolean,foundIndex=0,foundCount=0) {
        const sheet=book.Sheets[book.SheetNames[index]];if(!sheet)throw new Error("工作表不存在");
        const range=sheet["!ref"]?api.utils.decode_range(sheet["!ref"]):{s:{r:0,c:0},e:{r:-1,c:-1}};const totalRows=range.e.r+1,totalColumns=range.e.c+1;
        start=Math.max(0,Math.min(start,Math.max(0,totalRows-1)));firstColumn=Math.max(0,Math.min(firstColumn,Math.max(0,totalColumns-1)));
        const rows=Math.min(200,totalRows-start),columns=Math.min(50,totalColumns-firstColumn);const cells:unknown[]=[];let formulaMissing=0;
        const merged=new Map<string,{skip?:boolean;source?:Address;spanRows?:number;spanCols?:number}>();
        for(const merge of sheet["!merges"]??[]){
            const sr=Math.max(start,merge.s.r),sc=Math.max(firstColumn,merge.s.c),er=Math.min(start+rows-1,merge.e.r),ec=Math.min(firstColumn+columns-1,merge.e.c);
            if(sr>er||sc>ec)continue;
            for(let r=sr;r<=er;r++)for(let c=sc;c<=ec;c++)merged.set(`${r},${c}`,r===sr&&c===sc?{source:merge.s,spanRows:er-sr+1,spanCols:ec-sc+1}:{skip:true});
        }
        for(let r=start;r<start+rows;r++)for(let c=firstColumn;c<firstColumn+columns;c++){
            const merge=merged.get(`${r},${c}`);if(merge?.skip)continue;const cell=sheet[api.utils.encode_cell(merge?.source??{r,c})] as SheetCell|undefined;
            const formulaOnly=!!cell?.f&&cell.v===undefined;if(formulaOnly)formulaMissing++;
            cells.push({row:r,col:c,value:value(cell),numeric:cell?.t==="n",formulaOnly,spanRows:merge?.spanRows,spanCols:merge?.spanCols});
        }
        context.postMessage({type:"page",names:book.SheetNames,sheet:index,start,rows,columns,totalRows,totalColumns,firstColumn,cells,formulaMissing,found,foundIndex,foundCount,selected:selected?{row:selected.r,col:selected.c}:undefined});
    }
    context.onmessage=async(event:MessageEvent)=>{
        try{
            const message=event.data;
            if(message.type==="open"){
                const response=await fetch(message.url);if(!response.ok)throw new Error("读取表格失败");const data=await response.arrayBuffer();
                book=api.read(data,{type:"array",cellDates:true,cellNF:true,cellText:true,raw:message.extension==="csv",codepage:936});
                if(book.SheetNames.length===0)throw new Error("文件没有可显示的工作表");page(0,0,0);
            }else if(message.type==="page")page(message.sheet,message.start,message.firstColumn);
            else if(message.type==="find"){
                const query=String(message.query).toLocaleLowerCase(),index=Number(message.sheet);const sheet=book.Sheets[book.SheetNames[index]];
                if(query!==lastQuery){searchCursor=-1;lastQuery=query;}
                const matches:{address:Address;sheet:number}[]=[];
                for(let sheetIndex=0;sheetIndex<book.SheetNames.length;sheetIndex++){
                    const target=book.Sheets[book.SheetNames[sheetIndex]];
                    const addresses=Object.keys(target).filter(key=>!key.startsWith("!")&&value(target[key] as SheetCell).toLocaleLowerCase().includes(query)).map(key=>api.utils.decode_cell(key)).sort((a,b)=>a.r-b.r||a.c-b.c);
                    matches.push(...addresses.map(address=>({address,sheet:sheetIndex})));
                }
                if(matches.length){
                    searchCursor=searchCursor<0?Math.max(0,matches.findIndex(match=>match.sheet>=index)):(searchCursor+(message.backward?-1:1)+matches.length)%matches.length;
                    const match=matches[searchCursor];page(match.sheet,Math.floor(match.address.r/200)*200,Math.floor(match.address.c/50)*50,match.address,true,searchCursor+1,matches.length);
                }else page(index,0,0,undefined,false);
            }
        }catch(error){context.postMessage({error:error instanceof Error?error.message:"无法读取表格"});}
    };
})();
