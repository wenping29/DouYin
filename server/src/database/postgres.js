import { AsyncLocalStorage } from 'node:async_hooks';
import pg from 'pg';
import { postgresSchema } from './schema.js';

const { Pool, types } = pg;

// BIGINT / BIGSERIAL 默认会以字符串返回，这里统一转成 Number，保持与 SQLite 一致。
types.setTypeParser(20, (value) => Number(value));
// TIMESTAMPTZ / TIMESTAMP 统一返回 ISO 字符串，避免 API 序列化行为因数据库而变。
types.setTypeParser(1184, (value) => new Date(value).toISOString());
types.setTypeParser(1114, (value) => new Date(`${value}Z`).toISOString());

/** 把业务 SQL 里的 ? 占位符转换成 PostgreSQL 的 $1、$2…… */
function convertPlaceholders(sql) {
  let index = 0;
  let inString = false;
  let output = '';

  for (let i = 0; i < sql.length; i += 1) {
    const char = sql[i];
    if (char === "'") {
      if (inString && sql[i + 1] === "'") {
        output += "''";
        i += 1;
        continue;
      }
      inString = !inString;
      output += char;
      continue;
    }
    if (char === '?' && !inString) {
      index += 1;
      output += `$${index}`;
      continue;
    }
    output += char;
  }

  return output;
}

export function createPostgresDriver(config) {
  const poolOptions = config.databaseUrl
    ? { connectionString: config.databaseUrl }
    : {
        host: config.pg.host,
        port: config.pg.port,
        user: config.pg.user,
        password: config.pg.password,
        database: config.pg.database,
      };

  if (config.pg.ssl) {
    poolOptions.ssl = { rejectUnauthorized: false };
  }

  const pool = new Pool({ ...poolOptions, max: config.pg.poolSize });
  const transactionContext = new AsyncLocalStorage();
  let initPromise = null;

  pool.on('error', (error) => {
    console.error('[postgres] 空闲连接异常:', error.message);
  });

  async function query(sql, params = []) {
    const client = transactionContext.getStore();
    const executor = client || pool;
    return executor.query(convertPlaceholders(sql), params);
  }

  return {
    dialect: 'postgres',

    init() {
      if (!initPromise) initPromise = pool.query(postgresSchema);
      return initPromise;
    },

    prepare(sql) {
      return {
        async get(...params) {
          const result = await query(sql, params);
          return result.rows[0] ?? null;
        },
        async all(...params) {
          const result = await query(sql, params);
          return result.rows;
        },
        async run(...params) {
          const result = await query(sql, params);
          return {
            changes: Number(result.rowCount || 0),
            lastInsertRowid: result.rows?.[0]?.id ?? null,
          };
        },
      };
    },

    async exec(sql) {
      await query(sql);
    },

    async transaction(fn) {
      const client = await pool.connect();
      try {
        await client.query('BEGIN');
        const result = await transactionContext.run(client, fn);
        await client.query('COMMIT');
        return result;
      } catch (error) {
        try {
          await client.query('ROLLBACK');
        } catch {
          // 忽略回滚失败，保留原始异常
        }
        throw error;
      } finally {
        client.release();
      }
    },

    async reset() {
      await pool.query(
        'TRUNCATE TABLE users, videos, comments, likes, follows, video_views RESTART IDENTITY CASCADE',
      );
    },

    async close() {
      await pool.end();
    },
  };
}
