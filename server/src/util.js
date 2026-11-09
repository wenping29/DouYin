import fs from 'node:fs';
import path from 'node:path';
import { config } from './config.js';
import { inTransaction as runInTransaction } from './database/index.js';

export function fail(res, status, code, message) {
  return res.status(status).json({ error: { code, message } });
}

export function wrap(fn) {
  return (req, res, next) => Promise.resolve(fn(req, res, next)).catch(next);
}

export function paging(query, { defaultLimit = 10, maxLimit = 50 } = {}) {
  const rawLimit = Number.parseInt(query.limit, 10);
  const rawCursor = Number.parseInt(query.cursor, 10);
  const limit = Number.isFinite(rawLimit) ? Math.min(maxLimit, Math.max(1, rawLimit)) : defaultLimit;
  const offset = Number.isFinite(rawCursor) && rawCursor > 0 ? rawCursor : 0;
  return { limit, offset };
}

export function text(value, maxLength) {
  return String(value ?? '').trim().slice(0, maxLength);
}

export function escapeLike(value) {
  return value.replace(/[\\%_]/g, '\\$&');
}

export function inTransaction(fn) {
  return runInTransaction(fn);
}

/** 安全删除 media 目录下的文件 */
export function removeMediaFile(relativeUrl) {
  if (!relativeUrl || !relativeUrl.startsWith('/media/')) return;
  const mediaRoot = path.resolve(config.mediaDir);
  const full = path.resolve(mediaRoot, relativeUrl.slice('/media/'.length));
  if (!full.startsWith(mediaRoot + path.sep)) return;
  fs.rmSync(full, { force: true });
}
