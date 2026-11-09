import express from 'express';
import { db, getUserRow, nowIso, USER_SELECT } from '../database/index.js';
import { avatarUpload } from '../upload.js';
import {
  publicUser,
  serializeVideo,
  VIDEO_SELECT,
  videoViewerState,
} from '../serializers.js';
import { requireAuth, optionalAuth } from '../auth.js';
import { fail, paging, removeMediaFile, text, wrap } from '../util.js';

export const usersRouter = express.Router();

usersRouter.get(
  '/me',
  requireAuth,
  wrap((req, res) => res.json({ user: publicUser(req.user) })),
);

usersRouter.patch(
  '/me',
  requireAuth,
  wrap(async (req, res) => {
    const hasNickname = req.body?.nickname !== undefined;
    const hasBio = req.body?.bio !== undefined;
    if (!hasNickname && !hasBio) {
      return fail(res, 400, 'VALIDATION_ERROR', '没有需要更新的字段');
    }
    const nickname = hasNickname ? text(req.body.nickname, 20) : req.user.nickname;
    const bio = hasBio ? text(req.body.bio, 80) : req.user.bio;
    if (!nickname) {
      return fail(res, 400, 'VALIDATION_ERROR', '昵称不能为空');
    }
    await db
      .prepare('UPDATE users SET nickname = ?, bio = ? WHERE id = ?')
      .run(nickname, bio, req.user.id);
    return res.json({ user: publicUser(await getUserRow(req.user.id)) });
  }),
);

usersRouter.post(
  '/me/avatar',
  requireAuth,
  avatarUpload.single('avatar'),
  wrap(async (req, res) => {
    if (!req.file) {
      return fail(res, 400, 'VALIDATION_ERROR', '请上传头像图片');
    }
    const avatarUrl = `/media/avatars/${req.file.filename}`;
    const oldAvatar = req.user.avatar_url;
    await db.prepare('UPDATE users SET avatar_url = ? WHERE id = ?').run(avatarUrl, req.user.id);
    removeMediaFile(oldAvatar);
    return res.json({ user: publicUser(await getUserRow(req.user.id)) });
  }),
);

usersRouter.get(
  '/users/suggested',
  optionalAuth,
  wrap(async (req, res) => {
    const limit = Math.min(20, Math.max(1, Number.parseInt(req.query.limit, 10) || 8));
    const viewerId = req.user?.id || 0;
    const rows = await db
      .prepare(
        `${USER_SELECT}
         WHERE u.id != ?
           AND u.id NOT IN (SELECT following_id FROM follows WHERE follower_id = ?)
         ORDER BY follower_count DESC, u.id DESC
         LIMIT ?`,
      )
      .all(viewerId, viewerId, limit);
    return res.json({ items: rows.map((row) => publicUser(row)) });
  }),
);

usersRouter.get(
  '/users/:id',
  optionalAuth,
  wrap(async (req, res) => {
    const id = Number.parseInt(req.params.id, 10);
    const row = Number.isFinite(id) ? await getUserRow(id) : null;
    if (!row) return fail(res, 404, 'NOT_FOUND', '用户不存在');
    let isFollowing = false;
    if (req.user && req.user.id !== id) {
      isFollowing = Boolean(
        await db
          .prepare('SELECT 1 AS ok FROM follows WHERE follower_id = ? AND following_id = ?')
          .get(req.user.id, id),
      );
    }
    return res.json({ user: publicUser(row, { isFollowing }) });
  }),
);

usersRouter.get(
  '/users/:id/videos',
  optionalAuth,
  wrap(async (req, res) => {
    const id = Number.parseInt(req.params.id, 10);
    if (!Number.isFinite(id) || !(await getUserRow(id))) {
      return fail(res, 404, 'NOT_FOUND', '用户不存在');
    }
    const type = req.query.type === 'liked' ? 'liked' : 'posts';
    const { limit, offset } = paging(req.query, { defaultLimit: 12, maxLimit: 30 });

    const rows =
      type === 'liked'
        ? await db
            .prepare(
              `${VIDEO_SELECT}
               JOIN likes l ON l.video_id = v.id
               WHERE l.user_id = ?
               ORDER BY l.created_at DESC, v.id DESC
               LIMIT ? OFFSET ?`,
            )
            .all(id, limit + 1, offset)
        : await db
            .prepare(
              `${VIDEO_SELECT}
               WHERE v.user_id = ? AND v.status = 'published'
               ORDER BY v.created_at DESC, v.id DESC
               LIMIT ? OFFSET ?`,
            )
            .all(id, limit + 1, offset);

    const hasMore = rows.length > limit;
    const page = hasMore ? rows.slice(0, limit) : rows;
    const state = await videoViewerState(page, req.user?.id);
    return res.json({
      items: page.map((row) => serializeVideo(row, state)),
      nextCursor: hasMore ? String(offset + limit) : null,
    });
  }),
);

usersRouter.post(
  '/users/:id/follow',
  requireAuth,
  wrap(async (req, res) => {
    const id = Number.parseInt(req.params.id, 10);
    if (id === req.user.id) return fail(res, 400, 'VALIDATION_ERROR', '不能关注自己');
    if (!Number.isFinite(id) || !(await getUserRow(id))) {
      return fail(res, 404, 'NOT_FOUND', '用户不存在');
    }
    await db
      .prepare(
        `INSERT INTO follows (follower_id, following_id, created_at)
         VALUES (?, ?, ?) ON CONFLICT DO NOTHING`,
      )
      .run(req.user.id, id, nowIso());
    const row = await db
      .prepare('SELECT COUNT(*) AS c FROM follows WHERE following_id = ?')
      .get(id);
    return res.json({ following: true, followerCount: Number(row.c) });
  }),
);

usersRouter.delete(
  '/users/:id/follow',
  requireAuth,
  wrap(async (req, res) => {
    const id = Number.parseInt(req.params.id, 10);
    await db
      .prepare('DELETE FROM follows WHERE follower_id = ? AND following_id = ?')
      .run(req.user.id, id);
    const row = await db
      .prepare('SELECT COUNT(*) AS c FROM follows WHERE following_id = ?')
      .get(id);
    return res.json({ following: false, followerCount: Number(row.c) });
  }),
);
