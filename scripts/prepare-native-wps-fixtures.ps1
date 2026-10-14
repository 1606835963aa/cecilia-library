param([switch]$Offline)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$catalog=Get-Content -LiteralPath (Join-Path $root 'data\native-wps-fixtures.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$destination=Join-Path $root 'tests\external-fixtures\native-wps'
$staging=Join-Path $root ('tmp\native-wps-download-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $destination -Force | Out-Null
New-Item -ItemType Directory -Path $staging -Force | Out-Null
try {
    foreach($fixture in $catalog.fixtures){
        if(!$fixture.sha256){[pscustomobject]@{file=$fixture.file;status='NOT_RUN';reason='Verified source file unavailable'};continue}
        if([IO.Path]::GetFileName($fixture.file) -ne $fixture.file){throw 'Invalid fixture filename'}
        $target=Join-Path $destination $fixture.file
        if((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq $fixture.sha256){[pscustomobject]@{file=$fixture.file;status='PRESENT'};continue}
        if($Offline){throw ('Fixture missing or hash mismatch: '+$fixture.file)}
        if($fixture.url){$uri=$fixture.url}
        else {$encoded=($fixture.sourcePath.Split('/') | ForEach-Object {[Uri]::EscapeDataString($_)}) -join '/';$uri='https://raw.githubusercontent.com/henriquead7/wps-osistematico-templates/'+$catalog.sourceCommit+'/'+$encoded}
        if(([Uri]$uri).Scheme -ne 'https'){throw 'Fixture sources must use HTTPS'}
        $download=Join-Path $staging $fixture.file
        Invoke-WebRequest -Uri $uri -OutFile $download -TimeoutSec 60
        if((Get-Item -LiteralPath $download).Length -gt 5MB -or (Get-FileHash -LiteralPath $download).Hash -ne $fixture.sha256){throw ('Fixture verification failed: '+$fixture.file)}
        Copy-Item -LiteralPath $download -Destination $target -Force
        [pscustomobject]@{file=$fixture.file;status='DOWNLOADED'}
    }
} finally {
    $resolved=[IO.Path]::GetFullPath($staging)
    $temporaryRoot=[IO.Path]::GetFullPath((Join-Path $root 'tmp'))+[IO.Path]::DirectorySeparatorChar
    if(!$resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid staging directory'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
