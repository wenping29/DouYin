import { db } from './database/index.js';

export const VIDEO_SELECT = `
  SELECT v.*, u.username AS author_username, u.nickname AS author_nickname,
         u.avatar_url AS author_avatar
  FROM videos v
  JOIN users u ON u.id = v.user_id
`;

export function publicUser(row, { isFollowing = false } = {}) {
  if (!row) return null;
  return {
    id: row.id,
    username: row.username,
    nickname: row.nickname,
    avatarUrl: row.avatar_url || null,
    bio: row.bio || '',
    followerCount: Number(row.follower_count || 0),
    followingCount: Number(row.following_count || 0),
    videoCount: Number(row.video_count || 0),
    isFollowing: Boolean(isFollowing),
    createdAt: row.created_at,
  };
}

export function authorOf(row) {
  return {
    id: row.user_id,
    username: row.author_username,
    nickname: row.author_nickname,
    avatarUrl: row.author_avatar || null,
  };
}

export function serializeVideo(row, { likedIds, followingIds } = {}) {
  return {
    id: row.id,
    title: row.title,
    description: row.description || '',
    videoUrl: row.video_url,
    coverUrl: row.cover_url || null,
    duration: Number(row.duration || 0),
    width: Number(row.width || 0),
    height: Number(row.height || 0),
    viewCount: Number(row.view_count || 0),
    likeCount: Number(row.like_count || 0),
    commentCount: Number(row.comment_count || 0),
    shareCount: Number(row.share_count || 0),
    createdAt: row.created_at,
    liked: likedIds ? likedIds.has(row.id) : false,
    author: {
      ...authorOf(row),
      isFollowing: followingIds ? followingIds.has(row.user_id) : false,
    },
  };
}

/** 一次查询拿到当前页视频的「是否点赞」「是否已关注作者」 */
export async function videoViewerState(rows, viewerId) {
  const likedIds = new Set();
  const followingIds = new Set();
  if (!viewerId || rows.length === 0) return { likedIds, followingIds };

  const ids = [...new Set(rows.map((row) => row.id))];
  const authorIds = [...new Set(rows.map((row) => row.user_id))];
  const idMarks = ids.map(() => '?').join(',');
  const authorMarks = authorIds.map(() => '?').join(',');

  const likes = await db
    .prepare(`SELECT video_id FROM likes WHERE user_id = ? AND video_id IN (${idMarks})`)
    .all(viewerId, ...ids);
  for (const row of likes) {
    likedIds.add(row.video_id);
  }

  const follows = await db
    .prepare(
      `SELECT following_id FROM follows WHERE follower_id = ? AND following_id IN (${authorMarks})`,
    )
    .all(viewerId, ...authorIds);
  for (const row of follows) {
    followingIds.add(row.following_id);
  }

  return { likedIds, followingIds };
}

export const COMMENT_SELECT = `
  SELECT c.*, u.username AS author_username, u.nickname AS author_nickname,
         u.avatar_url AS author_avatar
  FROM comments c
  JOIN users u ON u.id = c.user_id
`;

export function serializeComment(row, { viewerId, videoOwnerId } = {}) {
  return {
    id: row.id,
    content: row.content,
    likeCount: Number(row.like_count || 0),
    createdAt: row.created_at,
    isMine: Boolean(viewerId && row.user_id === viewerId),
    canDelete: Boolean(viewerId && (row.user_id === viewerId || videoOwnerId === viewerId)),
    author: {
      id: row.user_id,
      username: row.author_username,
      nickname: row.author_nickname,
      avatarUrl: row.author_avatar || null,
    },
  };
}
