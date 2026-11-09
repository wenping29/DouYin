# 抖影（DouYing）产品与技术设计方案

> 一句话：一个面向年轻用户的竖屏短视频社区 —— 打开即刷、双击点赞、三步发布、关注即追更。
>
> 本文档对应仓库中**可运行的 MVP 骨架**：Flutter 双端 App + Node.js 真实后端 + SQLite / PostgreSQL + 本地媒体存储。

---

## 1. 产品目标

| 目标 | 衡量方式 |
| --- | --- |
| 打开 5 秒内看到第一条视频 | 冷启动到首帧时间 |
| 单条视频互动路径 ≤ 1 次点击 | 点赞 / 评论 / 关注转化率 |
| 发布一条视频 ≤ 3 步 | 选视频 → 填标题 → 发布 |
| 形成「消费 → 互动 → 关注 → 再消费」闭环 | 次日留存、人均播放条数 |

**MVP 非目标**：直播、电商橱窗、私信 IM、复杂推荐系统、内容审核后台、多机房部署。

---

## 2. 目标用户与核心场景

| 角色 | 场景 | 关键诉求 |
| --- | --- | --- |
| 内容消费者 | 通勤、睡前、碎片时间刷视频 | 打开即播、滑动流畅、不卡顿 |
| 轻创作者 | 记录生活、展示才艺 | 上传简单、有人看、有互动 |
| 社交用户 | 关注喜欢的人、看朋友动态 | 关注流、评论、主页 |
| 潜水用户 | 只看不互动 | 低打扰、少登录门槛（后续支持游客模式） |

---

## 3. 功能范围

### P0（本次骨架已实现）

- 账号：注册、登录、JWT 鉴权、本地持久化登录态
- 视频流：推荐流、关注流、分页加载、上下滑切换、自动播放/循环
- 播放：暂停/继续、静音、进度条、错误兜底、封面图
- 互动：点赞/取消、评论列表、发表评论、删除自己的评论、分享计数、播放计数
- 社交：关注/取关、作者主页、粉丝/关注/作品数
- 发布：选择视频与封面、填写标题/描述、上传进度、服务端封面兜底生成
- 个人：我的主页、作品/喜欢双 Tab、编辑昵称/简介/头像、退出登录
- 搜索：视频、用户，空态推荐用户

### P1（下一阶段）

- 消息中心：点赞/评论/关注通知、未读数
- 收藏、转发到会话、@ 与话题页
- 草稿箱、拍摄（相机）、剪辑
- 内容审核（机审 + 人审）、举报、拉黑
- 游客浏览 + 登录拦截

### P2（产品化）

- 直播、电商、私信
- 多路召回 + 排序模型、实时特征
- 对象存储 + CDN + HLS 转码
- 多端（Web 管理后台、小程序）

---

## 4. 信息架构

```
底部导航
├── 首页 Home
│   ├── 顶部：关注 / 推荐
│   ├── 全屏竖滑视频流
│   ├── 评论半屏弹层
│   └── 作者主页 / 视频详情
├── 发现 Discover
│   ├── 搜索框
│   ├── 视频结果（九宫格）
│   └── 用户结果 / 推荐关注
├── ＋ 发布（浮层页面）
│   ├── 选择视频
│   ├── 选择封面
│   └── 标题、描述、上传
├── 消息 Messages（P1 占位）
└── 我 Profile
    ├── 头像 / 昵称 / 简介 / 统计
    ├── 作品 / 喜欢
    ├── 编辑资料
    └── 退出登录
```

---

## 5. 核心流程

### 5.1 首次启动

```
启动 → 读取本地 token
  ├─ 有且有效 → 拉取 /api/me → 首页推荐流
  └─ 无/失效 → 登录页（演示账号一键填入）
```

### 5.2 刷视频

```
进入推荐流 → 请求 /api/feed?tab=recommend&limit=10
→ 首条自动播放（静音策略可配置）
→ 上下滑：暂停上一条、播放当前条、预加载下一条
→ 滑到倒数第 3 条：加载下一页并追加
→ 双击：点赞 + 心形动画；单击：暂停/继续
```

### 5.3 发布

```
点击 ＋ → 选择视频（相册/文件）→ 可选封面 → 标题/描述
→ 上传（进度条）→ 服务端校验 + 生成封面兜底
→ 返回首页/个人页，作品列表刷新
```

### 5.4 互动闭环

```
看视频 → 点赞/评论 → 作者收到通知（P1）→ 作者回访主页
→ 关注 → 关注流出现新作品 → 再次消费
```

---

## 6. 交互与视觉规范

| 项 | 规范 |
| --- | --- |
| 底色 | 纯黑 `#000000`，播放器区域无留白 |
| 主色 | 抖音红 `#FE2C55`（点赞、发布、强调） |
| 辅色 | 青色 `#25F4EE`（少量高光/渐变） |
| 文字 | 主白 `#FFFFFF`，次白 65%，弱化 45% |
| 标题 | 15–16sp，Semibold，最多 2 行 |
| 元信息 | 12sp，白色 60% |
| 右侧操作栏 | 宽 56，距右 8，图标 32，点赞后变红并弹跳 |
| 底部信息 | 距底 24 + 安全区，作者名、标题、音乐跑马灯 |
| 手势 | 上下滑切换；单击暂停；双击点赞；长按（P1 不感兴趣） |
| 动效 | 双击心形 600ms；点赞数字滚动；评论弹层 250ms |
| 状态 | 加载用骨架/封面；失败显示重试；空态给引导文案 |
| 无障碍 | 图标带语义标签；文字对比度 ≥ 4.5:1；支持系统字体缩放 |

---

## 7. 技术架构

```
┌────────────────────────────┐
│        Flutter App         │
│  UI / Riverpod? → Provider │
│  Dio REST / video_player   │
└─────────────┬──────────────┘
              │ HTTP (JSON / multipart)
┌─────────────▼──────────────┐
│      Node.js + Express     │
│  auth / feed / video /     │
│  comment / search / media  │
└──────┬──────────────┬──────┘
       │              │
┌──────▼────────────┐  ┌────▼─────────────┐
│ SQLite / Postgres │  │ 本地 media 目录   │
│ 业务数据           │  │ mp4 / cover / 头像 │
└───────────────────┘  └──────────────────┘
```

### 选型理由

| 层 | 选型 | 理由 |
| --- | --- | --- |
| 客户端 | Flutter 3.41 | 一套代码双端；video_player、image_picker 生态成熟 |
| 状态 | Provider + ChangeNotifier | MVP 足够轻，不引入复杂生成式状态管理 |
| 网络 | Dio | 拦截器、上传进度、表单支持好 |
| 后端 | Node 24 + Express | 启动快、依赖少；同一套业务代码可切换 SQLite / PostgreSQL |
| 数据库 | SQLite（默认）/ PostgreSQL | 本地开发零配置；需要并发或部署时用 `DB_DRIVER=postgres` 切换 |
| 存储 | 本地磁盘 + Express 静态服务 | 无需对象存储即可跑通；静态服务原生支持 Range，可拖动进度 |
| 鉴权 | JWT + scrypt | 无状态、依赖少；密码加盐哈希 |

### 客户端分层

```
lib/
├── core/        # ApiClient、AppState、主题、格式化工具
├── models/      # User / Video / Comment
├── pages/       # auth / home / comments / upload / profile / search
└── widgets/     # 头像、封面、空态等复用组件
```

- **AppState**：token、当前用户、数据版本号（发布后通知主页刷新）
- **ApiClient**：统一 baseUrl、鉴权头、错误转换、URL 绝对化
- **页面**：只依赖 ApiClient/AppState，不直接拼 HTTP

### 后端模块

```
src/
├── index.js        # 应用装配、静态媒体、错误处理
├── config.js       # 端口、密钥、目录
├── db.js           # Schema 与连接
├── auth.js         # 密码哈希、JWT、鉴权中间件
├── serializers.js  # 用户/视频/评论出参（含 liked/isFollowing）
├── cover.js        # 纯 JS 生成渐变封面/头像 PNG
├── seed.js         # 演示数据（4 个账号 + 4 条视频）
└── routes/         # auth / users / videos / feed / search
```

---

## 8. 数据模型

```
users
  id PK, username UNIQUE, password_hash, nickname, avatar_url,
  bio, created_at

videos
  id PK, user_id FK→users, title, description, video_url, cover_url,
  duration, width, height, view_count, like_count, comment_count,
  share_count, status, created_at
  INDEX(user_id, created_at), INDEX(created_at)

likes
  user_id, video_id, created_at
  PRIMARY KEY(user_id, video_id)
  INDEX(video_id)

comments
  id PK, video_id, user_id, content, like_count, created_at
  INDEX(video_id, created_at)

follows
  follower_id, following_id, created_at
  PRIMARY KEY(follower_id, following_id)
  INDEX(following_id)

video_views
  user_id, video_id, viewed_at
  PRIMARY KEY(user_id, video_id)
  INDEX(video_id)
```

**计数策略（MVP）**：`like_count / comment_count / share_count / view_count` 冗余在 `videos` 上，写操作在同一事务内更新，读路径不聚合。规模化后改为计数服务 + 异步校准。

**文件命名**：视频 `{timestamp}-{random}.mp4`，封面 `v{id}.png`，头像 `u{id}-{random}.png`；数据库只存相对 URL（`/media/...`），客户端按 baseUrl 拼接。

---

## 9. 推荐流策略（MVP）

关注流：`follows` 内作者的视频，按发布时间倒序。

推荐流：热门分 + 时间衰减，服务端在候选集（最近 500 条已发布）上计算：

```
score = (like*3 + comment*5 + share*4 + view*0.1 + 2)
        / pow(ageHours + 2, 0.55)
```

- 每次请求取候选、计算、排序、按 offset 分页
- 自身作品不进入推荐流
- 已播放记录写入 `video_views`，为后续「去重 / 负反馈 / 召回特征」预留
- P1 演进：多路召回（关注、热门、同城、标签、协同过滤）→ 粗排 → 精排 → 多样性重排

**取舍**：MVP 用确定性排序，保证分页稳定；真实推荐需要实时特征与在线实验，不在骨架范围内。

---

## 10. API 摘要

完整字段与示例见 [`docs/API.md`](./API.md)。

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| POST | `/api/auth/register` | 注册并返回 token |
| POST | `/api/auth/login` | 登录 |
| GET | `/api/me` | 当前用户 |
| PATCH | `/api/me` | 修改昵称/简介 |
| POST | `/api/me/avatar` | 上传头像 |
| GET | `/api/users/:id` | 用户主页信息 |
| GET | `/api/users/:id/videos` | 作品/喜欢列表 |
| POST/DELETE | `/api/users/:id/follow` | 关注/取关 |
| GET | `/api/users/suggested` | 推荐关注 |
| GET | `/api/feed` | 推荐流/关注流 |
| GET | `/api/videos/:id` | 视频详情 |
| POST | `/api/videos` | 上传视频（multipart） |
| DELETE | `/api/videos/:id` | 删除自己的视频 |
| POST/DELETE | `/api/videos/:id/like` | 点赞/取消 |
| POST | `/api/videos/:id/view` | 播放计数 |
| POST | `/api/videos/:id/share` | 分享计数 |
| GET/POST | `/api/videos/:id/comments` | 评论列表/发表 |
| DELETE | `/api/comments/:id` | 删除评论 |
| GET | `/api/search` | 搜索视频/用户 |
| GET | `/api/health` | 健康检查 |

---

## 11. 媒体链路

**上传**

1. 客户端 `image_picker` 选视频/封面，Dio `FormData` 上传（显示进度）
2. 服务端 `multer` 落盘，限制 200MB、仅 `video/*`；封面仅 `image/*`
3. 无封面时服务端用纯 JS（zlib + PNG 编码）生成品牌渐变封面，避免空图
4. 写入 `videos`，返回完整视频对象

**播放**

- `express.static` 自带 `Range` / `206`，`video_player` 可拖动进度、循环播放
- 客户端只保留当前页播放器；切页即暂停/释放，控制内存
- P1：FFmpeg 转码（H.264/AAC、720p、HLS）、封面抽帧、对象存储 + CDN

**限制**：当前样例视频为横屏，App 用 `BoxFit.cover` 裁切为竖屏展示；真实产品应引导竖屏拍摄或在转码时裁切。

---

## 12. 安全与合规

- 密码：`scrypt` + 随机盐，恒定时间比较；不回传任何密码字段
- 鉴权：`Authorization: Bearer <JWT>`，默认 30 天；密钥必须通过 `JWT_SECRET` 覆盖
- SQL：全部预编译参数，禁止拼字符串
- 上传：白名单 MIME/扩展名、大小限制、文件名随机化、静态目录不执行脚本
- 越权：删除视频/评论校验 owner；关注不能对自己
- CORS：开发期放开，生产按域名白名单
- **合规红线**：真实上线必须补充实名/青少年模式、内容审核、版权校验、隐私政策、举报与删除机制；当前仅用于本地学习演示，默认 HTTP + 开发密钥 + 无审核。

---

## 13. 性能与扩展

| 点 | MVP 做法 | 规模化 |
| --- | --- | --- |
| 首页 | 每页 10 条、滑到倒数第 3 条预取 | 预取 2 页、视频预加载 |
| 播放器 | 仅当前页持有 controller | 双播放器池 + 首帧预渲染 |
| 图片 | `cached_network_image` | 图片 CDN + WebP/AVIF |
| 数据库 | SQLite WAL / PostgreSQL + 索引 | PostgreSQL 读写分离 + Redis 计数 |
| 媒体 | 本地磁盘 + Range | OSS + CDN + HLS 多码率 |
| 接口 | 客户端分页 | 网关限流、缓存、可观测性 |

---

## 14. 目录结构

```
DouYin/
├── README.md
├── docs/
│   ├── DESIGN.md          # 本文件
│   └── API.md             # 接口契约
├── server/                # Node.js 后端
│   ├── package.json
│   ├── src/
│   ├── scripts/smoke-test.mjs
│   └── media/             # 运行时生成/上传（含 4 条演示视频）
└── app/                   # Flutter 客户端
    ├── pubspec.yaml
    └── lib/
```

---

## 15. 开发计划

| 阶段 | 内容 | 验收 |
| --- | --- | --- |
| Day 1–3（本骨架） | 工程搭建、账号、推荐/关注流、播放、点赞评论、发布、主页、搜索 | 双端可跑通全链路；接口 smoke test 通过 |
| Week 2 | 消息通知、收藏、举报、审核接入、游客模式 | 互动有通知；违规内容可下架 |
| Week 3 | 拍摄/剪辑、HLS 转码、封面抽帧、CDN | 上传后 1 分钟内可播；首帧 < 1s |
| Week 4 | 推荐多路召回、埋点、A/B、压测 | 人均播放时长提升；P95 API < 200ms |

---

## 16. MVP 验收清单

- [x] 注册/登录，重启 App 保持登录
- [x] 推荐流与关注流可切换、可无限下滑
- [x] 视频自动播放/循环/暂停/静音/进度
- [x] 点赞、评论、分享计数即时更新
- [x] 关注/取关，主页数据正确
- [x] 上传视频后出现在个人主页与推荐流
- [x] 搜索视频与用户
- [x] 编辑昵称/简介/头像
- [x] 后端 Range 播放、上传大小限制、越权删除拦截
- [x] `flutter analyze` / `flutter test` / 后端 smoke test 通过

---

## 17. 已知限制与下一步决策

1. **推荐很朴素**：热门 + 时间衰减，没有个性化。下一步先补埋点与多路召回，不要过早训练模型。
2. **无转码**：任意 mp4 直接播放，部分编码可能不兼容。上线前必须接 FFmpeg（H.264/AAC）。
3. **无审核**：公开部署前必须接内容安全服务，否则合规风险极高。
4. **数据库可切换**：默认 SQLite 适合 0→1 验证；已支持 `DB_DRIVER=postgres` 切到 PostgreSQL，更高并发再引入 Redis 计数与读写分离。
5. **本地存储**：多实例部署必须换对象存储，媒体 URL 改为 CDN 域名。
6. **密钥与 HTTP**：`JWT_SECRET`、HTTPS、CORS 白名单是上线硬门槛。
