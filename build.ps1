param([switch]$SkipChecks)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot;$env:TEMP=Join-Path $root 'build';$env:TMP=$env:TEMP
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
$c=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe';$w=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF';$speech=Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Speech\v4.0_4.0.0.0__31bf3856ad364e35\System.Speech.dll'
$icon=@();if(Test-Path -LiteralPath "$root\assets\codex-voice.ico"){$icon=@("/win32icon:$root\assets\codex-voice.ico")}
& $c /nologo /target:winexe /platform:x64 /optimize+ "/out:$root\CODEX.exe" @icon /r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$speech" "/r:$w\UIAutomationClient.dll" "/r:$w\UIAutomationTypes.dll" "/r:$w\WindowsBase.dll" (Get-ChildItem -LiteralPath "$root\source" -Filter '*.cs' -File | ForEach-Object { $_.FullName })
if($LASTEXITCODE -ne 0){throw 'Compile failed'}
if(!$SkipChecks){$p=Start-Process -FilePath "$root\CODEX.exe" -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru;if($p.ExitCode -ne 0){throw 'Checks failed'}; "Passed $(@(Select-String -LiteralPath "$root\build\tests.txt" -Pattern '^PASS ').Count) basic checks."}
