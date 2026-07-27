# Typeless Switch technical reference

## Scope

This document explains how the bundled scripts locate Typeless data, read the active session, export and import dictionary entries, and switch the local account on Windows and macOS.

All repository paths below are relative to the repository root. Operating-system data paths use environment variables instead of a developer-specific username, drive, or home directory.

## Repository path model

Generated files:

- references/typeless-dictionary-export.json
- references/typeless-dictionary-export.txt
- references/typeless-dictionary-export.csv
- accounts.json
- scripts/.vendor/

Only the first three are dictionary exports. accounts.json stores local account summaries, and scripts/.vendor contains runtime dependencies. All of these generated or private paths are excluded by .gitignore.

The wrappers calculate their own directory before invoking Node.js, so dependency and output locations do not depend on where the repository was cloned.

## Operating-system data model

Typeless data is outside the repository and belongs to the current operating-system user.

### Windows

The scripts derive locations from:

- %APPDATA% for the encrypted Typeless user store.
- %LOCALAPPDATA% and %ProgramFiles% for common application locations.
- The operating-system temporary directory for backups and the optional verification-code signal file.

Observed user-store layout:

~~~text
%APPDATA%\Typeless.exe
~~~

### macOS

The scripts derive locations from $HOME and the operating-system temporary directory.

Observed user-store layout:

~~~text
$HOME/Library/Application Support/Typeless
~~~

Common application locations are the system Applications directory and the current user's Applications directory. TYPELESS_APP_PATH can override application discovery on either platform.

These system paths cannot be repository-relative, but they remain user-independent because they are resolved from the active environment.

## Export pipeline

### 1. Start from the source account

The source account must be active in the Typeless desktop app for the same operating-system user who runs the scripts. Allow synchronization to finish, then quit Typeless completely before reading the local store.

### 2. Start the wrapper

Windows:

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\export-dictionary.ps1
~~~

macOS:

~~~bash
bash ./scripts/export-dictionary.sh
~~~

The wrapper:

1. Creates scripts/.vendor when needed.
2. Installs electron-store locally on first use.
3. Supplies the runtime module path to the Node.js exporter.
4. Writes all export formats under references.

### 3. Read the local encrypted session

scripts/read-user-session.mjs reads user-data.json from the current platform's Typeless data directory.

Typeless and electron-store releases have used more than one encryption layout. The reader tries compatible combinations of:

- Platform and architecture identifiers.
- Typeless application-name variants.
- PBKDF2-derived keys.
- Supported conf/electron-store ciphertext layouts.

A valid result must contain an access token. Tokens are used in memory for authenticated API requests and must never be printed or added to export artifacts.

### 4. Request dictionary data

The exporter calls:

~~~text
GET https://api.typeless.com/user/dictionary/list?size=10000
~~~

The bearer token comes from the local encrypted session. A successful response provides the word collection used for all output formats.

### 5. Write export artifacts

- JSON is the canonical structured backup and import source.
- TXT contains one term per line for quick inspection.
- CSV is intended for spreadsheet review.

The files remain inside references unless the user explicitly selects another output directory when invoking the Node.js implementation directly.

## Import pipeline

Start the platform wrapper without --input to use the canonical relative export automatically.

Windows:

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\import-dictionary.ps1
~~~

macOS:

~~~bash
bash ./scripts/import-dictionary.sh
~~~

The importer first lists existing target-account terms and removes duplicates from the pending set.

### bulk mode

bulk is the default.

~~~text
POST /user/dictionary/bulk-import
~~~

- Terms are grouped into chunks of up to 200.
- Chunks are submitted in parallel.
- This mode is fastest but migrates term text only.

### full mode

~~~text
POST /user/dictionary/add
~~~

- Requests run through a bounded concurrency pool.
- The default concurrency is 12.
- Language, category, and replacement metadata are preserved.

### dry-run mode

--dry-run performs file parsing, session validation, existing-term lookup, and duplicate calculation without adding entries.

## Account-switch pipeline

Run the wrapper with a target email:

Windows:

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\switch-account.ps1 --email "user@example.com"
~~~

macOS:

~~~bash
bash ./scripts/switch-account.sh --email "user@example.com"
~~~

The switcher:

1. Creates a timestamped backup under the operating-system temporary directory.
2. Removes the encrypted local login file.
3. Clears selected login, quota, request, Electron, and Chromium state.
4. Refreshes the local device identifier.
5. Opens a headless browser.
6. Selects the email-login flow in English or Chinese.
7. Submits the target email.
8. Accepts a six-digit code from --code, stdin, or the temporary signal file.
9. Captures the authenticated browser session.
10. Writes a new encrypted local session and updates accounts.json.

The optional signal file is named typeless-code.txt and belongs in the operating-system temporary directory, never in the repository.

## Application and browser discovery

Application discovery checks platform-standard locations derived from environment variables. For a non-standard installation, set TYPELESS_APP_PATH only in the current shell or user environment.

Browser automation checks:

1. PUPPETEER_EXECUTABLE_PATH
2. CHROME_PATH
3. Common Chrome or Edge locations on Windows
4. Common Chrome or Chromium locations on macOS and compatible Unix environments
5. Puppeteer's bundled browser when available

Do not add a personal executable path to repository documentation.

## Reset helpers

The reset helpers are destructive to the local Typeless session and must only run with user authorization.

Windows:

~~~powershell
powershell -ExecutionPolicy Bypass -File .\scripts\reset-device-windows.ps1
~~~

macOS:

~~~bash
bash ./scripts/reset-device-macos.sh
~~~

The macOS helper creates a backup before cleanup. The Windows switcher also creates a temporary backup; the standalone Windows reset helper should be used only when the user understands its local-state effects.

Local cleanup does not modify or bypass server-side account, subscription, quota, websocket, or device-slot rules.

## Validation checklist

After export:

- Confirm the account email is expected.
- Confirm the reported total is plausible.
- Spot-check known terms without publishing private vocabulary.
- Confirm the JSON export exists.

After import:

- Review imported, skipped, and failed counts.
- Export the target dictionary again.
- Compare counts and selected terms with the source export.

## Troubleshooting

### Missing user-data.json

Log in through the Typeless desktop app, wait for synchronization, quit the app, and retry.

### Session decryption failed

A Typeless or electron-store update may have changed the storage layout. Preserve the error message, but never share the encrypted store publicly without reviewing its sensitivity.

### HTTP 401

The local token is expired. Reauthenticate in the desktop app and export again.

### Dictionary endpoint failure

Check network connectivity, token freshness, and the response status. Typeless may have changed its API contract.

### Login selector failure

Typeless may have changed its localized page markup. Capture the visible button text and update selectors without hardcoding a single language.

### Import errors

Run --dry-run first. Use full mode when per-term error detail is needed, and reduce --concurrency if the service begins rate limiting.
