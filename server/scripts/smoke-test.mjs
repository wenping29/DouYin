import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const base = process.env.BASE_URL || 'http://127.0.0.1:8080';
const here = path.dirname(fileURLToPath(import.meta.url));
let passed = 0;

function check(name, condition, detail = '') {
  if (!condition) {
    throw new Error(`✗ ${name} ${detail}`);
  }
  passed += 1;
  console.log(`✓ ${name}`);
}

async function api(pathname, { method = 'GET', token, body, form } = {}) {
  const headers = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  let payload;
  if (form) {
    payload = form;
  } else if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
    payload = JSON.stringify(body);
  }
  const res = await fetch(`${base}${pathname}`, { method, headers, body: payload });
  const text = await res.text();
  let json = null;
  try {
    json = text ? JSON.parse(text) : null;
  } catch {
    json = { raw: text };
  }
  return { status: res.status, json, headers: res.headers };
}

const health = await api('/api/health');
check('健康检查', health.status === 200 && health.json?.ok === true);

const login = await api('/api/auth/login', {
  method: 'POST',
  body: { username: 'aze', password: 'demo1234' },
});
check('演示账号登录', login.status === 200 && Boolean(login.json?.token));
const token = login.json.token;

const me = await api('/api/me', { token });
check('获取当前用户', me.status === 200 && me.json.user.username === 'aze');

const feed = await api('/api/feed?tab=recommend&limit=3', { token });
check('推荐流返回数据', feed.status === 200 && feed.json.items.length > 0);

const following = await api('/api/feed?tab=following&limit=3', { token });
check('关注流返回数据', following.status === 200 && following.json.items.length > 0);

const videoId = feed.json.items[0].id;
const video = await api(`/api/videos/${videoId}`, { token });
check('视频详情', video.status === 200 && video.json.video.id === videoId);

const liked = await api(`/api/videos/${videoId}/like`, { method: 'POST', token });
check('点赞', liked.status === 200 && liked.json.liked === true);
const unliked = await api(`/api/videos/${videoId}/like`, { method: 'DELETE', token });
check('取消点赞', unliked.status === 200 && unliked.json.liked === false);

const comment = await api(`/api/videos/${videoId}/comments`, {
  method: 'POST',
  token,
  body: { content: 'smoke test 评论' },
});
check('发表评论', comment.status === 201 && Boolean(comment.json.comment.id));
const comments = await api(`/api/videos/${videoId}/comments`);
check('评论列表', comments.status === 200 && comments.json.total >= 1);
const deletedComment = await api(`/api/comments/${comment.json.comment.id}`, {
  method: 'DELETE',
  token,
});
check('删除自己的评论', deletedComment.status === 200);

const search = await api('/api/search?q=%E6%98%A5%E5%A4%A9');
check('搜索视频/用户', search.status === 200 && Array.isArray(search.json.videos));

const suggested = await api('/api/users/suggested', { token });
check('推荐关注', suggested.status === 200 && Array.isArray(suggested.json.items));

/* ---------- 上传 → 播放 → 删除 ---------- */
const samplePath = path.join(here, '..', 'media', 'seed', 'demo1.mp4');
check('演示视频文件存在', fs.existsSync(samplePath), samplePath);
const form = new FormData();
form.append('video', new Blob([fs.readFileSync(samplePath)], { type: 'video/mp4' }), 'smoke.mp4');
form.append('title', 'Smoke Test 上传视频');
form.append('description', '#smoke');
form.append('duration', '10');

const upload = await api('/api/videos', { method: 'POST', token, form });
check('上传视频', upload.status === 201 && Boolean(upload.json.video.id));
const uploaded = upload.json.video;
check('自动生成封面', Boolean(uploaded.coverUrl));

const media = await fetch(`${base}${uploaded.videoUrl}`, {
  headers: { Range: 'bytes=0-1023' },
});
check('媒体 Range 播放', media.status === 206 && Number(media.headers.get('content-length')) === 1024);
await media.arrayBuffer();

const viewed = await api(`/api/videos/${uploaded.id}/view`, { method: 'POST', token });
check('播放计数', viewed.status === 200 && viewed.json.viewCount >= 1);
const shared = await api(`/api/videos/${uploaded.id}/share`, { method: 'POST' });
check('分享计数', shared.status === 200 && shared.json.shareCount >= 1);

const removed = await api(`/api/videos/${uploaded.id}`, { method: 'DELETE', token });
check('删除自己的视频', removed.status === 200 && removed.json.ok === true);

/* ---------- 注册 / 关注 ---------- */
const username = `smoke_${Date.now().toString(36)}`;
const registered = await api('/api/auth/register', {
  method: 'POST',
  body: { username, password: 'demo1234', nickname: 'Smoke' },
});
check('注册新用户', registered.status === 201 && registered.json.user.username === username);
const newToken = registered.json.token;

const follow = await api('/api/users/1/follow', { method: 'POST', token: newToken });
check('关注用户', follow.status === 200 && follow.json.following === true);
const unfollow = await api('/api/users/1/follow', { method: 'DELETE', token: newToken });
check('取消关注', unfollow.status === 200 && unfollow.json.following === false);

const unauthorized = await api('/api/videos', { method: 'POST' });
check('未登录发布被拦截', unauthorized.status === 401);

console.log(`\n全部通过：${passed} 项检查`);
