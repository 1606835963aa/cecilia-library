$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$scratch = Join-Path $projectRoot 'tmp'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $scratch 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
function Fetch([string]$url, [string]$path) {
    & curl.exe --fail --location --retry 3 --silent --show-error --output $path $url
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $url" }
}
$sdkRoot = Join-Path $projectRoot '.tools\dotnet'
if (!(Test-Path -LiteralPath (Join-Path $sdkRoot 'dotnet.exe'))) {
    $metadata = Invoke-RestMethod 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json'
    $release = $metadata.releases | Where-Object { [datetime]$_.'release-date' -le (Get-Date) -and $_.sdk.version -notmatch '-' } | Select-Object -First 1
    $asset = $release.sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.url.EndsWith('.zip') } | Select-Object -First 1
    $archive = Join-Path $scratch 'dotnet-sdk.zip'
    Write-Output "Downloading .NET SDK $($release.sdk.version)"
    Fetch $asset.url $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $asset.hash) { throw 'SDK checksum mismatch' }
    New-Item -ItemType Directory -Path $sdkRoot -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $sdkRoot -Force
    Remove-Item -LiteralPath $archive
    @{ sdk=$release.sdk.version; url=$asset.url; sha512=$asset.hash } | ConvertTo-Json | Set-Content 'data\sdk-version.json' -Encoding UTF8
}
$innoRoot = Join-Path $projectRoot '.tools\innosetup'
if (!(Test-Path -LiteralPath (Join-Path $innoRoot 'ISCC.exe'))) {
    # Inno Setup 只用于生成安装包，以便携方式放在 .tools，不写入系统。
    $innoSetup = Join-Path $scratch 'innosetup.exe'
    Write-Output 'Downloading Inno Setup 6.7.3'
    Fetch 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' $innoSetup
    if ((Get-FileHash -LiteralPath $innoSetup -Algorithm SHA256).Hash -ne '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732') { throw 'Inno Setup checksum mismatch' }
    $process = Start-Process -FilePath $innoSetup -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/PORTABLE=1','/NOICONS','/CURRENTUSER',('/DIR="' + $innoRoot + '"') -Wait -PassThru
    Remove-Item -LiteralPath $innoSetup
    if ($process.ExitCode -ne 0) { throw 'Inno Setup preparation failed' }
}
& (Join-Path $sdkRoot 'dotnet.exe') --version
$env:DOTNET_CLI_HOME = Join-Path $scratch 'dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& (Join-Path $sdkRoot 'dotnet.exe') restore 'src\LightReader.App\LightReader.App.csproj' --configfile 'NuGet.Config'
if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
$env:npm_config_cache=Join-Path $scratch 'npm-cache'
& npm ci --ignore-scripts --no-audit --no-fund
if($LASTEXITCODE -ne 0){throw 'TypeScript dependency preparation failed'}
& (Join-Path $PSScriptRoot 'prepare-components.ps1')
