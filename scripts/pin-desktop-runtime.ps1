# 把安装包下载的 .NET 桌面运行时固定到最新正式补丁，并记录大小和 SHA-256。
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$scratch = Join-Path $projectRoot 'tmp'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$metadata = Invoke-RestMethod 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json'
$release = $metadata.releases | Where-Object { [datetime]$_.'release-date' -le (Get-Date) -and $_.windowsdesktop.version -notmatch '-' } | Select-Object -First 1
$asset = $release.windowsdesktop.files | Where-Object { $_.rid -eq 'win-x64' -and $_.url.EndsWith('.exe') } | Select-Object -First 1
$installer = Join-Path $scratch 'windowsdesktop-runtime.exe'
& curl.exe --fail --location --retry 3 --silent --show-error --output $installer $asset.url
if ($LASTEXITCODE -ne 0) { throw "Download failed: $($asset.url)" }
try {
    if ((Get-FileHash -LiteralPath $installer -Algorithm SHA512).Hash -ne $asset.hash) { throw 'Runtime checksum mismatch' }
    if ((Get-AuthenticodeSignature -LiteralPath $installer).Status -ne 'Valid') { throw 'Runtime signature invalid' }
    [ordered]@{
        version = $release.windowsdesktop.version
        major = $release.windowsdesktop.version.Split('.')[0]
        url = $asset.url
        bytes = (Get-Item -LiteralPath $installer).Length
        sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
        sha512 = $asset.hash
    } | ConvertTo-Json | Set-Content 'data\desktop-runtime.json' -Encoding UTF8
    Write-Output "Pinned .NET Desktop Runtime $($release.windowsdesktop.version)"
} finally { Remove-Item -LiteralPath $installer -Force }
