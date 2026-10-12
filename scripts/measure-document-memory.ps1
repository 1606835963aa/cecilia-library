$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$env:DOTNET_ROOT=Join-Path $projectRoot '.tools\dotnet'
$records=@()
foreach($file in @('阅读样本.docx','数据样本.xlsx','分页样本.pdf','旧文字样本.doc')){
    $runId=[guid]::NewGuid().ToString('N')
    $profile=Join-Path $projectRoot ('tmp\document-profile-'+$runId)
    New-Item -ItemType Directory -Path $profile -Force | Out-Null
    New-Item -ItemType Junction -Path (Join-Path $profile 'components') -Target (Join-Path $projectRoot 'components') | Out-Null
    $stateFile=Join-Path $projectRoot ('tmp\document-state-'+$runId+'.json')
    $reader=Start-Process -FilePath (Join-Path $projectRoot 'dist\LightReader\LightReader.exe') -ArgumentList @('--data-dir',('"'+$profile+'"'),'--state-file',('"'+$stateFile+'"'),('"'+(Join-Path $projectRoot ('tests\fixtures\'+$file))+'"')) -PassThru -WindowStyle Hidden
    try{
        $timer=[Diagnostics.Stopwatch]::StartNew()
        while(!(Test-Path -LiteralPath $stateFile)){if($reader.HasExited -or $timer.Elapsed.TotalSeconds -gt 90){throw 'Document did not reach ready state'};Start-Sleep -Milliseconds 50}
        $state=Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        if(!$state.shown){throw 'Document is not displayed'}
        Start-Sleep -Milliseconds 1000
        $tree=Get-CimInstance Win32_Process
        $ids=@([uint32]$reader.Id)
        do{$extra=@($tree | Where-Object {$ids -contains $_.ParentProcessId -and $ids -notcontains $_.ProcessId} | Select-Object -ExpandProperty ProcessId);$ids+=$extra}while($extra.Count -gt 0)
        $owned=@($tree | Where-Object {$ids -contains $_.ProcessId})
        $records+=@{file=$file;sampleAfter='UI-ready marker + 1000ms';renderer=$state.renderer;workingSetBytes=($owned | Measure-Object WorkingSetSize -Sum).Sum;processes=@($owned | ForEach-Object {@{name=$_.Name;workingSetBytes=$_.WorkingSetSize}})}
    }finally{if(!$reader.HasExited){Stop-Process -Id $reader.Id}}
}
@{measuredAt=(Get-Date).ToString('o');method='Sum of working sets, including shared pages and all descendants. One isolated profile per document; installed components, fresh Office conversion cache. Only sample after the actual document-ready marker.';records=$records} | ConvertTo-Json -Depth 6 | Set-Content 'reports\document-memory.json' -Encoding UTF8
$records | ForEach-Object {@{file=$_.file;MiB=$_.workingSetBytes/1MB}} | ConvertTo-Json
