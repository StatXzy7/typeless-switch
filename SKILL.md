---
name: typeless-switch
description: Export, switch, import, and verify Typeless account dictionaries on Windows or macOS. Use when a user wants to back up a Typeless dictionary, change the locally active Typeless account through email verification, or migrate dictionary entries in parallel.
---

# Typeless Switch

## Purpose

Use the bundled scripts to migrate a Typeless dictionary between accounts. Prefer the wrappers in scripts over reimplementing login, encryption, export, or import logic.

The workflow is interactive. The user supplies:

1. A target email address.
2. The six-digit verification code sent by Typeless.

Always reply in the language used by the user.

## Supported environments

- Windows 10/11 with PowerShell.
- macOS with Bash.
- Node.js 18 or later with npm.
- Typeless desktop app installed for the current operating-system user.

Linux is not currently supported because the Typeless desktop data layout is not implemented.

## Working-directory rule

Run commands from the repository root. All repository files must be referenced with relative paths under scripts or references.

Never add a developer-specific drive letter, home directory, username, or checkout location to documentation or commands. System data locations may be described with portable environment variables such as %APPDATA%, %LOCALAPPDATA%, $HOME, and the operating-system temporary directory.

## Canonical outputs

The export wrappers write:

- references/typeless-dictionary-export.json
- references/typeless-dictionary-export.txt
- references/typeless-dictionary-export.csv

The JSON file is the canonical input for later import and verification. These files are ignored by Git because they may contain user data.

## Platform command map

### Windows

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\export-dictionary.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\switch-account.ps1 --email "user@example.com"
powershell -ExecutionPolicy Bypass -File .\scripts\import-dictionary.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\reset-device-windows.ps1
~~~

### macOS

~~~bash
bash ./scripts/export-dictionary.sh
bash ./scripts/switch-account.sh --email "user@example.com"
bash ./scripts/import-dictionary.sh
bash ./scripts/reset-device-macos.sh
~~~

The wrappers install their runtime dependencies below scripts/.vendor and resolve their own location. Do not install or copy dependencies into arbitrary user directories.

## Default migration workflow

### Phase 1: protect the source dictionary

1. Ask the user to open Typeless and confirm that the source account is active.
2. Ask the user to wait for dictionary synchronization.
3. Ask the user to quit Typeless completely, including any tray or menu-bar process.
4. Run the platform export wrapper.
5. Confirm that references/typeless-dictionary-export.json exists.
6. Record the reported source email and word count without exposing tokens.

Do not switch accounts until the export succeeds.

If the source account uses Google or Apple sign-in, the user must perform that login in the Typeless desktop app. The export can then read the resulting local session.

### Phase 2: switch the local account

1. Ask for the target email address.
2. Run the platform switch wrapper with --email.
3. Wait for Typeless to send a six-digit code.
4. Ask the user for the code and provide it through stdin or --code.
5. Confirm that the script reports a successful new local session.

The switcher backs up the current local state to the operating-system temporary directory before changing it. It also records a non-secret account summary in accounts.json, which is ignored by Git.

For non-interactive agent execution, the switcher also watches a file named typeless-code.txt in the operating-system temporary directory. Do not place this signal file inside the repository.

### Phase 3: import in parallel

Run the platform import wrapper without --input. It automatically reads references/typeless-dictionary-export.json.

Default mode:

~~~text
--mode bulk
~~~

- Uses the official bulk-import endpoint.
- Sends chunks of up to 200 terms.
- Submits chunks in parallel.
- Optimized for speed and term-only migration.

Metadata-preserving mode:

~~~text
--mode full --concurrency 12
~~~

- Uses concurrent add requests.
- Preserves language, category, and replacement metadata.
- Allows a configurable concurrency limit.

Preview mode:

~~~text
--dry-run
~~~

- Reads the export and compares existing terms.
- Does not write dictionary entries.

When the user supplies another dictionary file, pass a quoted relative path from the repository root, such as backups/dictionary.json.

### Phase 4: verify

1. Run the export wrapper again against the target account.
2. Compare the reported word count with the source export.
3. Report imported, skipped, and failed counts.
4. Make clear that the source account was not modified remotely.

## Account-switch behavior

The switch script:

1. Backs up local Typeless state to the system temporary directory.
2. Clears the encrypted local login state and selected request/quota fields.
3. Clears Electron and Chromium session data that may survive a normal logout.
4. Refreshes the local device identifier.
5. Uses a headless browser for the email verification flow.
6. Captures the authenticated browser session.
7. Writes the new encrypted session into the current user's Typeless data directory.

This modifies local state. It does not alter or bypass Typeless server-side account, quota, subscription, or device policies.

## Path and installation compatibility

The scripts resolve Typeless data from the current operating-system user:

- Windows uses %APPDATA%, %LOCALAPPDATA%, and %ProgramFiles%.
- macOS uses $HOME and standard application locations.

If Typeless is installed elsewhere, use TYPELESS_APP_PATH for the current process. If browser discovery fails, use PUPPETEER_EXECUTABLE_PATH or CHROME_PATH. Never commit the resulting machine-specific values.

## Email compatibility

Generally supported:

- Gmail
- Outlook or Hotmail
- QQ Mail
- 163 Mail
- Normal custom-domain mailboxes

Known problematic categories:

- Addresses containing plus-tag aliases
- Disposable email providers
- Forwarding services that delay messages or break authentication signatures

The automated login flow supports email verification only.

## Guardrails

- Never display, log, commit, or persist access tokens or refresh tokens outside the encrypted Typeless store.
- Treat exported dictionaries as private user data.
- Do not commit accounts.json, scripts/.vendor, or references/typeless-dictionary-export files.
- Do not switch accounts before a requested source export has succeeded.
- Do not import during export-only work.
- Use --dry-run when the user requests a preview.
- Do not claim that local reset behavior bypasses server-side restrictions.
- Preserve LICENSE and third-party attribution when publishing changes.

## Troubleshooting

### No local login state

Ask the user to log in through the Typeless desktop app, wait for synchronization, quit the app, and retry export.

### Expired token or HTTP 401

Ask the user to reauthenticate in Typeless, quit the app, and retry.

### Login page selector failure

Keep the complete error output. Typeless may have changed the English or Chinese page structure.

### Browser executable not found

Use the bundled Puppeteer browser or configure PUPPETEER_EXECUTABLE_PATH or CHROME_PATH for the current shell.

### Account or websocket limit

Ensure Typeless is fully closed and retry once. If the error remains, report it as likely server-side state rather than repeatedly deleting local data.

### Import failure

Run --dry-run, verify the JSON source, check the target session, and use full mode to identify per-term failures when needed.

## Resources

- README.md — user-facing setup and commands.
- scripts/export-dictionary.ps1 and scripts/export-dictionary.sh — export wrappers.
- scripts/import-dictionary.ps1 and scripts/import-dictionary.sh — import wrappers.
- scripts/switch-account.ps1 and scripts/switch-account.sh — account-switch wrappers.
- scripts/reset-device-windows.ps1 and scripts/reset-device-macos.sh — local reset helpers.
- scripts/read-user-session.mjs — cross-platform session reader.
- references/extract-dictionary.md — technical path, API, and troubleshooting reference.
- 换号使用说明.txt — concise Windows guide.
