#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
VENDOR_DIR="$SCRIPT_DIR/.vendor/typeless-export-runtime"
ELECTRON_STUB_DIR="$VENDOR_DIR/node_modules/electron"

mkdir -p "$VENDOR_DIR"

# Install electron-store if missing (shared with export-dictionary)
if [ ! -d "$VENDOR_DIR/node_modules/electron-store" ]; then
  echo "[switch-account] Installing electron-store…"
  npm install --prefix "$VENDOR_DIR" --silent --no-fund --no-audit electron-store@10.0.1 >/dev/null
fi

# Install puppeteer (includes bundled Chromium) if missing
if [ ! -d "$VENDOR_DIR/node_modules/puppeteer" ]; then
  echo "[switch-account] Installing puppeteer (this may take a moment)…"
  npm install --prefix "$VENDOR_DIR" --silent --no-fund --no-audit puppeteer@24.2.1 >/dev/null
fi

# Electron stub (shared with export-dictionary)
mkdir -p "$ELECTRON_STUB_DIR"
cat > "$ELECTRON_STUB_DIR/package.json" <<'EOF'
{
  "name": "electron",
  "version": "0.0.0-skill-stub",
  "type": "module",
  "exports": "./index.js"
}
EOF
cat > "$ELECTRON_STUB_DIR/index.js" <<'EOF'
export const app = undefined;
export const ipcMain = undefined;
export const shell = { openPath: async () => '' };
export default { app, ipcMain, shell };
EOF

export TYPELESS_VENDOR_NODE_MODULES="$VENDOR_DIR/node_modules"
export NODE_PATH="$VENDOR_DIR/node_modules"

node "$SCRIPT_DIR/switch-account.mjs" "$@"
