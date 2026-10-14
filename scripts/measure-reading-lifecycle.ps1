param([string]$Application='dist\LightReader\LightReader.exe',[string]$Label='after',[int]$Iterations=10,[string]$Report,[switch]$PdfOnly,[switch]$AllowFailedChecks)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
if(!$Report){$Report='reports\memory-lifecycle-'+$Label+'.json'}
$executable=[IO.Path]::GetFullPath((Join-Path $projectRoot $Application))
$run=Join-Path $projectRoot ('tmp\lifecycle-'+$Label+'-'+[guid]::NewGuid().ToString('N'))
$profile=Join-Path $run 'profile'
New-Item -ItemType Directory -Path $profile -Force | Out-Null
New-Item -ItemType Junction -Path (Join-Path $profile 'components') -Target (Join-Path $projectRoot 'components') | Out-Null
@{projectRoot=$projectRoot;outputDirectory=$run;iterations=$Iterations;pdfOnly=$PdfOnly.IsPresent} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'config.json') -Encoding UTF8
$env:DOTNET_ROOT=Join-Path $projectRoot '.tools\dotnet'
$reader=Start-Process -FilePath $executable -ArgumentList @('--lifecycle-probe',('"'+(Join-Path $run 'config.json')+'"'),'--data-dir',('"'+$profile+'"')) -WindowStyle Hidden -PassThru
$readerStarted=$reader.StartTime.ToUniversalTime()
$known=@{}
$records=@()
$sequence=1
$timer=[Diagnostics.Stopwatch]::StartNew()
$complete=$null
try {
    while(!(Test-Path -LiteralPath (Join-Path $run 'complete.json'))){
        $stage=Join-Path $run ('stage-{0:D4}.json' -f $sequence)
        if(!(Test-Path -LiteralPath $stage)){
            if($reader.HasExited){throw ('Lifecycle probe exited before completion; exit code '+$reader.ExitCode+'; last sampled stage '+($sequence-1))}
            if($timer.Elapsed.TotalMinutes -gt 15){throw 'Lifecycle sampler timed out after 15 minutes'}
            Start-Sleep -Milliseconds 60
            continue
        }
        $snapshot=Get-Content -LiteralPath $stage -Raw | ConvertFrom-Json
        $tree=@(Get-CimInstance Win32_Process)
        $liveById=@{}
        foreach($entry in $tree){$liveById[[uint32]$entry.ProcessId]=$entry}
        $rootProcess=$liveById[[uint32]$reader.Id]
        if($reader.HasExited -or !$rootProcess -or [Math]::Abs(($rootProcess.CreationDate.ToUniversalTime()-$readerStarted).TotalMilliseconds) -ge 1){throw 'Measurement application exited or its PID was reused'}
        $active=@([uint32]$reader.Id)+@($tree | Where-Object {$known.ContainsKey($_.ProcessId.ToString()) -and $known[$_.ProcessId.ToString()] -eq $_.CreationDate} | Select-Object -ExpandProperty ProcessId)
        do {
            $extra=@($tree | Where-Object {
                $parent=$liveById[[uint32]$_.ParentProcessId]
                $active -contains $_.ParentProcessId -and $active -notcontains $_.ProcessId -and
                    $parent -and $_.CreationDate -ge $parent.CreationDate
            } | Select-Object -ExpandProperty ProcessId)
            $active+=$extra
        } while($extra.Count -gt 0)
        foreach($owned in @($tree | Where-Object {$active -contains $_.ProcessId})){$known[$owned.ProcessId.ToString()]=$owned.CreationDate}
        $owned=@($tree | Where-Object {$known.ContainsKey($_.ProcessId.ToString()) -and $known[$_.ProcessId.ToString()] -eq $_.CreationDate})
        $snapshot | Add-Member -NotePropertyName treeWorkingSetBytes -NotePropertyValue ([long]($owned | Measure-Object WorkingSetSize -Sum).Sum)
        $snapshot | Add-Member -NotePropertyName treePrivateBytes -NotePropertyValue ([long]($owned | Measure-Object PrivatePageCount -Sum).Sum)
        $snapshot | Add-Member -NotePropertyName processes -NotePropertyValue @($owned | ForEach-Object {@{id=$_.ProcessId;parentId=$_.ParentProcessId;createdAt=$_.CreationDate.ToString('o');name=$_.Name;workingSetBytes=[long]$_.WorkingSetSize;privateBytes=[long]$_.PrivatePageCount}})
        $records+=$snapshot
        [IO.File]::WriteAllText((Join-Path $run ('ack-{0:D4}' -f $sequence)),'sampled')
        $sequence++
    }
    $complete=Get-Content -LiteralPath (Join-Path $run 'complete.json') -Raw | ConvertFrom-Json
    $dll=Join-Path ([IO.Path]::GetDirectoryName($executable)) 'LightReader.dll'
    $fixtures=@('tests\fixtures\阅读样本.docx','tests\fixtures\数据样本.xlsx','tests\fixtures\分页样本.pdf','tests\fixtures\多页样本.tif','tests\fixtures\文本样本.txt','tmp\memory-fixtures\内存文字样本.docx','tmp\memory-fixtures\内存表格样本.xlsx') | ForEach-Object {
        @{file=$_;bytes=(Get-Item -LiteralPath $_).Length;sha256=(Get-FileHash -LiteralPath $_).Hash}
    }
    @{label=$Label;measuredAt=(Get-Date).ToString('o');applicationSha256=(Get-FileHash -LiteralPath $dll).Hash;applicationVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion;hostOs=[Environment]::OSVersion.VersionString;iterations=$Iterations;method='Whole process tree plus previously observed surviving descendants. PID identity and child creation after parent checked to exclude reused PIDs. Natural idle; no forced GC or working-set trimming. Same isolated fresh profile and generated fixtures for before/after.';fixtures=$fixtures;records=$records;probe=$complete} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Report -Encoding UTF8
    @{label=$Label;samples=$records.Count;failedChecks=@($complete.checks | Where-Object status -ne 'PASS').Count;error=$complete.error} | ConvertTo-Json
    if($complete.error){throw $complete.error}
    if(!$AllowFailedChecks -and @($complete.checks | Where-Object status -ne 'PASS').Count -gt 0){throw 'Lifecycle checks failed; inspect the saved report'}
} catch {
    if($null -eq $complete){
        $exitCode=if($reader.HasExited){$reader.ExitCode}else{$null}
        @{label=$Label;status='INTERRUPTED';measuredAt=(Get-Date).ToString('o');applicationSha256=(Get-FileHash -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($executable)) 'LightReader.dll')).Hash;
            iterations=$Iterations;records=$records;exitCode=$exitCode;lastSampledStage=$sequence-1;probe=@{error=$_.Exception.Message;checks=@();unhandledErrors=$null};
            method='Incomplete lifecycle sample; not acceptance. Available process-tree checkpoints retained.'} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Report -Encoding UTF8
    }
    throw
} finally {
    if(!$reader.HasExited){Stop-Process -Id $reader.Id}
}
