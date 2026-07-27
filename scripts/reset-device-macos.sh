#!/usr/bin/env bash
#
# Reset Typeless local account/device state on macOS.
#
# Newer Typeless releases keep most reusable state in Electron Store,
# Chromium session storage, and updater/cache directories. The Keychain device
# identifier must be overwritten, not merely deleted: recent Typeless builds can
# recreate the same identifier after deletion.
#
# Usage:
#   bash scripts/reset-device-macos.sh
#
set -euo pipefail

echo "[reset-device] Typeless device identifier reset tool (macOS)"

TYPELESS_DIR="$HOME/Library/Application Support/Typeless"
CACHES_DIR="$HOME/Library/Caches"
BACKUP_DIR="${TMPDIR:-/tmp}/typeless-reset-backup-$(date +%Y%m%d-%H%M%S)"

find_typeless_app() {
  if [ -n "${TYPELESS_APP_PATH:-}" ] && [ -d "$TYPELESS_APP_PATH" ]; then
    printf '%s\n' "$TYPELESS_APP_PATH"
    return 0
  fi

  local candidate
  for candidate in \
    "/Applications/Typeless.app" \
    "$HOME/Applications/Typeless.app"
  do
    if [ -d "$candidate" ]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}

TYPELESS_APP_PATH_FOUND="$(find_typeless_app || true)"

# 1. Kill Typeless if running
if pgrep -f "Typeless.app" > /dev/null 2>&1; then
  echo "[reset-device] Stopping Typeless..."
  osascript -e 'quit app "Typeless"' 2>/dev/null || true
  for i in $(seq 1 10); do
    pgrep -f "Typeless.app" > /dev/null 2>&1 || break
    sleep 0.5
  done
  echo "[reset-device] Typeless stopped"
else
  echo "[reset-device] Typeless is not running"
fi

# 2. Back up the local state before changing anything
mkdir -p "$BACKUP_DIR"
if [ -d "$TYPELESS_DIR" ]; then
  ditto -c -k --sequesterRsrc --keepParent "$TYPELESS_DIR" "$BACKUP_DIR/Typeless-Application-Support.zip"
  echo "[reset-device] Backed up Application Support to $BACKUP_DIR/Typeless-Application-Support.zip"
fi
for cache_name in now.typeless.desktop typeless-updater now.typeless.desktop.ShipIt; do
  if [ -e "$CACHES_DIR/$cache_name" ]; then
    ditto -c -k --sequesterRsrc --keepParent "$CACHES_DIR/$cache_name" "$BACKUP_DIR/$cache_name.zip" || true
    echo "[reset-device] Backed up cache $cache_name"
  fi
done

# 3. Overwrite the Keychain device identifier.
OLD_DEVICE_UUID="$(security find-generic-password \
  -s "now.typeless.desktop.deviceIdentifier" \
  -a "now.typeless.desktop.security.auth_key" \
  -w 2>/dev/null || true)"
NEW_DEVICE_UUID="$(uuidgen)"
printf 'old_uuid=%s\nnew_uuid=%s\n' "$OLD_DEVICE_UUID" "$NEW_DEVICE_UUID" > "$BACKUP_DIR/keychain-device-uuid.txt"

if security add-generic-password -U \
  -s "now.typeless.desktop.deviceIdentifier" \
  -a "now.typeless.desktop.security.auth_key" \
  -w "$NEW_DEVICE_UUID" >/dev/null; then
  echo "[reset-device] Keychain device identifier overwritten: $NEW_DEVICE_UUID"
else
  echo "[reset-device] Could not overwrite Keychain device identifier"
fi

# 4. Delete encrypted login state
if [ -f "$TYPELESS_DIR/user-data.json" ]; then
  rm -f "$TYPELESS_DIR/user-data.json"
  echo "[reset-device] Removed user-data.json"
else
  echo "[reset-device] user-data.json not found (already clean)"
fi

# 5. Clear login/quota/request state from app-storage.json (keep settings)
if [ -f "$TYPELESS_DIR/app-storage.json" ]; then
  TYPELESS_STORAGE_PATH="$TYPELESS_DIR/app-storage.json" node - <<'NODE'
    const fs = require('fs');
    try {
      const storagePath = process.env.TYPELESS_STORAGE_PATH;
      const data = JSON.parse(fs.readFileSync(storagePath, 'utf8'));
      delete data.userData;
      delete data.quotaUsage;
      delete data.currentRoute;
      delete data.TYPELESS_418_SEND_ERROR_COUNT;
      delete data.TYPELESS_TIME_DIFF;
      fs.writeFileSync(storagePath, JSON.stringify(data, null, '\t'));
      console.log('[reset-device] Cleared login/quota request state from app-storage.json');
    } catch(e) {
      console.log('[reset-device] Could not clean app-storage.json: ' + e.message);
    }
NODE
else
  echo "[reset-device] app-storage.json not found"
fi

# 6. Clear Electron/Chromium session state that can survive a simple logout
remove_path() {
  local target="$1"
  local label="$2"
  if [ -e "$target" ]; then
    rm -rf "$target"
    echo "[reset-device] Removed $label"
  fi
}

remove_path "$TYPELESS_DIR/.updaterId" ".updaterId"
remove_path "$TYPELESS_DIR/Cookies" "Cookies"
remove_path "$TYPELESS_DIR/Cookies-journal" "Cookies-journal"
remove_path "$TYPELESS_DIR/Local Storage" "Local Storage"
remove_path "$TYPELESS_DIR/Session Storage" "Session Storage"
remove_path "$TYPELESS_DIR/SharedStorage" "SharedStorage"
remove_path "$TYPELESS_DIR/SharedStorage-wal" "SharedStorage-wal"
remove_path "$TYPELESS_DIR/SharedStorage-shm" "SharedStorage-shm"
remove_path "$TYPELESS_DIR/Trust Tokens" "Trust Tokens"
remove_path "$TYPELESS_DIR/Trust Tokens-journal" "Trust Tokens-journal"
remove_path "$TYPELESS_DIR/Network Persistent State" "Network Persistent State"
remove_path "$TYPELESS_DIR/TransportSecurity" "TransportSecurity"
remove_path "$TYPELESS_DIR/blob_storage" "blob_storage"
remove_path "$TYPELESS_DIR/Cache" "Cache"
remove_path "$TYPELESS_DIR/Code Cache" "Code Cache"
remove_path "$TYPELESS_DIR/GPUCache" "GPUCache"
remove_path "$TYPELESS_DIR/DawnGraphiteCache" "DawnGraphiteCache"
remove_path "$TYPELESS_DIR/DawnWebGPUCache" "DawnWebGPUCache"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/Cookies" "partition Cookies"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/Cookies-journal" "partition Cookies-journal"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/Local Storage" "partition Local Storage"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/Session Storage" "partition Session Storage"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/SharedStorage" "partition SharedStorage"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/SharedStorage-wal" "partition SharedStorage-wal"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/SharedStorage-shm" "partition SharedStorage-shm"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/Trust Tokens" "partition Trust Tokens"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/Trust Tokens-journal" "partition Trust Tokens-journal"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/Network Persistent State" "partition Network Persistent State"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/TransportSecurity" "partition TransportSecurity"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/blob_storage" "partition blob_storage"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/Cache" "partition Cache"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/Code Cache" "partition Code Cache"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/GPUCache" "partition GPUCache"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/DawnGraphiteCache" "partition DawnGraphiteCache"
remove_path "$TYPELESS_DIR/Partitions/no-proxy-session/DawnWebGPUCache" "partition DawnWebGPUCache"

# 7. Clear updater/cache state. This also removes a bad pending update that can
# repeatedly trigger macOS signature validation failures.
remove_path "$CACHES_DIR/now.typeless.desktop" "now.typeless.desktop cache"
remove_path "$CACHES_DIR/typeless-updater" "typeless-updater cache"
remove_path "$CACHES_DIR/now.typeless.desktop.ShipIt" "Squirrel ShipIt cache"

# 8. Restart Typeless
if [ -n "$TYPELESS_APP_PATH_FOUND" ]; then
  echo "[reset-device] Starting Typeless..."
  open "$TYPELESS_APP_PATH_FOUND"
  echo "[reset-device] Typeless started"
else
  echo "[reset-device] Typeless.app not found in /Applications or ~/Applications, please start it manually"
fi

echo ""
echo "[reset-device] Done! Backup: $BACKUP_DIR"
echo "[reset-device] Typeless will rebuild local account/session state on next login."
echo "[reset-device] Keychain UUID backup: $BACKUP_DIR/keychain-device-uuid.txt"
echo "[reset-device] You'll need to log in again in the Typeless app."
