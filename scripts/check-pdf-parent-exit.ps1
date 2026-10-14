param([string]$Application='dist\LightReader\LightReader.exe',[string]$Report='reports\pdf-parent-exit.json')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$run=Join-Path $projectRoot ('tmp\pdf-parent-exit-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
@{projectRoot=$projectRoot;outputDirectory=$run;iterations=1;pdfOnly=$true;parentExit=$true} | ConvertTo-Json | Set-Content (Join-Path $run 'config.json') -Encoding UTF8
$env:DOTNET_ROOT=Join-Path $projectRoot '.tools\dotnet'
$app=Start-Process -FilePath (Join-Path $projectRoot $Application) -ArgumentList @('--lifecycle-probe',('"'+(Join-Path $run 'config.json')+'"'),'--data-dir',('"'+(Join-Path $run 'profile')+'"')) -WindowStyle Hidden -PassThru
$watch=[Diagnostics.Stopwatch]::StartNew()
$marker=Join-Path $run 'parent-exit.json'
try {
    while(!(Test-Path -LiteralPath $marker)){
        foreach($stage in Get-ChildItem -LiteralPath $run -Filter stage-*.json){[IO.File]::WriteAllText((Join-Path $run ($stage.BaseName.Replace('stage-','ack-'))),'sampled')}
        if($app.HasExited -or $watch.Elapsed.TotalSeconds -gt 45){throw 'Parent-exit probe failed before marker'}
        Start-Sleep -Milliseconds 40
    }
    $record=Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
    if($record.parentId -ne $app.Id -or !$record.workerId){throw 'Unexpected probe identities'}
    if(!$app.WaitForExit(5000)){throw 'Parent did not exit'}
    $child=Get-Process -Id $record.workerId -ErrorAction SilentlyContinue
    if($child -and !$child.WaitForExit(5000)){throw 'PDF worker survived parent'}
    $source=Join-Path $projectRoot 'tests\fixtures\分页样本.pdf'
    $exclusive=[IO.File]::Open($source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
    $exclusive.Dispose()
    @{status='PASS';method='Application-owned test exits parent immediately while PDF is displayed, without disposing the reader; worker must exit and source must unlock';applicationSha256=(Get-FileHash (Join-Path ([IO.Path]::GetDirectoryName((Join-Path $projectRoot $Application))) 'LightReader.dll')).Hash;workerSha256=(Get-FileHash (Join-Path ([IO.Path]::GetDirectoryName((Join-Path $projectRoot $Application))) 'LightReader.PdfWorker.dll')).Hash;elapsedMs=$watch.ElapsedMilliseconds;sourceUnlocked=$true} | ConvertTo-Json | Set-Content -LiteralPath $Report -Encoding UTF8
    Write-Output 'PDF parent-exit check PASS'
} finally {if(!$app.HasExited){Stop-Process -Id $app.Id}}
