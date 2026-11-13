# Publish and help people find the project

Publishing is a separate step. This folder does not create a repository, upload code or change GitHub settings automatically.

## Prepare the repository

1. Use the public [codex-voice-control repository](https://github.com/Azfar99/codex-voice-control). Keep its description and topics in sync with `repo-metadata.json` through GitHub's About settings.
2. Upload the **contents** of the Source ZIP as repository files, keeping `README.md` at the root. Preserve `LICENSE.txt` and third-party notices. Do not upload `data/`, `build/`, `tools/` or personal settings/logs. `.gitignore` covers these for Git-based publishing.
3. Check the README links and the Releases download link.
4. Create a Release for the version in the changelog. Attach Setup, Portable, Source, `SHA256SUMS.txt` and the source manifest. Use the matching changelog entry in the release notes.
5. Verify a downloaded package's SHA-256 against the published checksum, and test installation on a second Windows PC with its own compatible Codex app/account.

## Discoverability already prepared

The README describes the actual product in plain English, with Bengali instructions, searchable feature names, requirements, a real design image and direct usage examples. The proposed topics cover Windows voice commands, Codex native dictation, Bengali/Bangla and accessibility without unrelated keyword stuffing. Keep the description, screenshots and release notes accurate as versions change.

GitHub explains [repository topics](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/classifying-your-repository-with-topics) and [README visibility](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-readmes). Topics and useful documentation can help discovery; neither guarantees search rankings, recommendations or virality. This desktop project has no website requiring an HTML SEO tag bundle.

## Updates

After a major update, rebuild/test packages, update version/changelog, review the source ZIP and publish a new Release when the owner requests it. The bundled manual Windows workflow builds artifacts; it does not publish code or releases automatically. No reminder automation, GitHub account credentials or push authorization is stored in the source package.
