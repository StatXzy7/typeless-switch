#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
SKILL_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd)"
VENDOR_DIR="$SCRIPT_DIR/.vendor/typeless-export-runtime"
OUTPUT_DIR="$SKILL_DIR/references"
ELECTRON_STUB_DIR="$VENDOR_DIR/node_modules/electron"

mkdir -p "$VENDOR_DIR" "$OUTPUT_DIR"

if [ ! -d "$VENDOR_DIR/node_modules/electron-store" ]; then
  echo "[typeless-dictionary] Installing local runtime dependency: electron-store"
  npm install --prefix "$VENDOR_DIR" --silent --no-fund --no-audit electron-store@10.0.1 >/dev/null
fi

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

node "$SCRIPT_DIR/export-dictionary.mjs" --output-dir "$OUTPUT_DIR"
