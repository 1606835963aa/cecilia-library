param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Files)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$executable = Join-Path $projectRoot 'dist\LightReader\LightReader.exe'
if (!(Test-Path -LiteralPath $executable)) { $executable = Join-Path $projectRoot 'src\LightReader.App\bin\Release\net10.0-windows10.0.19041.0\LightReader.exe' }
if (!(Test-Path -LiteralPath $executable)) { throw '请先运行 scripts\build.ps1 -Publish' }
$registrationScript=Join-Path $projectRoot 'scripts\register-file-associations.ps1'
if($executable -eq (Join-Path $projectRoot 'dist\LightReader\LightReader.exe') -and (Test-Path -LiteralPath $registrationScript)){
 $registeredVersion=[Microsoft.Win32.Registry]::GetValue('HKEY_CURRENT_USER\Software\CeciliaLibrary','RegistrationVersion','')
 $registeredExecutable=[Microsoft.Win32.Registry]::GetValue('HKEY_CURRENT_USER\Software\CeciliaLibrary','RegisteredExecutable','')
 $registrationDisabled=[Microsoft.Win32.Registry]::GetValue('HKEY_CURRENT_USER\Software\CeciliaLibrary','RegistrationDisabled','')
 if($registrationDisabled -ne '1' -and ($registeredVersion -ne '2' -or $registeredExecutable -ne $executable)){& $registrationScript | Out-Null}
}
$sdk = Join-Path $projectRoot '.tools\dotnet'
if(Test-Path -LiteralPath (Join-Path $sdk 'dotnet.exe')){$env:DOTNET_ROOT=$sdk}
$dataDirectory=Join-Path $projectRoot '.local-data'
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
$componentRoot=Join-Path $projectRoot 'components'
$componentLink=Join-Path $dataDirectory 'components'
if((Test-Path -LiteralPath $componentRoot) -and !(Test-Path -LiteralPath $componentLink)){New-Item -ItemType Junction -Path $componentLink -Target $componentRoot | Out-Null}
$launchArguments=@('--data-dir',('"'+$dataDirectory+'"'))
foreach($file in $Files){$launchArguments+=('"'+[IO.Path]::GetFullPath($file)+'"')}
Start-Process -FilePath $executable -ArgumentList $launchArguments -WorkingDirectory $projectRoot -WindowStyle Normal
