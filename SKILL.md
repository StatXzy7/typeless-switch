---
name: typeless-switch
description: Build, verify, or operate the Typeless Switch Windows GUI for safe account switching and parallel dictionary migration.
---

# Typeless Switch repository guide

## Default entry point

For Windows users, prefer the installed WPF GUI. Do not require Node.js, npm, terminal commands, or repository checkout for normal account switching and dictionary migration.

Use the legacy scripts under `scripts` only for compatibility diagnostics or when the user explicitly requests command-line operation.

Reply in the user's language.

## Safety boundary

- Only operate accounts owned or authorized by the user.
- Never print, log, commit, or copy access tokens and refresh tokens.
- Treat exported dictionaries as private user data.
- Never claim that local cleanup bypasses Typeless server-side limits.
- Before changing the local account, stop Typeless and create a temporary backup.
- On login cancellation or failure, restore the backup and restart Typeless.
- Do not run a real account switch when the user only asked for code review, build, export, or diagnosis.

## Portable paths

Repository commands must be run from the repository root and use relative paths such as:

```text
.\src
.\tests
.\scripts
.\installer
```

Describe user data only with portable variables:

```text
%APPDATA%\Typeless.exe
%LOCALAPPDATA%\TypelessSwitch
%TEMP%\typeless-switch-backup-*
```

Never add a developer-specific drive, username, checkout directory, or executable path to source or documentation.

## GUI workflow

### Switch account

1. Validate the target email.
2. Stop Typeless.
3. Back up the current local user directory and device cache.
4. Clear the old local session.
5. Open the modal WebView2 login window.
6. Let the user complete the six-digit email verification.
7. Read the authenticated localStorage token without logging it.
8. Write the Typeless-compatible encrypted session atomically.
9. Restart Typeless.
10. Restore the backup if any step before successful write fails.

### Export

Use the active encrypted session to request the dictionary once, then write JSON, TXT and CSV together to the user-selected folder.

### Import

- Bulk mode: remove duplicates, split terms into chunks of at most 200, and submit chunks in parallel.
- Full mode: remove duplicates by term and language, then submit metadata-preserving add requests through a bounded concurrency pool.
- Always expose progress, failed counts, and cancellation.

## Build and verification

Run from the repository root:

```powershell
dotnet test .\TypelessSwitch.sln --configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

Expected generated outputs:

```text
artifacts\publish\win-x64\
installer\output\TypelessSwitch-*-win-x64-setup.exe
```

Before publishing:

1. Confirm Release tests pass.
2. Launch the published executable and confirm the main window opens.
3. Install silently into a temporary test directory, launch it, close it, and uninstall it.
4. Confirm no Typeless Switch process remains after closing.
5. Confirm generated artifacts and personal data remain ignored by Git.
6. Keep `main` as the only branch when repository policy requires it.

Technical implementation details are in `references\extract-dictionary.md`.
