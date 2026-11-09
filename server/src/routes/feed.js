import express from 'express';
import { db } from '../database/index.js';
import { optionalAuth } from '../auth.js';
import { rankRecommendations } from '../domain/recommendation.js';
import { serializeVideo, VIDEO_SELECT, videoViewerState } from '../serializers.js';
import { fail, paging, wrap } from '../util.js';

export const feedRouter = express.Router();

const CANDIDATE_LIMIT = 500;

feedRouter.get(
  '/feed',
  optionalAuth,
  wrap(async (req, res) => {
    const tab = req.query.tab === 'following' ? 'following' : 'recommend';
    const { limit, offset } = paging(req.query, { defaultLimit: 10, maxLimit: 30 });

    if (tab === 'following') {
      if (!req.user) return fail(res, 401, 'UNAUTHORIZED', '关注流需要先登录');
      const rows = await db
        .prepare(
          `${VIDEO_SELECT}
           JOIN follows f ON f.following_id = v.user_id
           WHERE f.follower_id = ? AND v.status = 'published'
           ORDER BY v.created_at DESC, v.id DESC
           LIMIT ? OFFSET ?`,
        )
        .all(req.user.id, limit + 1, offset);

      const hasMore = rows.length > limit;
      const page = hasMore ? rows.slice(0, limit) : rows;
      const state = await videoViewerState(page, req.user.id);
      return res.json({
        items: page.map((row) => serializeVideo(row, state)),
        nextCursor: hasMore ? String(offset + limit) : null,
      });
    }

    // 推荐流：候选集 → 热度 + 时间衰减 → 排序 → 偏移分页
    const candidates = await db
      .prepare(
        `${VIDEO_SELECT}
         WHERE v.status = 'published'
         ORDER BY v.created_at DESC
         LIMIT ?`,
      )
      .all(CANDIDATE_LIMIT);

    const visible = req.user
      ? candidates.filter((row) => row.user_id !== req.user.id)
      : candidates;

    const ranked = rankRecommendations(visible);

    const page = ranked.slice(offset, offset + limit + 1);
    const hasMore = page.length > limit;
    const items = hasMore ? page.slice(0, limit) : page;
    const state = await videoViewerState(items, req.user?.id);

    return res.json({
      items: items.map((row) => serializeVideo(row, state)),
      nextCursor: hasMore ? String(offset + limit) : null,
    });
  }),
);
