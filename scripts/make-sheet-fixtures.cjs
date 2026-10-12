const path=require('path'),fs=require('fs');
const XLSX=require('../components/sheet/0.20.3/xlsx.js');
const root=path.join(__dirname,'..','tests','fixtures');
const book=XLSX.read(fs.readFileSync(path.join(root,'数据样本.xlsx')),{cellDates:true,type:'buffer'});
for(const [ext,type] of [['xls','biff8'],['dbf','dbf'],['prn','prn'],['dif','dif']]) fs.writeFileSync(path.join(root,'旧表格样本.'+ext),XLSX.write(book,{bookType:type,type:'buffer'}));
console.log('Generated real BIFF8, DBF, PRN and DIF fixtures');
