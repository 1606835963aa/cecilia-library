from pathlib import Path
from io import BytesIO
import argparse,hashlib,json,subprocess
from PIL import Image,ImageChops

ROOT=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser()
parser.add_argument('--baseline',required=True)
parser.add_argument('--output',default='reports/pdf-pixel-012.json')
args=parser.parse_args()
revision=subprocess.check_output(['git','rev-parse','--verify',args.baseline+'^{commit}'],cwd=ROOT,text=True).strip()
checks=[]
for name in ['分页样本.pdf.png','扫描样本.pdf.png','native-pdf-page2.png']:
    relative='reports/previews/'+name
    before=subprocess.check_output(['git','show',revision+':'+relative],cwd=ROOT)
    after=(ROOT/relative).read_bytes()
    with Image.open(BytesIO(before)) as left,Image.open(BytesIO(after)) as right:
        equal=left.size==right.size and all(high==0 for low,high in ImageChops.difference(left.convert('RGBA'),right.convert('RGBA')).getextrema())
        checks.append({'file':relative,'status':'PASS' if equal else 'FAIL','beforeSha256':hashlib.sha256(before).hexdigest(),'afterSha256':hashlib.sha256(after).hexdigest(),'size':list(right.size)})
report={'baselineCommit':revision,'kind':'Before/after application-owned rendered PNG pixels; excludes changes outside these three synthetic PDF pages','checks':checks}
(ROOT/args.output).write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'checks':len(checks),'failures':[item for item in checks if item['status']=='FAIL']},ensure_ascii=False))
if any(item['status']=='FAIL' for item in checks):raise SystemExit(1)
