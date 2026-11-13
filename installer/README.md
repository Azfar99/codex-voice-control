# Windows packaging

End users run `CODEX-Voice-Control-0.4.2-Windows-x64-Setup.exe`; no build tool,
license key or Python is needed. They need their own compatible OpenAI Codex
desktop app, account and Windows English speech recognizer. Setup does not
install/sign in to OpenAI or copy the author's account.

Build from PowerShell 7 on Windows x64 with .NET Framework 4.7.2+:

```powershell
.\installer\get-build-tool.ps1
.\installer\package.ps1
.\installer\verify-release.ps1
```

The verified Inno Setup compiler is installed under the project's `tools/`
folder, is used only to build packages, and is not an end-user dependency.
`-CompilerPath` can select an existing compiler. Close the running voice control
before rebuilding CODEX.exe. `CODEX.exe --shutdown` requests a graceful exit.
CI uses `-SkipChecks` because Windows runners may not have a speech recognizer;
local release validation separately exercises the actual command/audio engine.

Deliverables are Setup EXE, portable ZIP, source ZIP, SHA256SUMS.txt and a source
file manifest under `releases/`. ZIPs use an explicit allowlist. Personal
settings, logs, microphone recordings, installed compiler and scratch builds
are excluded.

Setup is per-user, creates normal desktop/Start Menu shortcuts, offers login
startup, and includes a Windows uninstaller. F: is the default if present;
otherwise other users get a normal per-user LocalAppData install path. This
does not move this machine's project off F:. User settings in `data/` survive
updates/uninstall. Uninstall removes only installed product files and matching
startup registration; it does not delete Codex or its data.

Isolated packaging test (never use TESTMODE for ordinary installs):

```powershell
.\releases\CODEX-Voice-Control-0.4.2-Windows-x64-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TESTMODE=1 /TASKS="" /DIR=F:\CodexVoiceAssistant\build\setup-smoke-0.4.2
```

TESTMODE suppresses desktop/startup/account discovery integration and app
launch, so a payload installation can be checked without changing the running
primary instance. It writes an installer-test.flag that also suppresses stopping
the primary instance during test uninstall. A separate QA AppId also preserves
the normal installation's Windows uninstall registration. Installed payload files are still
real. Test results are not a substitute for a clean second-PC microphone test.

For GitHub, upload the source ZIP contents as repository files; attach Setup,
portable ZIP and checksums as Release assets. `.gitignore` excludes user state
and build tools. The manual Windows build workflow uploads artifacts and does
not automatically publish a release. The custom free-use license is not MIT or
an OSI open-source license: paid redistribution requires a separate agreement.

Built installers are unsigned until the owner supplies a code-signing
certificate. No certificate or credential is bundled in source.

See [publishing and discoverability](../docs/GITHUB-PUBLISH.md). Apply the
proposed description/topics to real repository settings when publishing;
uploading metadata JSON alone does not change GitHub settings.
