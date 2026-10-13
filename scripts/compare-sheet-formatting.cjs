const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),{spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'..'),xlsx=require(path.join(root,'components/sheet/0.20.3/xlsx.js'));
const value=cell=>!cell?'':cell.v===undefined||cell.v===null?(cell.f?'='+cell.f:''):(cell.w??xlsx.utils.format_cell(cell));
const asArray=buffer=>buffer.buffer.slice(buffer.byteOffset,buffer.byteOffset+buffer.byteLength);
const options=(cellText,extension)=>({type:'array',cellDates:true,cellNF:true,cellText,raw:extension==='csv',codepage:936});
const read=(filename,cellText)=>xlsx.read(asArray(fs.readFileSync(filename)),options(cellText,path.extname(filename).slice(1)));
const countStrings=book=>{let cells=0,formatted=0;for(const name of book.SheetNames)for(const [key,cell]of Object.entries(book.Sheets[name]))if(!key.startsWith('!')){cells++;if(cell.w!==undefined)formatted++;}return{cells,formatted};};
if(process.argv[2]==='child'){
 const mode=process.argv[3]==='true',input=fs.readFileSync(path.join(root,'tmp/memory-fixtures/内存表格样本.xlsx'));
 const before=process.memoryUsage(),started=performance.now(),book=xlsx.read(asArray(input),options(mode,'xlsx')),milliseconds=performance.now()-started;
 const after=process.memoryUsage(),sheet=book.Sheets[book.SheetNames[0]];let pageValues=0;for(let r=0;r<200;r++)for(let c=0;c<12;c++)pageValues+=value(sheet[xlsx.utils.encode_cell({r,c})]).length;
 console.log(JSON.stringify({cellText:mode,milliseconds,before,after,afterPage:process.memoryUsage(),count:countStrings(book),pageValues}));process.exit();
}
const comparisons=[];
for(const filename of fs.readdirSync(path.join(root,'tests/fixtures')).filter(filename=>/\.(csv|xls|xlsx|xlt|xltx|xlsm|xltm|xla|xlam|dbf|prn|dif)$/.test(filename))){
 const before=read(path.join(root,'tests/fixtures',filename),true),after=read(path.join(root,'tests/fixtures',filename),false);let cells=0;const mismatches=[];
 for(const name of before.SheetNames){const a=before.Sheets[name],b=after.Sheets[name];for(const key of Object.keys(a).filter(key=>!key.startsWith('!'))){cells++;if(value(a[key])!==value(b[key]))mismatches.push({sheet:name,key,before:value(a[key]),after:value(b[key])});}}
 comparisons.push({filename,cells,status:mismatches.length?'FAIL':'PASS',mismatches});
}
const samples=[];
for(const date1904 of [false,true]){
 const book=xlsx.utils.book_new(),sheet=xlsx.utils.aoa_to_sheet([['0123',1234.5,new Date(Date.UTC(2026,9,12)),42,0,undefined]]);book.Workbook={WBProps:{date1904}};
 const origin=Date.UTC(date1904?1904:1899,date1904?0:11,date1904?1:30);
 sheet.B1.z='#,##0.00';sheet.C1={t:'n',v:(Date.UTC(2026,9,12)-origin)/86400000,z:'yyyy-mm-dd'};sheet.D1.f='SUM(20,22)';sheet.E1.f='1-1';sheet.F1={t:'n',f:'SUM(20,22)',z:'#,##0.00'};sheet['!ref']='A1:F1';xlsx.utils.book_append_sheet(book,sheet,'format');
 const bytes=xlsx.write(book,{type:'buffer',bookType:'xlsx'}),a=xlsx.read(asArray(bytes),options(true,'xlsx')),b=xlsx.read(asArray(bytes),options(false,'xlsx'));
 const before=Object.keys(a.Sheets.format).filter(key=>!key.startsWith('!')).map(key=>[key,value(a.Sheets.format[key])]);const after=Object.keys(b.Sheets.format).filter(key=>!key.startsWith('!')).map(key=>[key,value(b.Sheets.format[key])]);
 samples.push({date1904,status:JSON.stringify(before)===JSON.stringify(after)&&after.some(([key,text])=>key==='C1'&&text==='2026-10-12')?'PASS':'FAIL',before,after});
}
const measures=[];
for(let iteration=0;iteration<3;iteration++)for(const mode of [true,false]){
 const child=spawnSync(process.execPath,[__filename,'child',String(mode)],{encoding:'utf8',windowsHide:true,timeout:60000});if(child.status!==0)throw new Error(child.stderr||'child failed');measures.push({...JSON.parse(child.stdout),iteration});
}
const report={ranAt:new Date().toISOString(),kind:'Node parser diagnostic; separate process per parse; not a WebView memory measurement',script:'scripts/compare-sheet-formatting.cjs',nodeVersion:process.version,inputSha256:crypto.createHash('sha256').update(fs.readFileSync(path.join(root,'tmp/memory-fixtures/内存表格样本.xlsx'))).digest('hex'),comparisons,samples,measures};fs.writeFileSync(path.join(root,'tmp/sheet-formatting-experiment.json'),JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({comparisons,samples,measures:measures.map(result=>({cellText:result.cellText,iteration:result.iteration,milliseconds:Math.round(result.milliseconds),heapUsedMiB:result.after.heapUsed/1048576,rssMiB:result.after.rss/1048576,heapAfterPageMiB:result.afterPage.heapUsed/1048576,formattedStrings:result.count.formatted,pageValues:result.pageValues}))},null,2));
if(comparisons.some(result=>result.status!=='PASS')||samples.some(result=>result.status!=='PASS'))process.exitCode=1;
