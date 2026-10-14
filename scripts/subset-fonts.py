"""生成界面字体子集。

用法：python scripts/subset-fonts.py 字体源目录
字体源目录放 Google Fonts 的 NotoSerifSC[wght].ttf、InstrumentSerif-Regular.ttf、InstrumentSerif-Italic.ttf。
中文宋体只保留界面源码里出现的字；文件名等动态文字使用系统的微软雅黑。
"""
import json
import pathlib
import re
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

root = pathlib.Path(__file__).resolve().parent.parent
source = pathlib.Path(sys.argv[1])
output = root / "src" / "LightReader.App" / "Fonts"
output.mkdir(exist_ok=True)

# 只取 C# 字符串和 XAML 属性值里的字，注释不进字体
text = ""
for p in (root / "src" / "LightReader.App").rglob("*"):
    if p.suffix not in (".xaml", ".cs") or "obj" in p.parts or "bin" in p.parts:
        continue
    code = p.read_text(encoding="utf-8")
    pattern = r'"((?:[^"\\\n]|\\.)*)"' if p.suffix == ".cs" else r'="([^"]*)"'
    text += "".join(re.findall(pattern, code))
# 组件名称来自组件清单，在组件页用宋体显示
text += "".join(c["name"] for c in json.loads((root / "data" / "component-manifest.json").read_text(encoding="utf-8"))["components"])
characters = {chr(c) for c in range(0x20, 0x7F)} | {c for c in text if ord(c) > 0x7F} | set("，。、：；？！“”‘’（）《》〈〉…—·→←↗")


def rename(font: TTFont, family: str, style: str) -> None:
    names = font["name"]
    for record in list(names.names):
        if record.nameID in (1, 2, 3, 4, 6, 16, 17, 21, 22, 25):
            names.removeNames(nameID=record.nameID)
    full = family if style == "Regular" else f"{family} {style}"
    for nameID, value in ((1, family), (2, style), (3, f"{full};Cecilia Library"), (4, full), (6, full.replace(" ", ""))):
        names.setName(value, nameID, 3, 1, 0x409)


def save(font: TTFont, keep: set[str], family: str, style: str, file: str) -> None:
    options = Options()
    options.name_IDs = ["*"]
    options.name_languages = ["*"]
    options.layout_features = ["*"]
    options.hinting = True
    subsetter = Subsetter(options)
    subsetter.populate(text="".join(sorted(keep)))
    subsetter.subset(font)
    rename(font, family, style)
    if "OS/2" in font:
        font["OS/2"].usWeightClass = 700 if style == "Bold" else 400
        font["OS/2"].fsSelection = (font["OS/2"].fsSelection & ~0b1100001) | (0b100000 if style == "Bold" else 0b1000000 if style == "Regular" else 1)
    font["head"].macStyle = 1 if style == "Bold" else 2 if style == "Italic" else 0
    font.save(output / file)
    print(file, (output / file).stat().st_size)


for weight, style in ((400, "Regular"), (700, "Bold")):
    variable = TTFont(source / "NotoSerifSC[wght].ttf")
    static = instancer.instantiateVariableFont(variable, {"wght": weight})
    save(static, characters, "Cecilia Serif", style, f"CeciliaSerif-{style}.ttf")

latin = {chr(c) for c in range(0x20, 0x7F)} | set("·→←↗–—’‘“”…")
for file, style in (("InstrumentSerif-Regular.ttf", "Regular"), ("InstrumentSerif-Italic.ttf", "Italic")):
    save(TTFont(source / file), latin, "Cecilia Latin", style, f"CeciliaLatin-{style}.ttf")
