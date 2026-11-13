$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$toolRoot=Join-Path $projectRoot 'tools';New-Item -ItemType Directory -Path $toolRoot -Force|Out-Null
$compilerInstaller=Join-Path $toolRoot 'innosetup-7.1.1-x64.exe'
$expected='8A5A6711C3766A6E5FB74F280BA0E3551E719CE94538D901FDBD1F8F9920B3F0'
if(!(Test-Path -LiteralPath $compilerInstaller)){Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' -OutFile $compilerInstaller}
if((Get-FileHash -LiteralPath $compilerInstaller -Algorithm SHA256).Hash -ne $expected){throw 'Compiler SHA-256 mismatch'}
if((Get-AuthenticodeSignature -LiteralPath $compilerInstaller).Status -ne 'Valid'){throw 'Compiler Authenticode signature invalid'}
$destination=Join-Path $toolRoot 'InnoSetup'
$process=Start-Process -FilePath $compilerInstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/CURRENTUSER','/NOICONS','/NORESTART',('/DIR="'+$destination+'"')) -WindowStyle Hidden -Wait -PassThru
if($process.ExitCode -ne 0){throw "Compiler setup failed: $($process.ExitCode)"}
Get-Item -LiteralPath (Join-Path $destination 'ISCC.exe')|Select-Object FullName,Length
