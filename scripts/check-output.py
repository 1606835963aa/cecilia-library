"""Independently check rendered Office PDFs and saved text using Python parsers."""
from pathlib import Path
import json, hashlib
import shutil
from pypdf import PdfReader
ROOT=Path(__file__).resolve().parent.parent
gui=json.loads((ROOT/'reports'/'gui-checks.json').read_text(encoding='utf-8-sig'))
assert gui['results'] and gui['results'][-1].get('file')=='flow:no unhandled UI errors', 'Wait for the completed window report before independent checks'
checks=[]
for result in gui['results']:
    source=result.get('retainedPdf') or result.get('previewSource')
    if source:source=str(ROOT/source)
    if source and source.lower().endswith('.pdf'):
        try:
            if not result.get('retainedPdf'):
                retained=ROOT/'reports'/'reference-pdfs'/(result['file']+'.pdf');retained.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,retained)
                result['retainedPdf']=str(retained);result['retainedPdfSha256']=hashlib.sha256(retained.read_bytes()).hexdigest();source=str(retained)
            reader=PdfReader(source);text='\n'.join(page.extract_text() or '' for page in reader.pages)
            expected='小熊读书' if any(s in result['file'] for s in ['旧文字','演示','开放文档']) else '办公 XML 测试' if result['file']=='办公表格.xml' else '中文'
            passed=expected in text or result['file']=='扫描样本.pdf'
            if any(s in result['file'] for s in ['演示','旧文字','分页样本','开放文档']):passed &= len(reader.pages)==2
            checks.append({'file':result['file'],'status':'PASS' if passed else 'FAIL','pages':len(reader.pages),'expectedText':expected,'expectedPresent':expected in text,'source':str(Path(source).relative_to(ROOT))})
        except Exception as error:checks.append({'file':result['file'],'status':'FAIL','error':str(error)})
for ext in ['txt','csv']:
    path=ROOT/'tests'/'saved'/('编辑样本.'+ext)
    if path.exists():
        text=path.read_text(encoding='utf-8-sig');checks.append({'file':path.name,'status':'PASS' if '编辑检查' in text and '001234567890123456' in text else 'FAIL','sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'reader':'Python utf-8-sig decoder'})
manifest=[]
for path in sorted((ROOT/'tests'/'fixtures').iterdir()):
    if path.is_file():
        source='scripts/make-odt-fixture.ps1; LibreOffice writer8 export of synthetic 阅读样本.docx' if path.suffix=='.odt' else 'scripts/make-sheet-fixtures.cjs; SheetJS 0.20.3 add-in writer; static hidden worksheets, no VBA' if path.suffix in ('.xla','.xlam') else 'scripts/make-fixtures.py, scripts/make-sheet-fixtures.cjs or LibreOffice documented export filters'
        manifest.append({'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'bytes':path.stat().st_size,'origin':'synthetic','source':source})
(ROOT/'data'/'fixture-manifest.json').write_text(json.dumps({'schemaVersion':1,'fixtures':manifest},ensure_ascii=False,indent=2),encoding='utf-8')
(ROOT/'reports'/'independent-checks.json').write_text(json.dumps({'checks':checks},ensure_ascii=False,indent=2),encoding='utf-8')
(ROOT/'reports'/'gui-checks.json').write_text(json.dumps(gui,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'checked':len(checks),'failures':[item for item in checks if item['status']=='FAIL']},ensure_ascii=False))
if any(item['status']=='FAIL' for item in checks):raise SystemExit(1)
