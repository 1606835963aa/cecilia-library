param([switch]$Remove,[switch]$OpenSettings)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$applicationName='塞西莉亚的图书馆'
$progId='CeciliaLibrary.Document'
$capabilities='Software\CeciliaLibrary\Capabilities'
$applicationKey='Software\Classes\Applications\LightReader.exe'
$progKey='Software\Classes\'+$progId
$appPathKey='Software\Microsoft\Windows\CurrentVersion\App Paths\LightReader.exe'
$ownerName='CeciliaLibraryRegistration'
$extensions=@((Get-Content -LiteralPath (Join-Path $projectRoot 'data\format-targets.json') -Raw | ConvertFrom-Json).formats | Where-Object verificationStatus -eq 'VERIFIED_SAMPLE' | ForEach-Object extension | Sort-Object -Unique)
$userRegistry=[Microsoft.Win32.Registry]::CurrentUser
function Set-RegistrationString([string]$Path,[string]$Name,[string]$Value){
    $key=$userRegistry.CreateSubKey($Path)
    try{$key.SetValue($Name,$Value,[Microsoft.Win32.RegistryValueKind]::String)}finally{$key.Dispose()}
}
function Assert-OwnedRegistration([string]$Path){
    $key=$userRegistry.OpenSubKey($Path)
    if($key){try{if($key.GetValue($ownerName) -ne '1'){throw ('该登记位置已由其他程序使用：'+$Path)}}finally{$key.Dispose()}}
}
foreach($ownedPath in @($applicationKey,$progKey,$appPathKey,'Software\CeciliaLibrary')){Assert-OwnedRegistration $ownedPath}
if($Remove){
    foreach($extension in $extensions){
        $key=$userRegistry.OpenSubKey(('Software\Classes\'+$extension+'\OpenWithProgids'),$true)
        if($key){try{$key.DeleteValue($progId,$false)}finally{$key.Dispose()}}
    }
    $registered=$userRegistry.OpenSubKey('Software\RegisteredApplications',$true)
    if($registered){try{if($registered.GetValue($applicationName) -eq $capabilities){$registered.DeleteValue($applicationName,$false)}}finally{$registered.Dispose()}}
    foreach($ownedPath in @($applicationKey,$progKey,$appPathKey,'Software\CeciliaLibrary')){$userRegistry.DeleteSubKeyTree($ownedPath,$false)}
}else{
    $executable=Join-Path $projectRoot 'dist\LightReader\LightReader.exe'
    if(!(Test-Path -LiteralPath $executable -PathType Leaf)){throw '请先生成 dist\LightReader 中的发布程序'}
    if($extensions.Count -eq 0){throw '没有已取得样本证据的格式可以登记'}
    $profile=Join-Path $projectRoot '.local-data'
    New-Item -ItemType Directory -Path $profile -Force | Out-Null
    $componentLink=Join-Path $profile 'components'
    if(!(Test-Path -LiteralPath $componentLink) -and (Test-Path -LiteralPath (Join-Path $projectRoot 'components'))){New-Item -ItemType Junction -Path $componentLink -Target (Join-Path $projectRoot 'components') | Out-Null}
    $command='"'+$executable+'" --data-dir "'+$profile+'" "%1"'
    $icon='"'+$executable+'",0'
    foreach($ownedPath in @($applicationKey,$progKey,$appPathKey,'Software\CeciliaLibrary')){Set-RegistrationString $ownedPath $ownerName '1'}
    Set-RegistrationString $applicationKey 'FriendlyAppName' $applicationName
    Set-RegistrationString ($applicationKey+'\DefaultIcon') '' $icon
    Set-RegistrationString ($applicationKey+'\shell\open\command') '' $command
    Set-RegistrationString $progKey '' '塞西莉亚的图书馆文档'
    Set-RegistrationString $progKey 'FriendlyTypeName' '塞西莉亚的图书馆文档'
    Set-RegistrationString ($progKey+'\Application') 'ApplicationName' $applicationName
    Set-RegistrationString ($progKey+'\Application') 'ApplicationIcon' $icon
    Set-RegistrationString ($progKey+'\DefaultIcon') '' $icon
    Set-RegistrationString ($progKey+'\shell\open\command') '' $command
    Set-RegistrationString $appPathKey '' $executable
    Set-RegistrationString $capabilities 'ApplicationName' $applicationName
    Set-RegistrationString $capabilities 'ApplicationDescription' '查看文字、表格、演示、PDF 和图片，提供文本基础编辑。'
    Set-RegistrationString $capabilities 'ApplicationIcon' $icon
    foreach($extension in $extensions){
        Set-RegistrationString ($capabilities+'\FileAssociations') $extension $progId
        Set-RegistrationString ($applicationKey+'\SupportedTypes') $extension ''
        $key=$userRegistry.CreateSubKey('Software\Classes\'+$extension+'\OpenWithProgids')
        try{$key.SetValue($progId,[byte[]]@(),[Microsoft.Win32.RegistryValueKind]::None)}finally{$key.Dispose()}
    }
    Set-RegistrationString 'Software\RegisteredApplications' $applicationName $capabilities
}
if(!('CeciliaLibrary.ShellAssociations' -as [type])){
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace CeciliaLibrary {
    public static class ShellAssociations {
        [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint e, uint f, IntPtr a, IntPtr b);
    }
}
'@
}
[CeciliaLibrary.ShellAssociations]::SHChangeNotify(0x08000000,0,[IntPtr]::Zero,[IntPtr]::Zero)
$settingsUri='ms-settings:defaultapps?registeredAppUser='+[Uri]::EscapeDataString($applicationName)
@{application=$applicationName;registered=!$Remove;extensionCount=$extensions.Count;scope='current-user';settingsUri=$settingsUri} | ConvertTo-Json
if($OpenSettings -and !$Remove){Start-Process -FilePath $settingsUri}
