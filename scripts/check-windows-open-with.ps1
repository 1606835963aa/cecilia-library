param([string]$Report='reports\windows-open-with-checks.json')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if(!('CeciliaLibrary.Diagnostics.OpenWithProbe' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'WindowsOpenWithProbe.cs')}
$executable=Join-Path $projectRoot 'dist\LightReader\LightReader.exe'
$formats=(Get-Content -LiteralPath (Join-Path $projectRoot 'data\format-targets.json') -Raw | ConvertFrom-Json).formats
$results=@()
foreach($extension in @($formats | Where-Object verificationStatus -eq 'VERIFIED_SAMPLE' | ForEach-Object extension | Sort-Object -Unique)){
    foreach($recommended in @($false,$true)){
        $handlers=@([CeciliaLibrary.Diagnostics.OpenWithProbe]::Enumerate($extension,$recommended))
        $ours=@($handlers | Where-Object {$_.Name -eq $executable})
        $valid=$ours.Count -eq 1 -and $ours[0].DisplayName -eq '塞西莉亚的图书馆' -and $ours[0].NameResult -eq 0 -and $ours[0].DisplayNameResult -eq 0 -and $ours[0].Recommended
        $results+=@{extension=$extension;filter=$(if($recommended){'recommended'}else{'all'});status=$(if($valid){'PASS'}else{'FAIL'});matchingHandlers=@($ours | Select-Object Name,DisplayName,Recommended)}
    }
}
$reportPath=[IO.Path]::GetFullPath((Join-Path $projectRoot $Report))
if(!$reportPath.StartsWith($projectRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Report must stay inside the project'}
@{checkedAt=(Get-Date).ToString('o');method='Read-only SHAssocEnumHandlers and IAssocHandler name/recommendation queries; no dialog clicks';results=$results;uiVerification='BLOCKED_ENV: Computer Use trusted RPC service cannot load';scope='Current desktop user; sample-verified extensions only'} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $reportPath -Encoding UTF8
@{checks=$results.Count;failures=@($results | Where-Object status -ne 'PASS')} | ConvertTo-Json -Depth 6
if(@($results | Where-Object status -ne 'PASS').Count -gt 0){throw 'Windows Open With candidate checks failed'}
