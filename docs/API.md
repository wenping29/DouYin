# 抖影 API 契约（MVP）

- Base URL：`http://localhost:8080`
- 所有响应为 `application/json; charset=utf-8`
- 需鉴权的接口携带：`Authorization: Bearer <token>`
- 分页统一使用 `cursor`（偏移量字符串）与 `limit`（默认 10，最大 50），返回 `nextCursor`（无更多时为 `null`）
- 媒体字段返回相对路径（如 `/media/videos/x.mp4`），客户端按 Base URL 拼成绝对地址

## 错误格式

```json
{
  "error": {
    "code": "INVALID_CREDENTIALS",
    "message": "用户名或密码错误"
  }
}
```

| HTTP | code 示例 | 场景 |
| --- | --- | --- |
| 400 | `VALIDATION_ERROR` | 参数缺失/格式错误 |
| 401 | `UNAUTHORIZED` | 未登录或 token 失效 |
| 403 | `FORBIDDEN` | 越权删除 |
| 404 | `NOT_FOUND` | 资源不存在 |
| 409 | `USERNAME_TAKEN` | 用户名已存在 |
| 413 | `FILE_TOO_LARGE` | 上传超过限制 |
| 500 | `INTERNAL_ERROR` | 服务端异常 |

---

## 1. 认证

### POST `/api/auth/register`

```json
{ "username": "aze", "password": "demo1234", "nickname": "阿泽" }
```

- `username`：3–20 位字母/数字/下划线，唯一
- `password`：6–64 位
- `nickname`：可选，默认等于 username

响应 `201`：

```json
{
  "token": "eyJhbGciOi...",
  "user": {
    "id": 5,
    "username": "aze",
    "nickname": "阿泽",
    "avatarUrl": "/media/avatars/u5-xxx.png",
    "bio": "",
    "followerCount": 0,
    "followingCount": 0,
    "videoCount": 0,
    "isFollowing": false,
    "createdAt": "2026-11-09T08:00:00.000Z"
  }
}
```

### POST `/api/auth/login`

```json
{ "username": "aze", "password": "demo1234" }
```

响应 `200`：结构同注册。

---

## 2. 用户

### GET `/api/me` 🔒

响应 `200`：`{ "user": User }`

### PATCH `/api/me` 🔒

```json
{ "nickname": "新昵称", "bio": "记录生活" }
```

`nickname` 1–20 字，`bio` ≤ 80 字。响应 `{ "user": User }`。

### POST `/api/me/avatar` 🔒

`multipart/form-data`，字段名 `avatar`（image/*，≤ 5MB）。响应 `{ "user": User }`。

### GET `/api/users/:id`

可选鉴权（带 token 时返回 `isFollowing`）。响应 `{ "user": User }`。

### GET `/api/users/:id/videos`

| 参数 | 说明 |
| --- | --- |
| `type` | `posts`（默认）或 `liked`；`liked` 仅本人或公开？MVP 公开 |
| `cursor` / `limit` | 分页 |

响应：

```json
{
  "items": [Video],
  "nextCursor": "10"
}
```

### POST `/api/users/:id/follow` 🔒

响应：`{ "following": true, "followerCount": 12 }`

### DELETE `/api/users/:id/follow` 🔒

响应：`{ "following": false, "followerCount": 11 }`

### GET `/api/users/suggested?limit=10` 🔒

返回未关注、非自己的用户：`{ "items": [User] }`

---

## 3. 视频流

### GET `/api/feed`

| 参数 | 说明 |
| --- | --- |
| `tab` | `recommend`（默认）/ `following` |
| `cursor` / `limit` | 分页 |

匿名可访问；带 token 时返回 `liked/isFollowing`，关注流需要登录。

响应：

```json
{
  "items": [Video],
  "nextCursor": "10"
}
```

### GET `/api/videos/:id`

响应 `{ "video": Video }`。

### POST `/api/videos` 🔒

`multipart/form-data`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `video` | file | 必填，video/*，≤ 200MB |
| `cover` | file | 可选，image/*，≤ 5MB |
| `title` | text | ≤ 100 字，默认「未命名作品」 |
| `description` | text | ≤ 500 字 |
| `duration` | text | 秒，可选（客户端上报） |

响应 `201`：`{ "video": Video }`

### DELETE `/api/videos/:id` 🔒

仅作者。响应 `{ "ok": true }`，同时删除媒体文件。

### POST `/api/videos/:id/like` / DELETE 🔒

响应：`{ "liked": true, "likeCount": 9 }`

### POST `/api/videos/:id/view`

匿名可访问。响应：`{ "viewCount": 101 }`

### POST `/api/videos/:id/share`

匿名可访问。响应：`{ "shareCount": 3 }`

---

## 4. 评论

### GET `/api/videos/:id/comments?cursor=0&limit=20`

响应：

```json
{
  "items": [Comment],
  "nextCursor": "20",
  "total": 3
}
```

### POST `/api/videos/:id/comments` 🔒

```json
{ "content": "太治愈了！" }
```

`content` 1–500 字。响应 `201`：`{ "comment": Comment, "commentCount": 4 }`

### DELETE `/api/comments/:id` 🔒

评论作者或视频作者可删。响应 `{ "ok": true, "commentCount": 3 }`

---

## 5. 搜索

### GET `/api/search?q=花&type=all`

| 参数 | 说明 |
| --- | --- |
| `q` | 关键词，≤ 50 字 |
| `type` | `all`（默认）/ `videos` / `users` |

响应：

```json
{
  "videos": [Video],
  "users": [User]
}
```

未命中类型返回空数组。按标题/描述/昵称/用户名模糊匹配，最多各 20 条。

---

## 6. 对象结构

### User

```json
{
  "id": 1,
  "username": "aze",
  "nickname": "阿泽",
  "avatarUrl": "/media/avatars/u1-x.png",
  "bio": "把日子过成电影",
  "followerCount": 128,
  "followingCount": 12,
  "videoCount": 3,
  "isFollowing": false,
  "createdAt": "2026-11-01T10:00:00.000Z"
}
```

### Video

```json
{
  "id": 1,
  "title": "下班后的快乐，就是这么简单 🐰",
  "description": "",
  "videoUrl": "/media/videos/demo1.mp4",
  "coverUrl": "/media/covers/v1.png",
  "duration": 10.0,
  "width": 0,
  "height": 0,
  "viewCount": 1024,
  "likeCount": 233,
  "commentCount": 12,
  "shareCount": 4,
  "createdAt": "2026-11-08T12:00:00.000Z",
  "liked": false,
  "author": {
    "id": 1,
    "username": "aze",
    "nickname": "阿泽",
    "avatarUrl": "/media/avatars/u1-x.png",
    "isFollowing": false
  }
}
```

### Comment

```json
{
  "id": 10,
  "content": "太治愈了！",
  "likeCount": 2,
  "createdAt": "2026-11-09T08:10:00.000Z",
  "isMine": false,
  "author": {
    "id": 2,
    "username": "xiaoman",
    "nickname": "小满",
    "avatarUrl": "/media/avatars/u2-x.png"
  }
}
```

---

## 7. 健康检查

### GET `/api/health`

```json
{ "ok": true, "uptime": 12.34 }
```
