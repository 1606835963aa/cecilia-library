"""Generate bounded larger documents under tmp for lifecycle comparisons."""
from pathlib import Path
from docx import Document
from openpyxl import Workbook

root=Path(__file__).resolve().parent.parent
output=root/'tmp'/'memory-fixtures'
output.mkdir(parents=True,exist_ok=True)
doc=Document()
for page in range(30):
    if page:doc.add_page_break()
    doc.add_heading(f'内存阅读样本 第 {page+1} 页',1)
    for paragraph in range(12):doc.add_paragraph(f'中文阅读检查 {page+1}-{paragraph+1}。'+'文字与标点完整，Memory sample 001234567890。'*12)
    if page%5==0:doc.add_picture(str(root/'tests'/'fixtures'/'图片样本.png'),width=3657600)
doc.save(output/'内存文字样本.docx')
book=Workbook(write_only=True)
sheet=book.create_sheet('五万行数据')
sheet.append(['编号','名称']+[f'数值{i}' for i in range(10)])
for row in range(50000):sheet.append([f'{row:018d}',f'中文数据 {row}']+[row+i/10 for i in range(10)])
book.save(output/'内存表格样本.xlsx')
print('Generated DOCX with 30 explicit page-break sections and XLSX with 50,000 data rows under tmp/memory-fixtures')
