import { config } from '../config.js';

// 按需加载驱动：使用 PostgreSQL 时不会触发 node:sqlite 的实验性警告。
const db =
  config.dbDriver === 'postgres'
    ? (await import('./postgres.js')).createPostgresDriver(config)
    : (await import('./sqlite.js')).createSqliteDriver(config);

export { db };

export const dialect = db.dialect;

export const nowIso = () => new Date().toISOString();

/** 读取用户，附带关注数/粉丝数/作品数 */
export const USER_SELECT = `
  SELECT u.*,
    (SELECT COUNT(*) FROM follows f WHERE f.following_id = u.id) AS follower_count,
    (SELECT COUNT(*) FROM follows f WHERE f.follower_id = u.id) AS following_count,
    (SELECT COUNT(*) FROM videos v WHERE v.user_id = u.id AND v.status = 'published') AS video_count
  FROM users u
`;

export function initDb() {
  return db.init();
}

export function getUserRow(id) {
  return db.prepare(`${USER_SELECT} WHERE u.id = ?`).get(id);
}

export function inTransaction(fn) {
  return db.transaction(fn);
}

export function resetDatabase() {
  return db.reset();
}

export function closeDb() {
  return db.close();
}
