# 参与贡献

谢谢你愿意帮塞西莉亚的图书馆变得更好。问题反馈、格式样本、文档修正和代码改进都欢迎。

## 反馈问题

在 [Issues](https://github.com/1606835963aa/cecilia-library/issues) 选择“问题反馈”或“功能建议”。文档显示不对时，最有用的信息是：文件格式、由哪个软件哪个版本保存、截图。

请不要上传含个人信息的真实文档。能用一份去掉敏感内容、仍能复现问题的文件就最好了。

## 提交代码

1. 按 [构建说明](docs/构建说明.md) 运行 `scripts\prepare.ps1` 和 `scripts\build.ps1 -Publish`。
2. 在自己的分支上修改。保持周围代码的写法；阅读页面改 `src/preview/*.ts`，再用 `npm run build` 生成 `src/preview/www` 中的脚本，两者一起提交。
3. 提交前运行：

   ```powershell
   .\.tools\dotnet\dotnet.exe run --project tests/LightReader.Checks -c Release -- $PWD
   node .\scripts\check-preview-behavior.cjs
   python -I .\scripts\sanitize-reports.py
   ```

   最后一条把 `reports` 中的本机绝对路径改成相对路径。
4. 涉及显示的改动，请用 `tests/fixtures` 中的样本实际打开看一遍，在拉取请求里说明看过哪些格式。

拉取请求会自动运行编译和核心检查。办公兼容组件约 356 MB，自动检查不下载它，相关检查记为跳过；改动办公转换时请在本机完整运行。

## 约定

- **文档只在本机处理**。不添加上传、统计或任何联网读取文档的功能。
- **组件按需安装**。新的第三方组件写进 [组件清单](data/component-manifest.json)：固定版本的下载地址、字节数和 SHA-256，许可证放进 `licenses/`，并在 [依赖清单](docs/依赖清单.md) 登记。许可证需与 AGPL-3.0 兼容。
- **如实记录验证**。区分“代码已实现”“样本验证通过”“完整验收”“未运行”。构建通过不能代替显示验收；失败记录保留在 [开发进度](docs/开发进度.md)。
- **测试样本**只用合成或可公开的文件，并在 [样本清单](data/fixture-manifest.json) 记录来源。
- 不提交 `.tools`、`.packages`、`components`、`dist`、`.local-data`、`node_modules`、`tmp`。

## 许可

提交到本项目的贡献，以 [AGPL-3.0](LICENSE) 发布。
