## 改了什么



## 怎样验证的

- [ ] 核心检查通过（`dotnet run --project tests/LightReader.Checks -c Release -- 项目目录`）
- [ ] 改动涉及显示时，已用 `tests/fixtures` 中的样本实际打开查看
- [ ] 报告中没有本机路径（`python -I scripts/sanitize-reports.py --check`）
