param([string]$Before='tmp\release-013\LightReader.exe',[string]$After='dist\LightReader\LightReader.exe',[int]$Rounds=5,[string]$Report='reports\pdf-open-014.json')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$run=Join-Path $projectRoot ('tmp\pdf-open-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$env:DOTNET_ROOT=Join-Path $projectRoot '.tools\dotnet'
$source=Join-Path $projectRoot 'tests\fixtures\分页样本.pdf'
$records=@()
for($round=1;$round -le $Rounds;$round++){
    foreach($target in @(@{label='before';path=$Before},@{label='after';path=$After})){
        $executable=Join-Path $projectRoot $target.path
        $marker=Join-Path $run ($target.label+'-'+$round+'.json')
        $profile=Join-Path $run ($target.label+'-'+$round+'-profile')
        $timer=[Diagnostics.Stopwatch]::StartNew()
        $reader=Start-Process -FilePath $executable -ArgumentList @(('"'+$source+'"'),'--state-file',('"'+$marker+'"'),'--data-dir',('"'+$profile+'"')) -WindowStyle Hidden -PassThru
        try {
            $state=$null
            while($null -eq $state){
                if($reader.HasExited -or $timer.Elapsed.TotalSeconds -gt 30){throw 'PDF first-page benchmark did not finish'}
                if(Test-Path -LiteralPath $marker){try{$state=Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json}catch{}}
                if($null -eq $state){Start-Sleep -Milliseconds 20}
            }
            $elapsed=$timer.Elapsed.TotalMilliseconds
            if(!$state.shown -or $state.renderer -ne 'Windows.Data.Pdf'){throw 'PDF native page not shown'}
            $records+=@{label=$target.label;round=$round;milliseconds=$elapsed;applicationVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($executable).FileVersion;applicationSha256=(Get-FileHash (Join-Path ([IO.Path]::GetDirectoryName($executable)) 'LightReader.dll')).Hash;state=$state}
        } finally {if(!$reader.HasExited){Stop-Process -Id $reader.Id};$reader.WaitForExit()}
    }
}
@{method='Alternating before/after launches with fresh isolated profiles; stopwatch spans process start through application-owned native first-page ready marker; 20ms polling, not a screen paint measurement';sourceSha256=(Get-FileHash $source).Hash;records=$records} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Report -Encoding UTF8
Write-Output ('PDF open benchmark complete: '+$records.Count+' launches')
