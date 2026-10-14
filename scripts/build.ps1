param([switch]$Publish)
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
 & $dotnetPath publish 'src\LightReader.App\LightReader.App.csproj' -c Release --no-restore --no-self-contained -o 'dist\LightReader'
 if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
 & $dotnetPath publish 'src\LightReader.PdfWorker\LightReader.PdfWorker.csproj' -c Release --no-restore --no-self-contained -o 'dist\LightReader'
 if ($LASTEXITCODE -ne 0) { throw 'PDF worker publish failed' }
}
