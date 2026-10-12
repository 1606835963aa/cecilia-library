param([string[]]$Components=@('word','sheet'))
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
New-Item -ItemType Directory -Path tmp -Force | Out-Null
$env:DOTNET_CLI_HOME=Join-Path $projectRoot 'tmp\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
& '.\.tools\dotnet\dotnet.exe' run --project 'src\LightReader.Tooling\LightReader.Tooling.csproj' -c Release -- $projectRoot @Components
if($LASTEXITCODE -ne 0){throw 'Component preparation failed'}
