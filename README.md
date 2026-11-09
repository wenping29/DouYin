# 抖影 DouYing

一个**能真正跑起来**的抖音风格竖屏短视频 MVP：Flutter 双端 App + Node.js 真实后端 + SQLite / PostgreSQL + 本地媒体存储。

- 打开即刷：推荐流 / 关注流、竖屏上下滑、自动播放与循环
- 完整互动：点赞、评论、分享、关注、播放计数
- 三步发布：选视频 → 填标题 → 上传，服务端自动生成封面
- 个人主页：作品 / 喜欢、编辑资料、头像上传
- 搜索：视频与用户
- 真实后端：JWT 鉴权、上传、Range 拖动播放、越权校验

> 仅用于学习与本地演示。上线前必须补充内容审核、实名/青少年模式、版权校验、HTTPS 与对象存储等能力。

## 目录

```
DouYin/
├── docs/
│   ├── DESIGN.md          # 产品与技术设计方案
│   └── API.md             # 完整接口契约
├── server/                # Node.js + Express + SQLite / PostgreSQL
│   ├── src/database/      # 数据库驱动（SQLite / PostgreSQL）
│   ├── scripts/smoke-test.mjs
│   └── media/seed/        # 4 条演示视频（离线可用）
└── app/                   # Flutter 客户端（Android / iOS / Web）
    └── lib/
```

## 快速启动

### 环境

- Flutter 3.41+ / Dart 3.11+
- Node.js 22.9+（推荐 24；`npm start` 会自动加载 `.env`）
- 数据库：默认使用内置 `node:sqlite`，无需安装；也可选 PostgreSQL 14+
- 无需 Docker、无需 FFmpeg

### 1. 启动后端

```bash
cd server
npm install
npm start
```

启动后会自动建库并写入演示数据：

```
抖影 API 已启动: http://localhost:8080
演示账号: aze / xiaoman / lulu / shanju，密码 demo1234
```

健康检查：<http://localhost:8080/api/health>

### 数据库：SQLite（默认）/ PostgreSQL

默认零配置使用 SQLite，数据在 `server/data/douying.db`。如果想用 PostgreSQL：

```bash
# 1. 先创建数据库（任选一种）
createdb douying
# 或在 psql 中执行：CREATE DATABASE douying;
```

```bash
# 2. 配置连接（推荐复制 .env.example 为 .env 后修改；Windows 用 copy .env.example .env）
cd server
cp .env.example .env
```

`.env` 中写入：

```dotenv
DB_DRIVER=postgres
DATABASE_URL=postgres://postgres:postgres@127.0.0.1:5432/douying
```

然后正常启动即可，服务会自动建表并写入演示数据：

```bash
npm start
```

也可以不写 `.env`，直接用环境变量启动。PowerShell：

```powershell
$env:DB_DRIVER="postgres"
$env:DATABASE_URL="postgres://postgres:postgres@127.0.0.1:5432/douying"
npm start
```

macOS / Linux：

```bash
DB_DRIVER=postgres DATABASE_URL=postgres://postgres:postgres@127.0.0.1:5432/douying npm start
```

说明：

- 只配置 `DATABASE_URL` 而不写 `DB_DRIVER`，也会自动切换到 PostgreSQL
- 也支持 `PGHOST` / `PGPORT` / `PGUSER` / `PGPASSWORD` / `PGDATABASE` 分项配置
- 云数据库需要 SSL 时加 `PGSSL=true`
- 切换数据库后重新执行 `npm run seed` 可强制写入干净的演示数据
- 客户端不需要任何改动，API 行为在两种数据库下保持一致

### 2. 启动 App

```bash
cd app
flutter pub get
flutter run
```

默认地址：

| 运行环境 | API Base URL |
| --- | --- |
| Android 模拟器 | `http://10.0.2.2:8080` |
| iOS 模拟器 / Web / 桌面 | `http://127.0.0.1:8080` |

真机调试时指定电脑局域网 IP：

```bash
flutter run --dart-define=API_BASE_URL=http://192.168.1.10:8080
```

浏览器预览：

```bash
flutter run -d chrome
```

### 3. 登录

直接点击「使用演示账号 aze / demo1234」，或使用以下任意账号：

| 用户名 | 昵称 | 密码 |
| --- | --- | --- |
| `aze` | 阿泽 | `demo1234` |
| `xiaoman` | 小满 | `demo1234` |
| `lulu` | 鹿鹿 | `demo1234` |
| `shanju` | 山居厨房 | `demo1234` |

## 验证

```bash
# 后端：24 项接口与媒体链路检查（需先启动 server）
cd server
npm run smoke

# 客户端：静态检查 + 单元/Widget 测试
cd app
flutter analyze
flutter test

# 可选：Web 构建验证
flutter build web
```

## 常用操作

```bash
# 重置演示数据（会删除所有账号/视频数据）
cd server
npm run seed
```

上传的视频保存在 `server/media/videos/`，封面在 `server/media/covers/`，头像在 `server/media/avatars/`。

## 技术栈

| 层 | 技术 |
| --- | --- |
| 客户端 | Flutter 3.41、Provider、Dio、video_player、image_picker、cached_network_image |
| 后端 | Node.js 24、Express、JSON Web Token、Multer |
| 数据库 | SQLite（默认，WAL）/ PostgreSQL 14+ |
| 媒体 | 本地磁盘 + Express 静态服务（原生 Range / 206） |

## 设计文档

- [产品与技术设计方案](docs/DESIGN.md)：功能范围、页面结构、数据模型、推荐策略、安全合规、开发计划
- [API 契约](docs/API.md)：全部接口、字段、错误码与示例

## 已知限制

- 推荐是「热度 + 时间衰减」，非个性化；已记录播放表，方便后续做召回与去重
- 未接入转码（FFmpeg/HLS）与内容审核，演示视频为横屏裁切展示
- SQLite 单机、媒体本地存储，适合 0→1 验证；需要更高并发时可切到 PostgreSQL，媒体仍建议接对象存储
- 消息中心、收藏、私信、拍摄剪辑为 P1/P2 规划
