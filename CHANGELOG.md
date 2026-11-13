# Changelog

## 0.4.1 — 2026-11-13

- Make the background Codex window transparent before invoking native dictation controls and restore its original opacity after the operation. This addresses the flash reported with the previous window-moving method.
- Keep native dictation available with a Codex chat open behind other apps or minimized. A wake command now shows an instruction to open Codex manually when the app is fully closed.
- Recheck foreground restoration, draft retention, voice commands and installer isolation. Publish Windows packages with checksums, Bengali and English documentation, and GitHub topics.

## 0.4.0 — 2026-11-13

- Add 4 px to existing orb sizes once; default 76 px. Slightly thicker shared-color border.
- Green idle/quiet and yellow speaking palette; stronger voice-driven outward waves and particle vibration.
- Cache verified accessibility elements for preview; read current text, check process/chat/mode and expire after one second. Refresh controls before every action and during transitions.
- Preview polling: 1000 → 250 ms only with its panel open. Faster command endpoint and native transition checks; reuse wave buffers and circle tables; skip covered drawing and slow hidden animation.
- Control background/minimized Codex outside the virtual desktop, restore the prior foreground app if the host activates itself, then restore its placement. Remove automatic editor-focus and draft-retyping paths.
- Show the small starting indicator immediately when a wake command is accepted.
- Preserve two seconds of silence before standalone Stop/Send/Run, continued-speech rejection, direct Send, Desktop restart and complete Stop.
- Add English/Bengali documentation, design image, proposed GitHub metadata and a publishing checklist.
- Refresh Setup, portable/source packages and integrity manifests. Existing user-selected custom free-use license retained.

Known tradeoff: a final spoken Stop/Send word can remain in the native draft. Automatic removal would require editor focus on the tested host. Fully closed-host launch, a second physical PC and future host versions are not verified.

## 0.3.0

Native Codex dictation controls, read-only draft preview, small Windows command grammar, silence gating, animated orb, tray and Desktop controls, package discovery, complete Stop/manual restart, Windows installer and custom copyright license.
