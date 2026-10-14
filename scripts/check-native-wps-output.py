from pathlib import Path
import hashlib,json
from datetime import datetime,timezone
from pypdf import PdfReader
from PIL import Image,ImageChops

ROOT=Path(__file__).resolve().parent.parent
def read(name):return json.loads((ROOT/name).read_text(encoding='utf-8-sig'))
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest().upper()
catalog=read('data/native-wps-fixtures.json')
display=read('reports/native-wps-display.json')
structure=read('reports/native-wps-structure.json')
checks=[]
assert not display['unhandledErrors'],'Unhandled error in display probe'
for fixture in catalog['fixtures']:
    try:
        result=next(item for item in display['results'] if item['id']==fixture['id'])
        container=next(item for item in structure['results'] if item['id']==fixture['id'])
        smoke=fixture['scope']=='blank-smoke'
        status='SMOKE_ONLY' if smoke else 'PASS'
        assert result['status']==status and container['status']==status,'Incomplete display or format check'
        source=ROOT/catalog['storage']/fixture['file']
        assert digest(source)==result['sha256']==container['sha256']==fixture['sha256'],'Source changed'
        assert result['sourceUnchanged'],'Source did not unlock or changed'
        reference=ROOT/result['referencePdf']
        pdf=PdfReader(reference)
        assert len(pdf.pages)==fixture['expectedPages']==len(result['pages']),'Page count mismatch'
        text=''.join(''.join(page.extract_text() or '' for page in pdf.pages).split())
        text_checks=[{'text':expected,'present':''.join(expected.split()) in text} for expected in fixture['expectedText']]
        assert all(item['present'] for item in text_checks),'Expected content missing from PDF'
        page_checks=[]
        for page in result['pages']:
            image=ROOT/page['preview']
            assert digest(image)==page['sha256'] and page['rendered'],'Bitmap changed or not shown'
            with Image.open(image) as bitmap:
                rgb=bitmap.convert('RGB')
                ink=ImageChops.difference(rgb,Image.new('RGB',rgb.size,'white')).getbbox() is not None
                assert smoke or ink,'Unexpected blank page'
                page_checks.append({'page':page['page'],'width':rgb.width,'height':rgb.height,'hasInk':ink})
        checks.append({'id':fixture['id'],'file':fixture['file'],'extension':fixture['extension'],'status':status,'sha256':fixture['sha256'],
            'pages':len(pdf.pages),'referencePdfSha256':digest(reference),'textChecks':text_checks,'pageChecks':page_checks})
    except Exception as error:checks.append({'id':fixture['id'],'file':fixture['file'],'status':'FAIL','error':str(error)})
report={'checkedAt':datetime.now(timezone.utc).isoformat(),'kind':'Independent PDF extraction with pypdf and bitmap checks with Pillow; visual review recorded separately',
    'applicationSha256':display['applicationSha256'],'checks':checks,'verifiedExtensions':sorted({item['extension'] for item in checks if item['status']=='PASS'})}
(ROOT/'reports/native-wps-independent.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'checks':len(checks),'verifiedExtensions':report['verifiedExtensions'],'failures':[item for item in checks if item['status']=='FAIL']},ensure_ascii=False))
if any(item['status']=='FAIL' for item in checks):raise SystemExit(1)
