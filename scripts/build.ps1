param([switch]$Publish,[switch]$Installer)
if ($Installer) { $Publish = $true }
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
New-Item -ItemType Directory -Path tmp -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $projectRoot 'tmp\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$dotnetPath = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetPath)) { throw '请先运行 scripts\prepare.ps1' }
& npm run build
if ($LASTEXITCODE -ne 0) { throw 'TypeScript build failed' }
& $dotnetPath build 'src\LightReader.App\LightReader.App.csproj' -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Application build failed' }
if ($Publish) {
 # 安装包只收当前发布结果，先清掉旧版本留下的文件。
 if ($Installer -and (Test-Path -LiteralPath 'dist\LightReader')) { Remove-Item -LiteralPath 'dist\LightReader' -Recurse -Force }
 & $dotnetPath publish 'src\LightReader.App\LightReader.App.csproj' -c Release --no-restore --no-self-contained -o 'dist\LightReader'
 if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
 & $dotnetPath publish 'src\LightReader.PdfWorker\LightReader.PdfWorker.csproj' -c Release --no-restore --no-self-contained -o 'dist\LightReader'
 if ($LASTEXITCODE -ne 0) { throw 'PDF worker publish failed' }
}
if ($Installer) {
 $compiler = Join-Path $projectRoot '.tools\innosetup\ISCC.exe'
 if (!(Test-Path -LiteralPath $compiler)) { throw '请先运行 scripts\prepare.ps1 准备 Inno Setup' }
 $version = ([xml](Get-Content -LiteralPath 'src\LightReader.App\LightReader.App.csproj' -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
 $runtime = Get-Content -LiteralPath 'data\desktop-runtime.json' -Raw | ConvertFrom-Json
 $extensions = @((Get-Content -LiteralPath 'data\format-targets.json' -Raw | ConvertFrom-Json).formats | Where-Object verificationStatus -eq 'VERIFIED_SAMPLE' | ForEach-Object extension | Sort-Object -Unique)
 if ($extensions.Count -eq 0) { throw '没有已取得样本证据的格式可以登记' }
 $work = Join-Path $projectRoot 'tmp\installer'
 New-Item -ItemType Directory -Path $work -Force | Out-Null
 # 每个已验证后缀登记到“打开方式”，卸载时只删除本软件写入的值。
 $lines = foreach ($extension in $extensions) {
  'Root: HKA; Subkey: "Software\Classes\{0}\OpenWithProgids"; ValueType: string; ValueName: "{{#ProgId}}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate' -f $extension
  'Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{0}\OpenWithProgids"; ValueType: string; ValueName: "{{#ProgId}}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate' -f $extension
  'Root: HKA; Subkey: "{{#Capabilities}}\FileAssociations"; ValueType: string; ValueName: "{0}"; ValueData: "{{#ProgId}}"; Tasks: associate' -f $extension
  'Root: HKA; Subkey: "Software\Classes\Applications\{{#AppExe}}\SupportedTypes"; ValueType: string; ValueName: "{0}"; ValueData: ""; Tasks: associate' -f $extension
 }
 $associations = Join-Path $work 'associations.iss'
 [IO.File]::WriteAllLines($associations, [string[]]$lines, (New-Object Text.UTF8Encoding $true))
 $output = Join-Path $projectRoot 'dist\installer'
 & $compiler /Qp "/DAppVersion=$version" "/DSourceRoot=$projectRoot" "/DOutputDir=$output" "/DAssociations=$associations" "/DRuntimeVersion=$($runtime.version)" "/DRuntimeMajor=$($runtime.major)" "/DRuntimeUrl=$($runtime.url)" "/DRuntimeSha256=$($runtime.sha256)" "/DRuntimeMiB=$([math]::Round($runtime.bytes / 1MB))" 'installer\CeciliaLibrary.iss'
 if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
 Remove-Item -LiteralPath $work -Recurse -Force
}
