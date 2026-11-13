param([string]$CompilerPath,[switch]$SkipBuild,[switch]$SkipChecks)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$env:TEMP=Join-Path $projectRoot 'build';$env:TMP=$env:TEMP
if(!$SkipBuild){& (Join-Path $projectRoot 'build.ps1') -SkipChecks:$SkipChecks}
if(!$CompilerPath){$CompilerPath=Join-Path $projectRoot 'tools\InnoSetup\ISCC.exe'}
if(!(Test-Path -LiteralPath $CompilerPath)){throw 'Inno Setup compiler missing. Run installer\get-build-tool.ps1 or pass -CompilerPath.'}
$releaseRoot=Join-Path $projectRoot 'releases';New-Item -ItemType Directory -Path $releaseRoot -Force|Out-Null
& $CompilerPath /Qp (Join-Path $PSScriptRoot 'CodexVoice.iss')
if($LASTEXITCODE -ne 0){throw 'Setup compiler failed'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$version=[regex]::Match((Get-Content -LiteralPath (Join-Path $projectRoot 'source\Platform.cs') -Raw),'const string Version="([0-9.]+)"').Groups[1].Value
if(!$version){throw 'Product version missing'}
if((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'CodexVoice.iss') -Raw) -notmatch ('#define ProductVersion "'+[regex]::Escape($version)+'"')){throw 'Installer/product version mismatch'}
function Write-SourceZip([string]$zipPath,[string[]]$filePaths){
  $stream=[IO.File]::Open($zipPath,[IO.FileMode]::Create)
  $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
  try{foreach($path in $filePaths){$relative=[IO.Path]::GetRelativePath($projectRoot,$path).Replace('\','/');[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$path,$relative,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}
  finally{$zip.Dispose();$stream.Dispose()}
}
$publicFiles=@('CODEX.exe','Start-CODEX.vbs','QUICKSTART.txt','README.md','CHANGELOG.md','LICENSE.txt','THIRD-PARTY-NOTICES.txt','assets\codex-voice.ico','assets\orb-states.png','docs\README.bn.md','docs\GITHUB-PUBLISH.md','installer\README.md')
$portable=$publicFiles|ForEach-Object {Join-Path $projectRoot $_}
Write-SourceZip (Join-Path $releaseRoot "CODEX-Voice-Control-$version-Windows-x64-Portable.zip") $portable
$source=@($publicFiles+@('repo-metadata.json','build.ps1','.gitignore','.github\workflows\windows-build.yml'))|ForEach-Object{Join-Path $projectRoot $_}
$source+=@(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'source') -File -Filter '*.cs'|ForEach-Object{$_.FullName})
$source+=@(Get-ChildItem -LiteralPath $PSScriptRoot -File|Where-Object{$_.Extension -in @('.ps1','.iss','.md','.json')}|ForEach-Object{$_.FullName})
$source=@($source|Select-Object -Unique)
Write-SourceZip (Join-Path $releaseRoot "CODEX-Voice-Control-$version-Source.zip") $source
$approved=@(Get-ChildItem -LiteralPath $releaseRoot -File|Where-Object {$_.Name -match ('^CODEX-Voice-Control-'+[regex]::Escape($version)+'-.*\.(exe|zip)$')})
$approved|ForEach-Object{"$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"}|Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding utf8
$source|ForEach-Object{[ordered]@{path=[IO.Path]::GetRelativePath($projectRoot,$_).Replace('\','/');bytes=(Get-Item -LiteralPath $_).Length;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()}}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $releaseRoot 'SOURCE-MANIFEST.json') -Encoding utf8
@"
CODEX Voice Control $version - Windows x64

Normal installation for you or friends:
CODEX-Voice-Control-$version-Windows-x64-Setup.exe

GitHub repository files:
CODEX-Voice-Control-$version-Source.zip
Upload its extracted contents; attach Setup, Portable and SHA256SUMS.txt to
a Release later. Personal settings, logs and build tools are excluded.

Without installation:
CODEX-Voice-Control-$version-Windows-x64-Portable.zip
Extract everything to a writable folder and open CODEX.exe.

Users need their own installed/signed-in compatible OpenAI Codex desktop app.
Windows English speech recognition handles only the small command vocabulary;
full message transcription uses Codex's native dictation.

Hello/Okay/Hey Codex starts recording. Pause at least TWO seconds, then say
Stop, Send or Run alone. Direct Send can finish recording and submit.
The final spoken command word may remain in the native draft.

Right-click > Stop completely closes the mic/process and disables startup.
Double-click the Desktop icon to restart. Download/use is free with the custom
copyright-credit license; no license key, password or automatic fee is needed.

If Codex has fully quit, the wake command starts it on a separate Windows desktop.
Wait for Recording before speaking. Open a specific chat normally to dictate
into that chat. Sign in and allow the microphone before first use. Setup is unsigned.
See README.md, docs/README.bn.md and docs/GITHUB-PUBLISH.md in the Source ZIP.
"@|Set-Content -LiteralPath (Join-Path $releaseRoot 'START-HERE.txt') -Encoding utf8
$approved|Select-Object Name,Length
