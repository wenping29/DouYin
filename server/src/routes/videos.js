import crypto from 'node:crypto';
import path from 'node:path';
import express from 'express';
import { config } from '../config.js';
import { db, nowIso } from '../database/index.js';
import { optionalAuth, requireAuth } from '../auth.js';
import { generateCover } from '../cover.js';
import { videoUpload } from '../upload.js';
import {
  COMMENT_SELECT,
  serializeComment,
  serializeVideo,
  VIDEO_SELECT,
  videoViewerState,
} from '../serializers.js';
import {
  fail,
  inTransaction,
  paging,
  removeMediaFile,
  text,
  wrap,
} from '../util.js';

export const videosRouter = express.Router();

function getVideoRow(id) {
  return db.prepare(`${VIDEO_SELECT} WHERE v.id = ?`).get(id);
}

/* ---------------- 发布 / 删除 ---------------- */

videosRouter.post(
  '/',
  requireAuth,
  videoUpload.fields([
    { name: 'video', maxCount: 1 },
    { name: 'cover', maxCount: 1 },
  ]),
  wrap(async (req, res) => {
    const videoFile = req.files?.video?.[0];
    const coverFile = req.files?.cover?.[0];

    if (!videoFile) {
      if (coverFile) removeMediaFile(`/media/covers/${coverFile.filename}`);
      return fail(res, 400, 'VALIDATION_ERROR', '请上传视频文件');
    }
    if (coverFile && coverFile.size > config.maxImageBytes) {
      removeMediaFile(`/media/videos/${videoFile.filename}`);
      removeMediaFile(`/media/covers/${coverFile.filename}`);
      return fail(res, 413, 'FILE_TOO_LARGE', '封面不能超过 5MB');
    }

    const title = text(req.body?.title, 100) || '未命名作品';
    const description = text(req.body?.description, 500);
    const duration = Number(req.body?.duration) || 0;
    const videoUrl = `/media/videos/${videoFile.filename}`;
    const coverUrl = coverFile ? `/media/covers/${coverFile.filename}` : null;

    const info = await db
      .prepare(
        `INSERT INTO videos
           (user_id, title, description, video_url, cover_url, duration, created_at)
         VALUES (?, ?, ?, ?, ?, ?, ?) RETURNING id`,
      )
      .run(req.user.id, title, description, videoUrl, coverUrl, duration, nowIso());

    const videoId = Number(info.lastInsertRowid);
    if (!coverUrl) {
      const coverName = `v${videoId}.png`;
      generateCover(`video:${videoId}:${title}`, path.join(config.mediaDir, 'covers', coverName));
      await db
        .prepare('UPDATE videos SET cover_url = ? WHERE id = ?')
        .run(`/media/covers/${coverName}`, videoId);
    }

    return res.status(201).json({ video: serializeVideo(await getVideoRow(videoId)) });
  }),
);

videosRouter.delete(
  '/:id',
  requireAuth,
  wrap(async (req, res) => {
    const id = Number.parseInt(req.params.id, 10);
    const row = Number.isFinite(id) ? await getVideoRow(id) : null;
    if (!row) return fail(res, 404, 'NOT_FOUND', '视频不存在');
    if (row.user_id !== req.user.id) return fail(res, 403, 'FORBIDDEN', '只能删除自己的作品');
    await db.prepare('DELETE FROM videos WHERE id = ?').run(id);
    removeMediaFile(row.video_url);
    removeMediaFile(row.cover_url);
    return res.json({ ok: true });
  }),
);

/* ---------------- 详情 ---------------- */

videosRouter.get(
  '/:id',
  optionalAuth,
  wrap(async (req, res) => {
    const id = Number.parseInt(req.params.id, 10);
    const row = Number.isFinite(id) ? await getVideoRow(id) : null;
    if (!row) return fail(res, 404, 'NOT_FOUND', '视频不存在');
    const state = await videoViewerState([row], req.user?.id);
    return res.json({ video: serializeVideo(row, state) });
  }),
);

/* ---------------- 点赞 ---------------- */

videosRouter.post(
  '/:id/like',
  requireAuth,
  wrap(async (req, res) => {
    const id = Number.parseInt(req.params.id, 10);
    const row = Number.isFinite(id) ? await getVideoRow(id) : null;
    if (!row) return fail(res, 404, 'NOT_FOUND', '视频不存在');

    await inTransaction(async () => {
      const info = await db
        .prepare(
          `INSERT INTO likes (user_id, video_id, created_at)
           VALUES (?, ?, ?) ON CONFLICT DO NOTHING`,
        )
        .run(req.user.id, id, nowIso());
      if (Number(info.changes) > 0) {
        await db.prepare('UPDATE videos SET like_count = like_count + 1 WHERE id = ?').run(id);
      }
    });

    const updated = await db.prepare('SELECT like_count FROM videos WHERE id = ?').get(id);
    return res.json({ liked: true, likeCount: Number(updated.like_count) });
  }),
);

videosRouter.delete(
  '/:id/like',
  requireAuth,
  wrap(async (req, res) => {
    const id = Number.parseInt(req.params.id, 10);
    const row = Number.isFinite(id) ? await getVideoRow(id) : null;
    if (!row) return fail(res, 404, 'NOT_FOUND', '视频不存在');

    await inTransaction(async () => {
      const info = await db
        .prepare('DELETE FROM likes WHERE user_id = ? AND video_id = ?')
        .run(req.user.id, id);
      if (Number(info.changes) > 0) {
        await db
          .prepare(
            `UPDATE videos
             SET like_count = CASE WHEN like_count > 0 THEN like_count - 1 ELSE 0 END
             WHERE id = ?`,
          )
          .run(id);
      }
    });

    const updated = await db.prepare('SELECT like_count FROM videos WHERE id = ?').get(id);
    return res.json({ liked: false, likeCount: Number(updated.like_count) });
  }),
);

/* ---------------- 播放 / 分享 ---------------- */

videosRouter.post(
  '/:id/view',
  optionalAuth,
  wrap(async (req, res) => {
    const id = Number.parseInt(req.params.id, 10);
    const row = Number.isFinite(id) ? await getVideoRow(id) : null;
    if (!row) return fail(res, 404, 'NOT_FOUND', '视频不存在');

    await inTransaction(async () => {
      await db.prepare('UPDATE videos SET view_count = view_count + 1 WHERE id = ?').run(id);
      if (req.user) {
        await db
          .prepare(
            `INSERT INTO video_views (user_id, video_id, viewed_at) VALUES (?, ?, ?)
             ON CONFLICT(user_id, video_id) DO UPDATE SET viewed_at = excluded.viewed_at`,
          )
          .run(req.user.id, id, nowIso());
      }
    });

    const updated = await db.prepare('SELECT view_count FROM videos WHERE id = ?').get(id);
    return res.json({ viewCount: Number(updated.view_count) });
  }),
);

videosRouter.post(
  '/:id/share',
  wrap(async (req, res) => {
    const id = Number.parseInt(req.params.id, 10);
    const row = Number.isFinite(id) ? await getVideoRow(id) : null;
    if (!row) return fail(res, 404, 'NOT_FOUND', '视频不存在');
    await db.prepare('UPDATE videos SET share_count = share_count + 1 WHERE id = ?').run(id);
    const updated = await db.prepare('SELECT share_count FROM videos WHERE id = ?').get(id);
    return res.json({ shareCount: Number(updated.share_count) });
  }),
);

/* ---------------- 评论 ---------------- */

videosRouter.get(
  '/:id/comments',
  optionalAuth,
  wrap(async (req, res) => {
    const id = Number.parseInt(req.params.id, 10);
    const video = Number.isFinite(id) ? await getVideoRow(id) : null;
    if (!video) return fail(res, 404, 'NOT_FOUND', '视频不存在');
    const { limit, offset } = paging(req.query, { defaultLimit: 20, maxLimit: 50 });

    const rows = await db
      .prepare(
        `${COMMENT_SELECT}
         WHERE c.video_id = ?
         ORDER BY c.created_at DESC, c.id DESC
         LIMIT ? OFFSET ?`,
      )
      .all(id, limit + 1, offset);

    const hasMore = rows.length > limit;
    const page = hasMore ? rows.slice(0, limit) : rows;
    const totalRow = await db
      .prepare('SELECT COUNT(*) AS c FROM comments WHERE video_id = ?')
      .get(id);

    return res.json({
      items: page.map((row) =>
        serializeComment(row, { viewerId: req.user?.id, videoOwnerId: video.user_id }),
      ),
      nextCursor: hasMore ? String(offset + limit) : null,
      total: Number(totalRow.c),
    });
  }),
);

videosRouter.post(
  '/:id/comments',
  requireAuth,
  wrap(async (req, res) => {
    const id = Number.parseInt(req.params.id, 10);
    const video = Number.isFinite(id) ? await getVideoRow(id) : null;
    if (!video) return fail(res, 404, 'NOT_FOUND', '视频不存在');
    const content = text(req.body?.content, 500);
    if (!content) return fail(res, 400, 'VALIDATION_ERROR', '评论不能为空');

    const commentId = await inTransaction(async () => {
      const info = await db
        .prepare(
          `INSERT INTO comments (video_id, user_id, content, created_at)
           VALUES (?, ?, ?, ?) RETURNING id`,
        )
        .run(id, req.user.id, content, nowIso());
      await db.prepare('UPDATE videos SET comment_count = comment_count + 1 WHERE id = ?').run(id);
      return Number(info.lastInsertRowid);
    });

    const row = await db.prepare(`${COMMENT_SELECT} WHERE c.id = ?`).get(commentId);
    const countRow = await db
      .prepare('SELECT comment_count FROM videos WHERE id = ?')
      .get(id);
    return res.status(201).json({
      comment: serializeComment(row, { viewerId: req.user.id, videoOwnerId: video.user_id }),
      commentCount: Number(countRow.comment_count),
    });
  }),
);
