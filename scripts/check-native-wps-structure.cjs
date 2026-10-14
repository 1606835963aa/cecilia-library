const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'..'),xlsx=require(path.join(root,'components/sheet/0.20.3/xlsx.js'));
const catalog=JSON.parse(fs.readFileSync(path.join(root,'data/native-wps-fixtures.json'),'utf8'));
const results=[];
for(const fixture of catalog.fixtures){
  try{
    const filename=path.join(root,catalog.storage,fixture.file),bytes=fs.readFileSync(filename);
    if(path.basename(fixture.file)!==fixture.file)throw new Error('Invalid fixture name');
    const sha256=crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase();
    if(sha256!==fixture.sha256)throw new Error('Source hash mismatch');
    if(bytes.subarray(0,8).toString('hex')!=='d0cf11e0a1b11ae1')throw new Error('Not an OLE document');
    const compound=xlsx.CFB.read(bytes,{type:'buffer'});
    const required=['.wps','.wpt'].includes(fixture.extension)?'WordDocument':['.et','.ett'].includes(fixture.extension)?'Workbook':'PowerPoint Document';
    if(!compound.FileIndex.some(entry=>entry.name===required))throw new Error('Required document stream missing');
    const summaries=compound.FileIndex.filter(entry=>/SummaryInformation/.test(entry.name)).map(entry=>Buffer.from(entry.content));
    const producerMarkers=['KSOProductBuildVer','Kingsoft Office','WPS Office'].filter(name=>summaries.some(bytes=>bytes.includes(Buffer.from(name))||bytes.includes(Buffer.from(name,'utf16le'))));
    if(!producerMarkers.length&&fixture.scope!=='blank-smoke')throw new Error('No WPS producer metadata');
    const properties={};
    if(['.et','.ett'].includes(fixture.extension)){
      const book=xlsx.read(bytes,{dense:true,cellNF:true,cellDates:true,cellText:false});
      properties.sheetCount=book.SheetNames.length;properties.ranges=book.SheetNames.map(name=>book.Sheets[name]['!ref']);
      properties.ksoBuild=book.Props?.KSOProductBuildVer??null;
      if(properties.sheetCount!==fixture.sheetCount)throw new Error('Worksheet count mismatch');
    }
    results.push({id:fixture.id,file:fixture.file,extension:fixture.extension,status:fixture.scope==='blank-smoke'?'SMOKE_ONLY':'PASS',sha256,bytes:bytes.length,stream:required,producerMarkers,...properties});
  }catch(error){results.push({id:fixture.id,file:fixture.file,status:'FAIL',error:error.message});}
}
fs.writeFileSync(path.join(root,'reports/native-wps-structure.json'),JSON.stringify({ranAt:new Date().toISOString(),kind:'Pinned public originals; OLE streams and WPS producer metadata; no extension substitution',results},null,2)+'\n');
console.log(JSON.stringify({checks:results.length,failures:results.filter(item=>item.status==='FAIL')},null,2));
if(results.some(item=>item.status==='FAIL'))process.exitCode=1;
