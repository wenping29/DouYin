import express from 'express';
import { db, USER_SELECT } from '../database/index.js';
import { optionalAuth } from '../auth.js';
import { publicUser, serializeVideo, VIDEO_SELECT, videoViewerState } from '../serializers.js';
import { escapeLike, wrap } from '../util.js';

export const searchRouter = express.Router();

searchRouter.get(
  '/search',
  optionalAuth,
  wrap(async (req, res) => {
    const q = String(req.query.q || '').trim().slice(0, 50);
    const type = ['all', 'videos', 'users'].includes(req.query.type) ? req.query.type : 'all';
    if (!q) return res.json({ videos: [], users: [] });

    const pattern = `%${escapeLike(q)}%`;
    let videos = [];
    let users = [];

    if (type === 'all' || type === 'videos') {
      const rows = await db
        .prepare(
          `${VIDEO_SELECT}
           WHERE v.status = 'published'
             AND (LOWER(v.title) LIKE LOWER(?) ESCAPE '\\'
               OR LOWER(v.description) LIKE LOWER(?) ESCAPE '\\')
           ORDER BY v.like_count DESC, v.id DESC
           LIMIT 20`,
        )
        .all(pattern, pattern);
      const state = await videoViewerState(rows, req.user?.id);
      videos = rows.map((row) => serializeVideo(row, state));
    }

    if (type === 'all' || type === 'users') {
      const rows = await db
        .prepare(
          `${USER_SELECT}
           WHERE LOWER(u.nickname) LIKE LOWER(?) ESCAPE '\\'
              OR LOWER(u.username) LIKE LOWER(?) ESCAPE '\\'
           ORDER BY follower_count DESC, u.id DESC
           LIMIT 20`,
        )
        .all(pattern, pattern);

      let followingIds = new Set();
      if (req.user && rows.length > 0) {
        const marks = rows.map(() => '?').join(',');
        const ids = rows.map((row) => row.id);
        const followingRows = await db
          .prepare(
            `SELECT following_id FROM follows WHERE follower_id = ? AND following_id IN (${marks})`,
          )
          .all(req.user.id, ...ids);
        followingIds = new Set(followingRows.map((row) => row.following_id));
      }
      users = rows.map((row) =>
        publicUser(row, { isFollowing: followingIds.has(row.id) }),
      );
    }

    return res.json({ videos, users });
  }),
);
