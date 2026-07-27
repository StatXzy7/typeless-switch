#!/usr/bin/env node
/**
 * Typeless Dictionary Importer
 *
 * Reads a previously exported dictionary JSON and imports all words into
 * the currently logged-in Typeless account via the API.
 *
 * Default mode uses the official bulk-import API (same as Typeless app CSV
 * import): chunks of 200 terms, posted in parallel.
 *
 * Usage:
 *   node scripts/import-dictionary.mjs --input <path-to-export.json> [--dry-run]
 *   node scripts/import-dictionary.mjs --input <export.json> --mode bulk
 *   node scripts/import-dictionary.mjs --input <export.json> --mode full --concurrency 12
 *
 * Modes:
 *   bulk  - POST /user/dictionary/bulk-import (fast; term-only, like official CSV)
 *   full  - POST /user/dictionary/add concurrently (preserves lang/category/replace)
 *
 * Requires: TYPELESS_VENDOR_NODE_MODULES env var (set by wrapper script).
 */

import fs from 'fs';
import path from 'path';
import { readTypelessUser } from './read-user-session.mjs';

const API_BASE = 'https://api.typeless.com';
const BULK_CHUNK_SIZE = 200;

function getArg(flag, fallback = null) {
  const i = process.argv.indexOf(flag);
  return i === -1 ? fallback : (process.argv[i + 1] ?? fallback);
}
function hasFlag(flag) { return process.argv.includes(flag); }

async function getAccessToken() {
  const user = await readTypelessUser();
  if (!user?.access_token) throw new Error('未读取到 access_token');
  return { token: user.access_token, email: user.email, user_id: user.user_id };
}

async function listExistingWords(token) {
  const res = await fetch(`${API_BASE}/user/dictionary/list?size=10000`, {
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
  });
  const data = await res.json();
  if (!res.ok || data?.status !== 'OK') throw new Error(`词典列表请求失败: ${res.status}`);
  return data?.data?.words || [];
}

function authHeaders(token) {
  return {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
  };
}

async function addWord(token, word) {
  const body = {
    term: word.term,
    lang: word.lang,
    category: word.category,
    auto: word.auto ?? true,
    replace: word.replace ?? false,
    replace_targets: word.replace_targets || [],
  };
  const res = await fetch(`${API_BASE}/user/dictionary/add`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify(body),
  });
  const data = await res.json();
  return { ok: res.ok && data?.status === 'OK', status: res.status, data, term: word.term };
}

async function bulkImportChunk(token, terms) {
  const res = await fetch(`${API_BASE}/user/dictionary/bulk-import`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify({ content: terms.join('\n') }),
  });
  let data = null;
  try {
    data = await res.json();
  } catch {
    data = null;
  }
  const ok = res.ok && (data?.status === 'OK' || data?.success === true || data?.code === 0 || !data?.status);
  // Some responses use { status: 'OK' }, others wrap differently; treat HTTP 2xx
  // without explicit FAIL as success (matches Typeless client behavior).
  const failedExplicitly = data?.status === 'FAIL' || data?.success === false;
  return {
    ok: res.ok && !failedExplicitly,
    status: res.status,
    data,
    count: terms.length,
  };
}

function chunkArray(items, size) {
  const chunks = [];
  for (let i = 0; i < items.length; i += size) {
    chunks.push(items.slice(i, i + size));
  }
  return chunks;
}

async function mapPool(items, concurrency, worker) {
  const results = new Array(items.length);
  let next = 0;
  async function runner() {
    while (true) {
      const i = next++;
      if (i >= items.length) return;
      results[i] = await worker(items[i], i);
    }
  }
  const n = Math.max(1, Math.min(concurrency, items.length || 1));
  await Promise.all(Array.from({ length: n }, () => runner()));
  return results;
}

async function importBulk(token, words) {
  const terms = words.map((w) => w.term).filter(Boolean);
  const chunks = chunkArray(terms, BULK_CHUNK_SIZE);
  console.error(`[import] 使用 bulk-import：${terms.length} 词 / ${chunks.length} 批（每批 ${BULK_CHUNK_SIZE}，并行提交）`);

  const settled = await Promise.allSettled(
    chunks.map((chunk, index) => bulkImportChunk(token, chunk).then((result) => ({ ...result, index, chunk }))),
  );

  let success = 0;
  let failed = 0;
  for (const item of settled) {
    if (item.status === 'fulfilled' && item.value.ok) {
      success += item.value.count;
      console.error(`  ✓ 批次 ${item.value.index + 1}/${chunks.length}: ${item.value.count} 词`);
    } else if (item.status === 'fulfilled') {
      failed += item.value.count;
      console.error(
        `  ✗ 批次 ${item.value.index + 1}/${chunks.length}: HTTP ${item.value.status} `
        + `${JSON.stringify(item.value.data).substring(0, 160)}`,
      );
    } else {
      failed += BULK_CHUNK_SIZE;
      console.error(`  ✗ 批次失败: ${item.reason?.message || item.reason}`);
    }
  }
  return { success, failed };
}

async function importFull(token, words, concurrency) {
  console.error(`[import] 使用并发单条导入（保留 lang/category/replace），并发=${concurrency}`);
  let success = 0;
  let failed = 0;
  let done = 0;
  const total = words.length;

  await mapPool(words, concurrency, async (word) => {
    const result = await addWord(token, word);
    done++;
    if (result.ok) {
      success++;
    } else {
      failed++;
      console.error(`  ✗ ${word.term}: HTTP ${result.status} ${JSON.stringify(result.data).substring(0, 100)}`);
    }
    if (done % 50 === 0 || done === total) {
      console.error(`  …进度 ${done}/${total}（成功 ${success}，失败 ${failed}）`);
    }
    return result;
  });

  return { success, failed };
}

async function main() {
  const inputPath = getArg('--input');
  const dryRun = hasFlag('--dry-run');
  const mode = (getArg('--mode', 'bulk') || 'bulk').toLowerCase();
  const concurrency = Math.max(1, Number(getArg('--concurrency', '12')) || 12);

  if (!inputPath) {
    console.error('用法: node scripts/import-dictionary.mjs --input <export.json> [--dry-run] [--mode bulk|full] [--concurrency N]');
    process.exit(1);
  }
  if (!['bulk', 'full'].includes(mode)) {
    console.error(`[import] 未知模式: ${mode}（支持 bulk / full）`);
    process.exit(1);
  }

  const exportData = JSON.parse(fs.readFileSync(path.resolve(inputPath), 'utf8'));
  const wordsToImport = exportData.words || [];
  if (wordsToImport.length === 0) {
    console.error('[import] 导入文件中没有词条');
    process.exit(0);
  }

  console.error(`[import] 待导入词条: ${wordsToImport.length} 个 (来源: ${exportData.account?.email || 'unknown'})`);

  const { token, email, user_id } = await getAccessToken();
  console.error(`[import] 目标账号: ${email} (${user_id})`);

  // Check existing words to avoid duplicates
  const existing = await listExistingWords(token);
  const existingTerms = new Set(existing.map((w) => `${w.term}||${w.lang || ''}`));
  const existingTermOnly = new Set(existing.map((w) => w.term).filter(Boolean));
  console.error(`[import] 目标账号已有词条: ${existing.length} 个`);

  // bulk 只传 term；full 按 term+lang 去重
  const toAdd = mode === 'bulk'
    ? wordsToImport.filter((w) => w.term && !existingTermOnly.has(w.term))
    : wordsToImport.filter((w) => w.term && !existingTerms.has(`${w.term}||${w.lang || ''}`));
  const skipped = wordsToImport.length - toAdd.length;
  if (skipped > 0) console.error(`[import] 跳过已存在的词条: ${skipped} 个`);
  console.error(`[import] 需要新增的词条: ${toAdd.length} 个`);

  if (dryRun) {
    console.error('[import] --dry-run 模式，不执行实际导入');
    for (const w of toAdd.slice(0, 30)) console.error(`  + ${w.term} (${w.lang}, ${w.category})`);
    if (toAdd.length > 30) console.error(`  …以及另外 ${toAdd.length - 30} 个`);
    process.exit(0);
  }

  if (toAdd.length === 0) {
    console.error('[import] 没有需要新增的词条');
    console.log(JSON.stringify({
      ok: true,
      mode,
      target: { email, user_id },
      source: exportData.account || {},
      imported: 0,
      failed: 0,
      skipped,
      total_in_source: wordsToImport.length,
    }, null, 2));
    return;
  }

  const started = Date.now();
  const { success, failed } = mode === 'bulk'
    ? await importBulk(token, toAdd)
    : await importFull(token, toAdd, concurrency);
  const elapsedMs = Date.now() - started;

  console.error(`[import] 完成: 成功 ${success}, 失败 ${failed}, 跳过 ${skipped}, 耗时 ${(elapsedMs / 1000).toFixed(1)}s`);
  console.log(JSON.stringify({
    ok: failed === 0,
    mode,
    target: { email, user_id },
    source: exportData.account || {},
    imported: success,
    failed,
    skipped,
    total_in_source: wordsToImport.length,
    elapsed_ms: elapsedMs,
  }, null, 2));
}

main().catch((err) => {
  console.error(err?.stack || String(err));
  process.exit(1);
});
