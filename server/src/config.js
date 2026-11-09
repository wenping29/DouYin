import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const rootDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

function resolveDriver() {
  const explicit = String(process.env.DB_DRIVER || '').trim().toLowerCase();
  if (explicit === 'postgres' || explicit === 'postgresql') return 'postgres';
  if (explicit === 'sqlite') return 'sqlite';
  // 没有显式指定时：提供 DATABASE_URL 就使用 PostgreSQL，否则默认 SQLite。
  return process.env.DATABASE_URL ? 'postgres' : 'sqlite';
}

function envFlag(name) {
  return ['1', 'true', 'yes', 'on'].includes(String(process.env[name] || '').toLowerCase());
}

export const config = {
  rootDir,
  port: Number(process.env.PORT || 8080),
  jwtSecret: process.env.JWT_SECRET || 'douying-dev-secret-change-me',
  mediaDir: path.join(rootDir, 'media'),
  dataDir: path.join(rootDir, 'data'),
  dbDriver: resolveDriver(),
  dbPath: process.env.DB_PATH || path.join(rootDir, 'data', 'douying.db'),
  databaseUrl: process.env.DATABASE_URL || '',
  pg: {
    host: process.env.PGHOST || '127.0.0.1',
    port: Number(process.env.PGPORT || 5432),
    user: process.env.PGUSER || 'postgres',
    password: process.env.PGPASSWORD || '',
    database: process.env.PGDATABASE || 'douying',
    ssl: envFlag('PGSSL') || envFlag('DATABASE_SSL'),
    poolSize: Number(process.env.PGPOOL_SIZE || 10),
  },
  maxVideoBytes: 200 * 1024 * 1024,
  maxImageBytes: 5 * 1024 * 1024,
  isDev: process.env.NODE_ENV !== 'production',
};

for (const dir of [
  config.mediaDir,
  path.join(config.mediaDir, 'videos'),
  path.join(config.mediaDir, 'covers'),
  path.join(config.mediaDir, 'avatars'),
]) {
  fs.mkdirSync(dir, { recursive: true });
}

if (config.dbDriver === 'sqlite') {
  fs.mkdirSync(config.dataDir, { recursive: true });
}
