"""把 reports 中的本机绝对路径改写成相对项目根目录的路径。

用法：python -I scripts/sanitize-reports.py [--check]
--check 只检查，发现绝对路径时以非零状态退出。
"""
import json
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parent.parent
reports = root / "reports"
# JSON 中的 Windows 路径以双反斜杠转义，例如 C:\Users\...
absolute = re.compile(r'"[A-Za-z]:(?:\\|/)[^"]*"')


def project_prefixes():
    text = str(root)
    yield json.dumps(text + "\\", ensure_ascii=False)[1:-1]
    yield json.dumps(text, ensure_ascii=False)[1:-1]
    yield text.replace("\\", "/") + "/"
    yield text.replace("\\", "/")


def main():
    check = "--check" in sys.argv[1:]
    found = []
    for path in sorted(reports.rglob("*.json")):
        original = path.read_text(encoding="utf-8-sig")
        text = original
        for prefix in project_prefixes():
            text = text.replace(prefix, "")
        if not check and text != original:
            path.write_text(text, encoding="utf-8", newline="")
        for match in absolute.finditer(text):
            found.append(f"{path.relative_to(root)}: {match.group(0)[:80]}")
    for line in found:
        print(line)
    if found:
        print(f"{len(found)} 处绝对路径需要处理", file=sys.stderr)
        sys.exit(1 if check else 0)


if __name__ == "__main__":
    main()
