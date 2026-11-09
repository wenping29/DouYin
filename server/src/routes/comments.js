import express from 'express';
import { db } from '../database/index.js';
import { requireAuth } from '../auth.js';
import { fail, inTransaction, wrap } from '../util.js';

export const commentsRouter = express.Router();

commentsRouter.delete(
  '/comments/:id',
  requireAuth,
  wrap(async (req, res) => {
    const id = Number.parseInt(req.params.id, 10);
    const row = Number.isFinite(id)
      ? await db
          .prepare(
            `SELECT c.*, v.user_id AS video_owner_id
             FROM comments c JOIN videos v ON v.id = c.video_id
             WHERE c.id = ?`,
          )
          .get(id)
      : null;
    if (!row) return fail(res, 404, 'NOT_FOUND', '评论不存在');
    if (row.user_id !== req.user.id && row.video_owner_id !== req.user.id) {
      return fail(res, 403, 'FORBIDDEN', '没有权限删除这条评论');
    }

    const commentCount = await inTransaction(async () => {
      await db.prepare('DELETE FROM comments WHERE id = ?').run(id);
      await db
        .prepare(
          `UPDATE videos
           SET comment_count = CASE WHEN comment_count > 0 THEN comment_count - 1 ELSE 0 END
           WHERE id = ?`,
        )
        .run(row.video_id);
      const countRow = await db
        .prepare('SELECT comment_count FROM videos WHERE id = ?')
        .get(row.video_id);
      return countRow.comment_count;
    });

    return res.json({ ok: true, commentCount: Number(commentCount) });
  }),
);
