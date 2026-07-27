/**
 * Robust Typeless session reader for Windows/macOS.
 * Tries multiple key derivations and conf encryption layouts,
 * because Typeless / electron-store / conf versions differ.
 */
import fs from 'fs';
import os from 'os';
import path from 'path';
import crypto from 'crypto';
import { pathToFileURL } from 'url';

const USER_DATA_DIR = process.platform === 'win32'
  ? path.join(process.env.APPDATA || path.join(os.homedir(), 'AppData', 'Roaming'), 'Typeless.exe')
  : path.join(os.homedir(), 'Library', 'Application Support', 'Typeless');

function keyCandidates() {
  const appNames = ['Typeless', 'Typeless.exe', 'typeless'];
  const arches = Array.from(new Set([process.arch, 'x64', 'ia32', 'arm64']));
  const platforms = Array.from(new Set([process.platform, 'win32', 'darwin']));
  const keys = [];

  for (const platform of platforms) {
    for (const arch of arches) {
      for (const appName of appNames) {
        const seed = crypto.createHash('sha256').update(`${platform}-${arch}`).digest('hex');
        const buf = crypto.pbkdf2Sync(seed + appName, 'typeless-user-service', 10000, 32, 'sha256');
        keys.push(buf);
        keys.push(buf.toString('hex'));
        keys.push(seed + appName);
      }
    }
  }
  return keys;
}

function tryDecryptConfStyle(fileBuf, encryptionKey) {
  const attempts = [];

  // Current conf: IV(16) + ':' + ciphertext
  attempts.push(() => {
    const iv = fileBuf.subarray(0, 16);
    const password = crypto.pbkdf2Sync(encryptionKey, iv.toString(), 10000, 32, 'sha512');
    const decipher = crypto.createDecipheriv('aes-256-cbc', password, iv);
    return Buffer.concat([
      decipher.update(fileBuf.subarray(17)),
      decipher.final(),
    ]).toString('utf8');
  });

  // Older layout: IV(16) + ciphertext (no colon)
  attempts.push(() => {
    const iv = fileBuf.subarray(0, 16);
    const password = crypto.pbkdf2Sync(encryptionKey, iv.toString(), 10000, 32, 'sha512');
    const decipher = crypto.createDecipheriv('aes-256-cbc', password, iv);
    return Buffer.concat([
      decipher.update(fileBuf.subarray(16)),
      decipher.final(),
    ]).toString('utf8');
  });

  // Direct key as cipher key (some custom forks)
  attempts.push(() => {
    const iv = fileBuf.subarray(0, 16);
    const key = Buffer.isBuffer(encryptionKey)
      ? encryptionKey.subarray(0, 32)
      : crypto.createHash('sha256').update(String(encryptionKey)).digest();
    const decipher = crypto.createDecipheriv('aes-256-cbc', key, iv);
    const start = fileBuf[16] === 0x3a ? 17 : 16;
    return Buffer.concat([
      decipher.update(fileBuf.subarray(start)),
      decipher.final(),
    ]).toString('utf8');
  });

  for (const attempt of attempts) {
    try {
      const text = attempt();
      if (text && text.trimStart().startsWith('{')) return text;
    } catch {
      // try next
    }
  }
  return null;
}

function parseStorePayload(text) {
  const parsed = JSON.parse(text);
  const raw = parsed?.userData;
  if (!raw) return null;
  const user = typeof raw === 'string' ? JSON.parse(raw) : raw;
  if (!user?.access_token) return null;
  return user;
}

async function tryElectronStore(encryptionKey) {
  const vendorRoot = process.env.TYPELESS_VENDOR_NODE_MODULES;
  if (!vendorRoot) return null;
  const modPath = path.join(vendorRoot, 'electron-store', 'index.js');
  if (!fs.existsSync(modPath)) return null;
  const mod = await import(pathToFileURL(modPath).href);
  const Store = mod.default;
  try {
    const store = new Store({
      name: 'user-data',
      cwd: USER_DATA_DIR,
      encryptionKey,
      clearInvalidConfig: false,
    });
    const raw = store.get('userData');
    if (!raw) return null;
    const user = typeof raw === 'string' ? JSON.parse(raw) : raw;
    if (!user?.access_token) return null;
    return user;
  } catch {
    return null;
  }
}

export function getUserDataDir() {
  return USER_DATA_DIR;
}

export async function readTypelessUser() {
  const filePath = path.join(USER_DATA_DIR, 'user-data.json');
  if (!fs.existsSync(filePath)) {
    throw new Error('未找到 user-data.json。请先在 Typeless 中登录，然后完全退出再重试。');
  }

  const fileBuf = fs.readFileSync(filePath);
  const keys = keyCandidates();

  for (const key of keys) {
    const user = await tryElectronStore(key);
    if (user) return user;
  }

  for (const key of keys) {
    const text = tryDecryptConfStyle(fileBuf, key);
    if (!text) continue;
    try {
      const user = parseStorePayload(text);
      if (user) return user;
    } catch {
      // try next
    }
  }

  throw new Error(
    '无法解密 Typeless 登录态（user-data.json）。\n'
    + '请先完全退出 Typeless，再打开并重新登录一次，然后退出后重试导出。\n'
    + '若是 Google/Apple 登录，也可改用邮箱验证码登录后再导出。',
  );
}
