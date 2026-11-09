# 抖影 DouYing

一个**能真正跑起来**的抖音风格竖屏短视频 MVP：Flutter 双端 App + Node.js 后端，并提供独立的 ASP.NET Core C# 后端实现；支持 SQLite / PostgreSQL 与本地媒体存储。

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
├── server/                # Node.js + Express + SQLite / PostgreSQL（原后端）
│   ├── src/database/      # 数据库驱动（SQLite / PostgreSQL）
│   ├── scripts/smoke-test.mjs
│   └── media/seed/        # 4 条演示视频（离线可用）
├── server-dotnet/         # ASP.NET Core + C#（独立后端项目）
├── app/                   # Flutter 客户端（Android / iOS / Web）
│   └── lib/
└── tools/
    └── chrome-download-manager/ # Chrome 下载管理扩展（MV3）
```

## 快速启动

### 环境

- Flutter 3.41+ / Dart 3.11+
- Node.js 22.9+（推荐 24；`npm start` 会自动加载 `.env`）
- .NET 10 SDK（仅使用 C# 后端时需要）
- 数据库：Node.js 后端默认使用内置 `node:sqlite`；C# 后端默认使用 SQLite；两者均可选 PostgreSQL 14+
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

### C# 后端（独立项目）

`server-dotnet/` 是与 Node.js 后端并列的完整 ASP.NET Core 实现，保留相同的 API 契约，可直接替换客户端所连接的服务。原 `server/` 项目保持不变。

```powershell
cd server-dotnet
dotnet run
```

默认监听 `http://localhost:8080`，使用独立 SQLite 数据库 `server-dotnet/data/douying.db` 并自动初始化演示账号。演示视频复用仓库中的 `server/media/seed/`；新上传的媒体和生成封面保存在 `server-dotnet/media/`。

可复制 `server-dotnet/.env.example` 为 `server-dotnet/.env` 后配置 `JWT_SECRET`、`PORT` 或数据库。PostgreSQL 可设置 `DB_DRIVER=postgres` 与 `DATABASE_URL`；也支持 `PGHOST` / `PGPORT` / `PGUSER` / `PGPASSWORD` / `PGDATABASE` / `PGSSL`。SQLite 可用 `DB_PATH` 指定路径。

两种后端不能同时占用同一个端口；如需并行运行，可为其中一个配置不同的 `PORT`。

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

# 也可将同一组接口测试用于 C# 后端（需先启动 server-dotnet）
cd ..
$env:BASE_URL="http://127.0.0.1:8080"
node server\scripts\smoke-test.mjs

# 客户端：静态检查 + 单元/Widget 测试
cd app
flutter analyze
flutter test

# 可选：Web 构建验证
flutter build web
```

## GitHub Actions 打包

在 GitHub 仓库的 **Actions** 页面可分别运行以下工作流。它们也会在推送到 `main` 或创建 Pull Request 时自动运行；构建完成后，从对应运行记录的 **Artifacts** 下载产物。

| 工作流 | 产物 |
| --- | --- |
| `Build Android` | `android-packages`：Android APK 和 AAB |
| `Build Windows` | `windows-x64-package`：Windows x64 ZIP，解压后运行 `douying.exe` |
| `Build Web` | `web-package`：可部署的 Web 静态文件 ZIP |

工作流使用 Flutter 3.41.4。Android 当前沿用项目 Gradle 中的 debug 签名配置，APK 可用于测试安装；AAB 不能直接发布到应用商店，正式发布前需配置发布签名。

发布 Web 前，在仓库 **Settings → Secrets and variables → Actions → Variables** 中设置 `API_BASE_URL` 为可访问的后端 API 地址（例如 `https://api.example.com`）。未设置时，Web 构建仍可完成，但会使用客户端默认地址。

当前仓库还没有 iOS 和鸿蒙（OpenHarmony）平台工程，因此工作流暂不生成 iOS 或鸿蒙安装包。Windows 工作流会在 CI 中生成标准 Flutter Windows runner；如需本地开发 Windows 版本，可在 `app` 目录执行 `flutter create --platforms=windows --project-name douying .`。

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
| 后端 | Node.js 24 / Express（`server/`）；C# / ASP.NET Core 10（`server-dotnet/`） |
| 数据库 | SQLite（默认，WAL）/ PostgreSQL 14+ |
| 媒体 | 本地磁盘 + Express / ASP.NET Core 静态服务（Range / 206） |

## 设计文档

- [产品与技术设计方案](docs/DESIGN.md)：功能范围、页面结构、数据模型、推荐策略、安全合规、开发计划
- [API 契约](docs/API.md)：全部接口、字段、错误码与示例

## 已知限制

- 推荐是「热度 + 时间衰减」，非个性化；已记录播放表，方便后续做召回与去重
- 未接入转码（FFmpeg/HLS）与内容审核，演示视频为横屏裁切展示
- SQLite 单机、媒体本地存储，适合 0→1 验证；需要更高并发时可切到 PostgreSQL，媒体仍建议接对象存储
- 消息中心、收藏、私信、拍摄剪辑为 P1/P2 规划
