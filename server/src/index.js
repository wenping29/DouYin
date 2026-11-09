import express from 'express';
import cors from 'cors';
import multer from 'multer';
import { config } from './config.js';
import { closeDb, dialect, initDb } from './database/index.js';
import { authRouter } from './routes/auth.js';
import { usersRouter } from './routes/users.js';
import { videosRouter } from './routes/videos.js';
import { commentsRouter } from './routes/comments.js';
import { feedRouter } from './routes/feed.js';
import { searchRouter } from './routes/search.js';
import { seedDatabase } from './seed.js';

const app = express();

app.disable('x-powered-by');
app.use(
  cors({
    origin: true,
    exposedHeaders: ['Content-Range', 'Accept-Ranges', 'Content-Length'],
  }),
);
app.use(express.json({ limit: '1mb' }));

// 简单请求日志（开发期）
app.use((req, res, next) => {
  if (!config.isDev) return next();
  const started = Date.now();
  res.on('finish', () => {
    if (req.path === '/api/health') return;
    console.log(`${req.method} ${req.originalUrl} ${res.statusCode} ${Date.now() - started}ms`);
  });
  next();
});

app.get('/api/health', (_req, res) => {
  res.json({ ok: true, uptime: process.uptime(), database: dialect });
});

app.use('/api/auth', authRouter);
app.use('/api', usersRouter);
app.use('/api/videos', videosRouter);
app.use('/api', commentsRouter);
app.use('/api', feedRouter);
app.use('/api', searchRouter);

// 媒体静态服务：Express 静态中间件原生支持 Range / 206，video_player 可拖动进度
app.use(
  '/media',
  express.static(config.mediaDir, {
    maxAge: '1h',
    setHeaders(res) {
      res.setHeader('Accept-Ranges', 'bytes');
      res.setHeader('Cache-Control', 'public, max-age=3600');
    },
  }),
);

app.use((req, res) => {
  res.status(404).json({
    error: { code: 'NOT_FOUND', message: `接口不存在：${req.method} ${req.path}` },
  });
});

// eslint-disable-next-line no-unused-vars
app.use((err, req, res, next) => {
  if (res.headersSent) return next(err);

  if (err instanceof multer.MulterError) {
    if (err.code === 'LIMIT_FILE_SIZE') {
      return res.status(413).json({
        error: { code: 'FILE_TOO_LARGE', message: '文件超过大小限制' },
      });
    }
    return res.status(400).json({
      error: { code: 'UPLOAD_ERROR', message: `上传失败：${err.message}` },
    });
  }

  const known = {
    INVALID_VIDEO_TYPE: ['INVALID_VIDEO_TYPE', '仅支持视频文件'],
    INVALID_IMAGE_TYPE: ['INVALID_IMAGE_TYPE', '仅支持图片文件'],
    UNEXPECTED_FIELD: ['UNEXPECTED_FIELD', '不支持的上传字段'],
  };
  if (err?.message && known[err.message]) {
    const [code, message] = known[err.message];
    return res.status(400).json({ error: { code, message } });
  }

  if (err instanceof SyntaxError && 'body' in err) {
    return res.status(400).json({
      error: { code: 'VALIDATION_ERROR', message: '请求体不是合法 JSON' },
    });
  }

  console.error('[error]', err);
  return res.status(500).json({
    error: { code: 'INTERNAL_ERROR', message: '服务端开小差了，请稍后重试' },
  });
});

async function start() {
  await initDb();
  const seedResult = await seedDatabase();
  if (seedResult.seeded) {
    console.log(
      `[seed] 已写入 ${seedResult.users} 个演示账号、${seedResult.videos} 条视频；密码均为 ${seedResult.password}`,
    );
  }

  return app.listen(config.port, '0.0.0.0', () => {
    console.log(`\n抖影 API 已启动: http://localhost:${config.port}`);
    console.log(`Android 模拟器访问: http://10.0.2.2:${config.port}`);
    console.log(`健康检查: http://localhost:${config.port}/api/health`);
    console.log(`数据库: ${dialect === 'postgres' ? 'PostgreSQL' : 'SQLite'}`);
    console.log(
      `演示账号: aze / xiaoman / lulu / shanju，密码 demo1234\n`,
    );
  });
}

let server;
try {
  server = await start();
} catch (error) {
  console.error('[startup] 启动失败:', error);
  await closeDb().catch(() => undefined);
  process.exit(1);
}

let shuttingDown = false;
async function shutdown(signal) {
  if (shuttingDown) return;
  shuttingDown = true;
  console.log(`\n[shutdown] 收到 ${signal}，正在关闭...`);
  server.close(async () => {
    await closeDb().catch((error) => console.error('[shutdown] 关闭数据库失败:', error));
    process.exit(0);
  });
  // 兜底：10 秒内没有正常关闭就强制退出
  setTimeout(() => process.exit(0), 10000).unref();
}

process.on('SIGINT', () => shutdown('SIGINT'));
process.on('SIGTERM', () => shutdown('SIGTERM'));
