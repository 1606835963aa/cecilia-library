const path=require('path'),fs=require('fs');
const XLSX=require('../components/sheet/0.20.3/xlsx.js');
const root=path.join(__dirname,'..','tests','fixtures');
const book=XLSX.read(fs.readFileSync(path.join(root,'数据样本.xlsx')),{cellDates:true,cellNF:true,type:'buffer'});
if(!process.argv.includes('--addins-only')) {
    for(const [ext,type] of [['xls','biff8'],['dbf','dbf'],['prn','prn'],['dif','dif']]) fs.writeFileSync(path.join(root,'旧表格样本.'+ext),XLSX.write(book,{bookType:type,type:'buffer'}));
    console.log('Generated real BIFF8, DBF, PRN and DIF fixtures');
}
book.Workbook ??= {};
book.Workbook.Sheets=book.SheetNames.map(name=>({name,Hidden:2}));
book.Sheets[book.SheetNames[0]].A7={t:'s',v:'加载项数据查看'};
const dataRange=XLSX.utils.decode_range(book.Sheets[book.SheetNames[0]]['!ref']);
dataRange.e.r=Math.max(dataRange.e.r,6);
book.Sheets[book.SheetNames[0]]['!ref']=XLSX.utils.encode_range(dataRange);
for(const type of ['xla','xlam']) {
    const content=XLSX.write(book,{bookType:type,type:'buffer',compression:true});
    fs.writeFileSync(path.join(root,'加载项数据.'+type),content);
    const reopened=XLSX.read(content,{type:'buffer'});
    if(reopened.SheetNames.length!==3||reopened.Sheets[reopened.SheetNames[0]].A2.v!=='001234567890123456'||reopened.Sheets[reopened.SheetNames[0]].A7?.v!=='加载项数据查看')throw new Error('Add-in fixture data lost');
    console.log('Generated '+type+' with its declared add-in type and three hidden data sheets');
}
