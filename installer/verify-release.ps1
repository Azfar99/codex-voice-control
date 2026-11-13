param([switch]$SkipInstallTest)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$releaseRoot=Join-Path $projectRoot 'releases'
$version=[regex]::Match((Get-Content -LiteralPath (Join-Path $projectRoot 'source\Platform.cs') -Raw),'const string Version="([0-9.]+)"').Groups[1].Value
$results=[Collections.Generic.List[string]]::new()
function Assert-Release([bool]$ok,[string]$message){if(!$ok){throw $message};$results.Add("PASS $message")}
Assert-Release ((Get-Content -LiteralPath (Join-Path $releaseRoot 'START-HERE.txt') -Raw).Contains("CODEX Voice Control $version")) 'delivery guide points to the current release'
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach($line in Get-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt')){
  if(!$line.Trim()){continue};$parts=$line -split '\s+',2
  Assert-Release ((Get-FileHash -LiteralPath (Join-Path $releaseRoot $parts[1]) -Algorithm SHA256).Hash -eq $parts[0]) ("release SHA-256: "+$parts[1])
}
$sourceZip=Join-Path $releaseRoot "CODEX-Voice-Control-$version-Source.zip"
$zip=[IO.Compression.ZipFile]::OpenRead($sourceZip)
try{
  $names=@($zip.Entries|ForEach-Object{$_.FullName})
  Assert-Release (($names|Select-Object -Unique).Count -eq $names.Count) 'source ZIP has no duplicate entries'
  Assert-Release (@($names|Where-Object{$_ -match '(^|/)(data|build|tools|\.git)/|settings\.xml|\.log$|\.(wav|mp3|mp4)$'}).Count -eq 0) 'source ZIP excludes user data, logs, recordings and build tools'
  $manifest=Get-Content -LiteralPath (Join-Path $releaseRoot 'SOURCE-MANIFEST.json') -Raw|ConvertFrom-Json
  Assert-Release ($manifest.Count -eq $names.Count) 'source manifest covers exactly the packaged files'
  foreach($item in $manifest){
    $entry=$zip.GetEntry($item.path);Assert-Release ($null -ne $entry -and $entry.Length -eq $item.bytes) ("source length: "+$item.path)
    $stream=$entry.Open();$algorithm=[Security.Cryptography.SHA256]::Create()
    try{$digest=[BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}finally{$algorithm.Dispose();$stream.Dispose()}
    Assert-Release ($digest -eq $item.sha256) ("source hash: "+$item.path)
    if($item.path -match '\.(cs|md|txt|json|yml|ps1|iss|vbs)$'){
      $reader=[IO.StreamReader]::new($entry.Open());try{$content=$reader.ReadToEnd()}finally{$reader.Dispose()}
      Assert-Release ($content -notmatch 'sk-(?:proj-)?[A-Za-z0-9_-]{24,}|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{35,}|C:\\Users\\') ("no standard credential or personal-profile path: "+$item.path)
      if($item.path -match '\.md$'){
        foreach($link in [regex]::Matches($content,'!?\[[^\]]*\]\(([^\s)]+)\)')){
          $target=$link.Groups[1].Value;if($target -match '^(https?://|#)'){continue}
          $relative=[Uri]::UnescapeDataString(([Uri]::new([Uri]::new('https://archive.invalid/'+$item.path),$target)).AbsolutePath.TrimStart('/'))
          Assert-Release ($names -contains $relative) ("relative documentation link: "+$item.path+' -> '+$relative)
        }
      }
    }
  }
}finally{$zip.Dispose()}
$metadata=Get-Content -LiteralPath (Join-Path $projectRoot 'repo-metadata.json') -Raw|ConvertFrom-Json
Assert-Release ($metadata.topics.Count -le 20 -and @($metadata.topics|Where-Object{$_ -notmatch '^[a-z0-9][a-z0-9-]{0,49}$'}).Count -eq 0) 'proposed GitHub topics use valid names'
$bridge=Get-Content -LiteralPath (Join-Path $projectRoot 'source\CodexBridge.cs') -Raw
Assert-Release ($bridge -notmatch 'SetFocus\(|SendInput\(|TypeUnicode\(|CleanControl\(') 'native bridge contains no editor-focus or keyboard-retyping path'
$backgroundHost=Get-Content -LiteralPath (Join-Path $projectRoot 'source\BackgroundCodex.cs') -Raw
Assert-Release ($backgroundHost -match 'Desktop = name' -and $backgroundHost -notmatch 'SwitchDesktop\(|ShellExecute\(' -and $bridge -notmatch 'Process\.Start\(') 'the cold launch targets the separate desktop and has no interactive launch fallback'
if(!$SkipInstallTest){
  $testFolder=Join-Path $projectRoot "build\setup-smoke-$version-$PID"
  $resolved=[IO.Path]::GetFullPath($testFolder);$buildRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'build'))+[IO.Path]::DirectorySeparatorChar
  if(!$resolved.StartsWith($buildRoot,[StringComparison]::OrdinalIgnoreCase)-or(Test-Path -LiteralPath $resolved)){throw 'Installer test needs a fresh directory inside this project build folder'}
  $registryKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
  $normalUninstall='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{70E77688-01C3-4200-9056-CE0C442EF83F}_is1'
  $testUninstall='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{70E77688-01C3-4200-9056-CE0C442EF83F}-QA_is1'
  $normalBefore=Get-ItemProperty -LiteralPath $normalUninstall -ErrorAction SilentlyContinue|ConvertTo-Json -Compress
  $startupBefore=(Get-ItemProperty -LiteralPath $registryKey -ErrorAction SilentlyContinue).CodexVoiceCompanion
  $primary=@(Get-Process CODEX -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq (Join-Path $projectRoot 'CODEX.exe')})
  $setup=Join-Path $releaseRoot "CODEX-Voice-Control-$version-Windows-x64-Setup.exe"
  $p=Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TESTMODE=1','/TASKS=""',('/DIR="'+$testFolder+'"')) -WindowStyle Hidden -Wait -PassThru
  Assert-Release ($p.ExitCode -eq 0) 'isolated Windows Setup exits successfully'
  Assert-Release (Test-Path -LiteralPath $testUninstall) 'QA installer uses a separate Windows uninstall registration'
  Assert-Release ((Get-ItemProperty -LiteralPath $normalUninstall -ErrorAction SilentlyContinue|ConvertTo-Json -Compress) -eq $normalBefore) 'QA install preserves normal Windows uninstall registration'
  $installed=Join-Path $testFolder 'CODEX.exe'
  Assert-Release ((Get-FileHash -LiteralPath $installed).Hash -eq (Get-FileHash -LiteralPath (Join-Path $projectRoot 'CODEX.exe')).Hash) 'installed executable matches release payload'
  $p=Start-Process -FilePath $installed -ArgumentList '--probe-prerequisites' -WindowStyle Hidden -Wait -PassThru
  Assert-Release ($p.ExitCode -eq 0) 'isolated installed copy detects this PC Codex and speech prerequisites'
  $p=Start-Process -FilePath (Join-Path $testFolder 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru
  Assert-Release ($p.ExitCode -eq 0 -and !(Test-Path -LiteralPath $installed)) 'isolated uninstaller removes installed executable'
  Assert-Release (!(Test-Path -LiteralPath (Join-Path $testFolder 'installer-test.flag'))) 'isolated uninstaller removes test marker'
  Assert-Release (!(Test-Path -LiteralPath $testUninstall) -and (Get-ItemProperty -LiteralPath $normalUninstall -ErrorAction SilentlyContinue|ConvertTo-Json -Compress) -eq $normalBefore) 'QA uninstall removes only its own Windows registration'
  Assert-Release ((Get-ItemProperty -LiteralPath $registryKey -ErrorAction SilentlyContinue).CodexVoiceCompanion -eq $startupBefore) 'isolated install/uninstall preserves primary startup entry'
  foreach($process in $primary){Assert-Release ($null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) 'isolated uninstall keeps primary companion running'}
}
$report=Join-Path $projectRoot "build\release-checks-$version.txt"
$results|Set-Content -LiteralPath $report -Encoding utf8
"Passed $($results.Count) release checks. Report: $report"
