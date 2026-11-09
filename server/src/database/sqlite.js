import fs from 'node:fs';
import path from 'node:path';
import { AsyncLocalStorage } from 'node:async_hooks';
import { DatabaseSync } from 'node:sqlite';
import { sqliteSchema } from './schema.js';

/**
 * SQLite 驱动。
 *
 * node:sqlite 是同步 API，而路由层统一使用 async/await；为了避免异步事务之间
 * 互相穿插，这里用一个串行队列把「事务外的数据库操作」排队执行，事务内部则
 * 通过 AsyncLocalStorage 直接复用同一个连接。
 */
export function createSqliteDriver(config) {
  fs.mkdirSync(path.dirname(config.dbPath), { recursive: true });

  const raw = new DatabaseSync(config.dbPath);
  raw.exec(`
    PRAGMA journal_mode = WAL;
    PRAGMA foreign_keys = ON;
    PRAGMA busy_timeout = 5000;
  `);

  const transactionContext = new AsyncLocalStorage();
  let queue = Promise.resolve();
  let initPromise = null;

  function enqueue(task) {
    const next = queue.then(task, task);
    queue = next.then(
      () => undefined,
      () => undefined,
    );
    return next;
  }

  function withConnection(task) {
    return transactionContext.getStore() ? task() : enqueue(task);
  }

  return {
    dialect: 'sqlite',

    init() {
      if (!initPromise) {
        initPromise = withConnection(async () => raw.exec(sqliteSchema));
      }
      return initPromise;
    },

    prepare(sql) {
      const statement = raw.prepare(sql);
      return {
        get: (...params) =>
          withConnection(async () => statement.get(...params) ?? null),
        all: (...params) => withConnection(async () => statement.all(...params)),
        run: (...params) =>
          withConnection(async () => {
            const info = statement.run(...params);
            return {
              changes: Number(info.changes),
              lastInsertRowid: Number(info.lastInsertRowid),
            };
          }),
      };
    },

    exec(sql) {
      return withConnection(async () => raw.exec(sql));
    },

    transaction(fn) {
      return enqueue(async () => {
        raw.exec('BEGIN');
        try {
          const result = await transactionContext.run(true, fn);
          raw.exec('COMMIT');
          return result;
        } catch (error) {
          try {
            raw.exec('ROLLBACK');
          } catch {
            // 忽略回滚失败，保留原始异常
          }
          throw error;
        }
      });
    },

    async reset() {
      await withConnection(async () => {
        raw.exec(`
          DELETE FROM users;
          DELETE FROM sqlite_sequence WHERE name IN ('users', 'videos', 'comments');
        `);
      });
    },

    async close() {
      raw.close();
    },
  };
}
