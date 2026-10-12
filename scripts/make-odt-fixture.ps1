$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$scratchRoot=Join-Path $projectRoot 'tmp'
$work=Join-Path $scratchRoot ('odt-fixture-'+[guid]::NewGuid().ToString('N'))
$profile=Join-Path $work 'profile'
$output=Join-Path $work 'output'
$officeExecutable=Join-Path $projectRoot 'components\office\26.2.6\program\soffice.exe'
if(!(Test-Path -LiteralPath $officeExecutable)){throw '请先准备 office 组件'}
New-Item -ItemType Directory -Path $profile,$output -Force | Out-Null
try {
    $profileUri=([uri]($profile+'\')).AbsoluteUri
    $conversionProcess=Start-Process -FilePath $officeExecutable -ArgumentList @('-env:UserInstallation='+$profileUri,'--headless','--nologo','--nodefault','--norestore','--convert-to','odt:writer8','--outdir',('"'+$output+'"'),('"'+(Join-Path $projectRoot 'tests\fixtures\阅读样本.docx')+'"')) -WindowStyle Hidden -PassThru -Wait
    if($conversionProcess.ExitCode -ne 0){throw 'ODT 导出失败'}
    Copy-Item -LiteralPath (Join-Path $output '阅读样本.odt') -Destination (Join-Path $projectRoot 'tests\fixtures\开放文档.odt')
    Write-Output '已通过 LibreOffice writer8 导出真实 ODT 样本'
} finally {
    $resolvedWork=[IO.Path]::GetFullPath($work)
    $resolvedScratch=[IO.Path]::GetFullPath($scratchRoot)+'\'
    if(!$resolvedWork.StartsWith($resolvedScratch,[StringComparison]::OrdinalIgnoreCase)){throw '临时目录超出项目范围'}
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force
}
