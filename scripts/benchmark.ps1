param([int]$ColdRuns=5,[int]$RepeatRuns=10)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$env:DOTNET_ROOT = Join-Path $projectRoot '.tools\dotnet'
$samples = @()
$runId=[guid]::NewGuid().ToString('N')
$machine = Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer,Model,TotalPhysicalMemory
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1 Name
for ($index=0; $index -lt $ColdRuns+$RepeatRuns; $index++) {
    $output = Join-Path $projectRoot ('tmp\benchmark-'+$runId+'-'+$index+'.json')
    $profile = Join-Path $projectRoot ('tmp\benchmark-profile-'+$runId+'-'+$(if($index -lt $ColdRuns){$index}else{'repeat'}))
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $reader = Start-Process -FilePath (Join-Path $projectRoot 'dist\LightReader\LightReader.exe') -ArgumentList @('--data-dir',('"'+$profile+'"'),'--benchmark',('"'+$output+'"')) -PassThru -WindowStyle Hidden
    while (!(Test-Path -LiteralPath $output)) {
        if ($reader.HasExited -or $timer.Elapsed.TotalSeconds -gt 30) { throw "Benchmark $index failed" }
        Start-Sleep -Milliseconds 20
    }
    $sample=Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    $processTree=Get-CimInstance Win32_Process
    $ownedIds=@([uint32]$reader.Id)
    do {
      $extra=@($processTree | Where-Object { $ownedIds -contains $_.ParentProcessId -and $ownedIds -notcontains $_.ProcessId } | Select-Object -ExpandProperty ProcessId)
      $ownedIds += $extra
    } while($extra.Count -gt 0)
    $ownedTree=@($processTree | Where-Object { $ownedIds -contains $_.ProcessId })
    $sample | Add-Member -NotePropertyName childCount -NotePropertyValue ($ownedTree.Count-1)
    $sample | Add-Member -NotePropertyName treeWorkingSetBytes -NotePropertyValue (($ownedTree | Measure-Object WorkingSetSize -Sum).Sum)
    $sample | Add-Member -NotePropertyName launchToRecordMs -NotePropertyValue $timer.ElapsedMilliseconds
    $sample | Add-Member -NotePropertyName category -NotePropertyValue $(if($index -lt $ColdRuns){'new-profile'}else{'repeat-profile'})
    $samples += $sample
    $reader.WaitForExit()
}
$compressed = Join-Path $projectRoot 'dist\LightReader-0.1.0-win-x64.zip'
Compress-Archive -LiteralPath 'dist\LightReader' -DestinationPath $compressed -Force
$versions = & '.\.tools\dotnet\dotnet.exe' --list-runtimes
$disk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='Z:'" | Select-Object DeviceID,DriveType,FileSystem,VolumeName
@{ measuredAt=(Get-Date).ToString('o'); machine=$machine; cpu=$cpu; disk=$disk; samples=$samples; mainFolderBytes=(Get-ChildItem -LiteralPath 'dist\LightReader' -Recurse -File | Measure-Object Length -Sum).Sum; zipBytes=(Get-Item -LiteralPath $compressed).Length; sharedRuntimes=$versions; excludes=@('shared WebView2 runtime','shared .NET runtime','optional components','developer SDK','document cache') } | ConvertTo-Json -Depth 10 | Set-Content 'reports\performance.json' -Encoding UTF8
$samples | Select-Object category,uiReadyMs,workingSetBytes,childCount
