import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { config } from './config.js';
import { closeDb, db, initDb, nowIso, resetDatabase } from './database/index.js';
import { hashPassword } from './auth.js';
import { generateAvatar, generateCover } from './cover.js';

const DEMO_PASSWORD = 'demo1234';

const USERS = [
  { username: 'aze', nickname: '阿泽', bio: '用镜头记录城市和夜晚 🌃', seed: 'aze-night' },
  { username: 'xiaoman', nickname: '小满', bio: '今天也要好好吃饭 🍚', seed: 'xiaoman-food' },
  { username: 'lulu', nickname: '鹿鹿', bio: '旅行 | 摄影 | 咖啡 ☕️', seed: 'lulu-travel' },
  { username: 'shanju', nickname: '山居厨房', bio: '一人食也要认真 🍳', seed: 'shanju-kitchen' },
];

const VIDEOS = [
  {
    file: 'demo1.mp4',
    remote: 'https://www.w3schools.com/html/mov_bbb.mp4',
    title: '下班后的快乐，就是这么简单 🐰',
    description: '把今天的疲惫留在路上，回家只负责开心。#日常 #治愈',
    user: 'aze',
    duration: 10,
    counts: { view: 12800, like: 2361, share: 89 },
  },
  {
    file: 'demo2.mp4',
    remote: 'https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4',
    title: '路边的花开了，春天真的来了 🌸',
    description: '随手一拍，都是春天写给人间的信。#春天 #随手拍',
    user: 'xiaoman',
    duration: 14,
    counts: { view: 8300, like: 1204, share: 45 },
  },
  {
    file: 'demo3.mp4',
    remote: 'https://media.w3.org/2010/05/sintel/trailer.mp4',
    title: '把周末交给一场说走就走的旅行 ✈️',
    description: '目的地不重要，出发本身就很浪漫。#旅行 #在路上',
    user: 'lulu',
    duration: 52,
    counts: { view: 21000, like: 4320, share: 210 },
  },
  {
    file: 'demo4.mp4',
    remote: 'https://media.w3.org/2010/05/bunny/trailer.mp4',
    title: '一个人也要好好吃饭 🍳',
    description: '认真做饭的人，运气都不会太差。#一人食 #厨房',
    user: 'shanju',
    duration: 33,
    counts: { view: 5600, like: 880, share: 32 },
  },
];

const COMMENTS = [
  { video: 0, user: 'xiaoman', content: '这也太治愈了吧，循环第三遍 🥹', likes: 12 },
  { video: 0, user: 'lulu', content: '求 BGM！', likes: 3 },
  { video: 1, user: 'aze', content: '春天真的来了，周末去走走 🌸', likes: 5 },
  { video: 2, user: 'shanju', content: '已加入旅行清单', likes: 8 },
  { video: 3, user: 'xiaoman', content: '一个人也要好好吃饭，赞同！', likes: 6 },
];

export async function seedDatabase({ force = false } = {}) {
  await initDb();

  const existingRow = await db.prepare('SELECT COUNT(*) AS c FROM users').get();
  const existing = Number(existingRow.c);
  if (existing > 0) {
    if (!force) return { seeded: false, users: existing };
    await resetDatabase();
  }

  const userIds = new Map();
  const insertUser = db.prepare(
    `INSERT INTO users (username, password_hash, nickname, avatar_url, bio, created_at)
     VALUES (?, ?, ?, ?, ?, ?) RETURNING id`,
  );

  for (const [index, user] of USERS.entries()) {
    const avatarName = `u${index + 1}-seed.png`;
    generateAvatar(`${user.seed}:${user.username}`, path.join(config.mediaDir, 'avatars', avatarName));
    const createdAt = new Date(Date.now() - (30 - index) * 86400000).toISOString();
    const info = await insertUser.run(
      user.username,
      hashPassword(DEMO_PASSWORD),
      user.nickname,
      `/media/avatars/${avatarName}`,
      user.bio,
      createdAt,
    );
    userIds.set(user.username, Number(info.lastInsertRowid));
  }

  const insertVideo = db.prepare(
    `INSERT INTO videos
       (user_id, title, description, video_url, cover_url, duration, view_count, like_count,
        comment_count, share_count, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, 0, ?, ?) RETURNING id`,
  );
  const videoIds = [];

  for (const [index, video] of VIDEOS.entries()) {
    const localPath = path.join(config.mediaDir, 'seed', video.file);
    const hasLocal = fs.existsSync(localPath);
    const videoUrl = hasLocal ? `/media/seed/${video.file}` : video.remote;
    const createdAt = new Date(Date.now() - (index + 1) * 5 * 3600000).toISOString();
    const info = await insertVideo.run(
      userIds.get(video.user),
      video.title,
      video.description,
      videoUrl,
      null,
      video.duration,
      video.counts.view,
      video.counts.like,
      video.counts.share,
      createdAt,
    );
    const id = Number(info.lastInsertRowid);
    videoIds.push(id);

    const coverName = `v${id}.png`;
    generateCover(`video:${id}:${video.title}`, path.join(config.mediaDir, 'covers', coverName));
    await db
      .prepare('UPDATE videos SET cover_url = ? WHERE id = ?')
      .run(`/media/covers/${coverName}`, id);
  }

  const insertComment = db.prepare(
    `INSERT INTO comments (video_id, user_id, content, like_count, created_at)
     VALUES (?, ?, ?, ?, ?)`,
  );
  for (const [index, comment] of COMMENTS.entries()) {
    await insertComment.run(
      videoIds[comment.video],
      userIds.get(comment.user),
      comment.content,
      comment.likes,
      new Date(Date.now() - (index + 1) * 1800000).toISOString(),
    );
  }
  await db.exec(`
    UPDATE videos
    SET comment_count = (SELECT COUNT(*) FROM comments WHERE comments.video_id = videos.id);
  `);

  const insertLike = db.prepare(
    `INSERT INTO likes (user_id, video_id, created_at)
     VALUES (?, ?, ?) ON CONFLICT DO NOTHING`,
  );
  await insertLike.run(userIds.get('aze'), videoIds[1], nowIso());
  await insertLike.run(userIds.get('xiaoman'), videoIds[0], nowIso());
  await insertLike.run(userIds.get('lulu'), videoIds[0], nowIso());
  await insertLike.run(userIds.get('shanju'), videoIds[2], nowIso());

  const insertFollow = db.prepare(
    `INSERT INTO follows (follower_id, following_id, created_at)
     VALUES (?, ?, ?) ON CONFLICT DO NOTHING`,
  );
  const followPairs = [
    ['aze', 'xiaoman'],
    ['aze', 'lulu'],
    ['xiaoman', 'aze'],
    ['lulu', 'aze'],
    ['lulu', 'shanju'],
    ['shanju', 'aze'],
    ['shanju', 'xiaoman'],
  ];
  for (const [follower, following] of followPairs) {
    await insertFollow.run(userIds.get(follower), userIds.get(following), nowIso());
  }

  const insertView = db.prepare(
    `INSERT INTO video_views (user_id, video_id, viewed_at) VALUES (?, ?, ?)
     ON CONFLICT(user_id, video_id) DO UPDATE SET viewed_at = excluded.viewed_at`,
  );
  await insertView.run(userIds.get('aze'), videoIds[1], nowIso());
  await insertView.run(userIds.get('aze'), videoIds[2], nowIso());

  return { seeded: true, users: USERS.length, videos: VIDEOS.length, password: DEMO_PASSWORD };
}

const isCli = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isCli) {
  const force = process.argv.includes('--force');
  try {
    await initDb();
    const result = await seedDatabase({ force });
    console.log('[seed]', result);
    console.log(
      `[seed] 演示账号：${USERS.map((user) => user.username).join(' / ')}，密码均为 ${DEMO_PASSWORD}`,
    );
    console.log(`[seed] 数据库驱动：${config.dbDriver}`);
  } catch (error) {
    console.error('[seed] 失败:', error);
    process.exitCode = 1;
  } finally {
    await closeDb();
  }
}
