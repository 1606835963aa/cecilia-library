# GitHub 仓库

中文项目名：塞西莉亚的图书馆。英文名：Cecilia's Library。

仓库：[1606835963aa/cecilia-library](https://github.com/1606835963aa/cecilia-library)，以 AGPL-3.0 开源。

GitHub 仓库地址名只接受英文字母、数字、点、横线和下划线，中文名保留在 README 与仓库描述中。[GitHub 命名规则](https://docs.github.com/en/repositories/creating-and-managing-repositories/creating-a-new-repository)

Git 保存源码、构建与安装包脚本、依赖版本、合成测试样本与验收记录。开发工具、下载后的组件、成品目录、缓存、本机配置和私人样本通过 `.gitignore` 排除。

每次推送和拉取请求由 [GitHub Actions](../.github/workflows/checks.yml) 编译并运行核心检查。发布时在本机执行 `scripts\build.ps1 -Installer`，把 `dist\installer` 中的安装包上传到 Releases，版本号与 `src/LightReader.App/LightReader.App.csproj` 一致。

在另一台电脑上使用源码，请按 [构建说明](构建说明.md) 准备依赖并构建。
