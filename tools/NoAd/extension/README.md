# CleanView 广告拦截扩展 v0.1（可运行 MVP 骨架）

> 基于 `ad-blocker-extension-prd.md` v0.1.1 实现的 Chrome MV3 扩展。
> 目标：可本地加载、可验证核心闭环（拦截 → 白名单 → 统计 → 匿名上报）。**不是生产就绪版本**。

---

## 0. 先说清楚：PRD 与 MV3 工程现实的 4 处偏差（重要）

写代码前先对齐，避免"照着 PRD 写完发现跑不起来"。以下均已在本实现中按工程现实落地，并同步修订了权限声明文档。

| # | PRD 条款 | 工程现实 | 本实现做法 | 建议的产品文档修订 |
|---|----------|----------|------------|--------------------|
| 1 | 7.7 / 11.1「禁止静态声明 `<all_urls>`，改动态 host 授权」 | **全覆盖拦截 + content_scripts.matches 本身就需要所有站点访问权**。Chrome 商店对广告拦截类扩展允许 `<all_urls>`，但必须在权限理由中说明。完全动态授权需每站点用户确认，与"全覆盖/一键清净"定位冲突 | 静态 `host_permissions: ["<all_urls>"]` + `content_scripts.matches`，权限理由写入 `store-permissions-declaration.md` | 修订 7.7 与 11.1，接受方案 A；动态授权列为后续可选 |
| 2 | 10.1「拦截统计」 | `onRuleMatchedDebug` / `declarativeNetRequestFeedback` **仅在 unpacked(dev) 扩展可用**，商店安装版拿不到规则命中详情 | 用 `webRequest.onErrorOccurred` 捕获 `net::ERR_BLOCKED_BY_CLIENT` 近似统计。口径：**请求级拦截数**，不含元素隐藏、不含缓存命中 | 在 10.3 指标口径补充"近似值"说明与排除项 |
| 3 | 7.3「订阅规则列表 + 静默更新」 | MV3 **静态 ruleset 只能在扩展包内，运行时不能替换**；动态规则上限 30,000 条，而完整公开规则集是十万条级 | 已交付 `tools/build-rules.js` 预编译脚本：把规则列表编译成静态 ruleset（示例输出 18 条）+ cosmetic 选择器；运行时只更新 ≤ 5000 条精选动态规则 | 修订 7.3 与第十三节：区分"静态规则随发版更新"与"运行时精选动态更新" |
| 4 | 4.1「视频贴片全覆盖」 | 广告与正片常同域/同流，DNR 无法按内容区分，MV3 下能力有限 | 仅做通用请求拦截 + 广告容器隐藏，**不承诺贴片拦截率** | 在 2.3 成功指标补充排除项，靠 50 站点评测集实测 |

> 另：`Firefox` 的 MV3 支持程度与 Chrome 有差异（`updateEnabledRulesets`、DNR 上限、事件 API），Firefox 版本需单独适配，不在本骨架范围内。

---

## 1. 项目结构

```
extension/
├── manifest.json                  # MV3 配置
├── README.md                      # 本文件
├── filters/
│   └── sample-filters.txt         # 示例规则输入（ABP 语法，原创整理）
├── tools/
│   ├── build-rules.js             # 规则预编译器（零依赖 Node）
│   └── build-report.json          # 构建报告（跳过原因分布，自动生成）
├── rules/
│   └── ruleset_core.json          # 静态 DNR 规则【由脚本生成，勿手改】
├── src/
│   ├── shared.js                  # 存储 schema / 统计 / 匿名上报契约（ES module）
│   ├── background.js              # Service Worker：动态规则、更新、计数、上报
│   ├── cosmetic-selectors.js     # Cosmetic 选择器【由脚本生成，勿手改】
│   ├── content.js                  # Cosmetic Filtering（内置兜底 + 编译产物 + MutationObserver）
│   ├── content.css                # 隐藏样式
│   ├── popup.html / popup.js / popup.css    # 弹窗：开关、站点暂停、当日统计
│   └── options.html / options.js   # 设置页：白名单、规则订阅、隐私、首启告知
├── server/
│   └── stats-server.js            # 零依赖 Node 统计 mock 后端（仅 dev）
└── tests/
    └── self-test.md               # 自测清单（对应 PRD RTM 的 TC-01~TC-08）
```

---

## 2. 快速开始（dev 加载）

**前置**：Chrome / Edge ≥ 110；Node.js ≥ 18（仅统计后端需要）。

```bash
# 1) 可选：启动本地统计 mock 后端，用于验证匿名上报
node server/stats-server.js
# 输出：[CleanView] dev 统计服务已启动: http://127.0.0.1:8787
```

1. 打开 `chrome://extensions`，右上角开启 **开发者模式**。
2. 点「加载已解压的扩展程序」，选择本 `extension/` 目录。
3. 首次安装会自动打开 `options.html?welcome=1`，即 **FR-06 首启告知**（明示统计内容 + 关闭按钮）。
4. 在扩展卡片点「Service Worker」打开 devtools 控制台，观察规则重建与上报日志。
5. 打开任意含广告的页面 → 点扩展图标查看「今日拦截 / 今日隐藏元素」。

> 若加载失败，先看 `chrome://extensions` 的错误详情，常见是 JSON 语法错误或 ruleset 路径不对。

---

## 3. 规则预编译（tools/build-rules.js）

静态 ruleset 只能随扩展包发布，所以规则变更必须走固定链路：
**改 `filters/*.txt` → 跑脚本 → 重新加载扩展 → 发版**。`rules/ruleset_core.json` 与 `src/cosmetic-selectors.js` 均为生成物，**不要手工编辑**。

### 用法

```bash
# 路径相对 extension/ 根解析，任意目录运行均可；零依赖，Node >= 18

# ① 仅用内置示例规则（开发/测试，18 条）
npm run build:rules
# 等价于：node tools/build-rules.js --input filters/sample-filters.txt ...

# ② 接入真实规则源（推荐；须已确认许可证，见 3.2 节）
npm run build:full
# 等价于：node tools/fetch-rules.js  （读取 rules-sources.json，下载→拼接→编译）

# 直接调用 build-rules 的常用参数：
node tools/build-rules.js \
  --input filters/my-list.txt \
  --out-static rules/ruleset_core.json \
  --out-cosmetic src/cosmetic-selectors.js \
  --report tools/build-report.json \
  --max-static 150000 --max-cosmetic 8000 --minify true
```

> **生成物已被 `.gitignore` 忽略，不入库**（受上游许可证约束）。`rules/ruleset_core.json`、`src/cosmetic-selectors.js`、`tools/build-report.json` 均由脚本在构建期生成；仓库只保留 `filters/sample-filters.txt`（示例）与 `rules-sources.json`（源清单）。

### 语法映射

| ABP / filter 语法 | 转为 DNR |
|-------------------|----------|
| `\|\|domain^`（纯域名锚点） | `condition.requestDomains`（含子域；**DNR 不识别 `\|\|`/`^`，由 `transformPattern` 转换**） |
| `\|\|domain/path^` | `requestDomains` + `urlFilter`（`^` 近似为 `*`） |
| `*banner*`、`\|https://...\|` | `condition.urlFilter`（DNR 原生支持 `*` `\|`） |
| `/regex/$script` | `condition.regexFilter`（限量 200 条、长度 ≤ 500） |
| `$script,image,xmlhttprequest,subdocument` | `condition.resourceTypes`（subdocument→`sub_frame`，document→`main_frame`） |
| `$~script`、`$~image` | `condition.excludedResourceTypes` |
| `$third-party` / `$~third-party` | `condition.domainType: thirdParty / firstParty` |
| `$domain=a.com\|b.com` / `~domain=` | `initiatorDomains` / `excludedInitiatorDomains` |
| `$important` | `priority` 1 → 50 |
| `$match-case` | `isUrlFilterCaseSensitive: true` |
| `@@例外` | `allow`（`$document` → `allowAllRequests`），`priority=100` |
| `##.ad` / `a.com##.ad` / `~b.com##.ad` | cosmetic 选择器，带域名/排除域限定（注入为 `<style>`） |

### 保守跳过（宁可少拦，不误拦）
`$redirect` `$rewrite` `$csp` `$header` `$sitekey` `$popup`、未知选项、cosmetic 例外 `#@#`、超长 urlFilter/regex —— 整条跳过，跳过原因分布写入 `tools/build-report.json` 便于追溯。

### 裁剪与自检
- 评分（决定超限时先裁谁）：`例外(1000) > important(500) > 纯通用拦截骨干(120) > 域名限定(20) > 资源类型(10) > 正则(-30)`。**纯通用拦截规则（如 `||doubleclick.net^`）是广告拦截的命门，必须优先保留，否则知名网络会先被裁掉、覆盖率反降**（已踩坑）。
- `--max-static` 默认 30,000（取 Chrome 保证可用的下限）；接入真实源建议提到 150,000（候选约 117k，可做到网络层零裁剪）。**注意**：规则越多，产物体积越大（见 3.2 节 13MB 实测），需权衡安装包大小。
- `--max-cosmetic` 默认 8,000：真实源含数万条元素隐藏规则，全量注入会拖慢页面；领域限定优先保留，通用规则限量填充。
- `--minify`：关闭美化缩进，产物体积约减半（13MB → 约 7MB 级别），发布推荐开启。
- id 从 1 递增，**与动态白名单规则 id 段 100000+ 隔离**。
- 输出前自检：id 唯一、`action.type` 合法、`resourceTypes` 在 DNR 合法枚举内、`urlFilter` 与 `regexFilter` 恰好其一、长度达标。**任一失败即退出码 1，不产出非法 ruleset**。

### 本次示例运行结果（内置 sample-filters.txt）
```
静态 DNR 规则   : 18  -> rules/ruleset_core.json
Cosmetic 选择器 : 6   -> src/cosmetic-selectors.js
跳过 / 裁剪     : 3 条（语法跳过 3 / 静态超限 0 / 正则超限 0 / cosmetic 超限 0）
构建报告        : tools/build-report.json
```

### 接入链路
```
filters/*.txt ──build-rules.js──> rules/ruleset_core.json      → manifest 静态 ruleset
                                ├─> src/cosmetic-selectors.js  → manifest content_scripts 第 1 个注入
                                └─> tools/build-report.json     → 构建可追溯
```
> 注意：`manifest.json` 的 `content_scripts.js` 顺序必须是 `cosmetic-selectors.js` 在 `content.js` 之前，否则 `globalThis.CV_COSMETIC_SELECTORS` 读不到（content.js 有内置兜底选择器，不会崩，但编译规则不生效）。

### 3.2 规则源与许可证（rules-sources.json + fetch-rules.js）

真实规则在**构建期**由 `tools/fetch-rules.js` 拉取并编译，源清单见 `rules-sources.json`。设计原则：**不把任何上游规则内容提交进仓库**（许可证红线），生成物已被 `.gitignore` 忽略。

```bash
npm run build:full        # 读取 rules-sources.json，下载→拼接→编译
node tools/fetch-rules.js --only easylist,easylist-china   # 只取指定源
node tools/fetch-rules.js --dry-run                       # 只下载不编译（调试）
```

`rules-sources.json` 当前启用三个源（实测 2026-11-04）：
| 源 | 体积 | 作用 |
|----|------|------|
| EasyList | ~2.0 MB | 国际通用广告/跟踪 |
| EasyPrivacy | ~1.5 MB | 隐私/跟踪器（不含广告展示） |
| EasyList China | ~0.5 MB | 补充国内网络（百度/腾讯/字节/新浪/网易/阿里） |

**实测（maxStatic=150000 网络层零裁剪）：** 117,042 条静态规则 + 8,000 条 cosmetic，产物 `ruleset_core.json` ≈ 13 MB（minify 后约 7–8 MB），编译耗时 ~9s。

> ⚠️ **许可证红线（商业化前必读）**：EasyList / EasyPrivacy / EasyList China 采用 **EasyList License**（基于 CC BY-NonCommercial-NoDerivatives 4.0）——**默认禁止商业用途，须获书面许可；且禁止以 EasyList 名义发布修改版、须署名**。本工具定位「后期上架商店、测试期免费」，一旦转为收费/商业化，**必须**改用许可证更宽松的源（如 **AdGuard 中文过滤**，已在 `rules-sources.json` 中设为 `enabled:false` 备选，其许可证允许再分发与商业使用，仅需保留版权声明与署名）。所有生成物须按 `NOTICE` 添加上游许可证署名。

### 3.2.1 本地自定义规则（filters/custom.txt — 用户在配置文件内新增/修改规则）

除了在 `rules-sources.json` 里**勾选哪些上游源**，你还可以在一个本地文件里**亲手写规则**，且优先级高于上游——这就是「在配置文件内新增/修改规则」的开放入口。

- **入口文件**：`filters/custom.txt`（已入库、可版本化、带注释示例）。使用标准 Adblock Plus / filter 语法，**与上游规则源同一套**，抄上游写法即可。
- **配置声明**：`rules-sources.json` 的 `customFilterPath` 指向它；换文件名/路径时改这一行即可。
- **编译**：`fetch-rules.js` 在编译完上游后，自动把它单独编译为独立且优先级更高的 `ruleset_custom.json` + `src/cosmetic-custom.js`。也可单独跑：

  ```bash
  npm run build:custom      # 仅编译自定义规则（开发迭代快）
  npm run build:full        # 重新编译「上游 + 自定义」（推荐，保持两边一致）
  ```

- **优先级（关键）**：自定义层编译时整体 `priority + 100`，因此**你的规则说了算**：
  - 自定义 block = 101 > 上游 important = 50 > 上游 block = 1；
  - 自定义例外 `@@` = 200 > 动态白名单 allow = 100。
  - 可覆盖上游的漏拦，或用 `@@` 放行上游误杀的正常资源。
- **生效**：改完保存，到扩展管理页点「刷新 (Reload)」即可，无需重启浏览器。
- **能写什么**（详见文件内注释）：拦截域名 `||ads.x.com^`、限定资源类型 `$image,script`、整站/资源放行 `@@||x.com^$document`、元素隐藏 `##.ad-box`、域名限定 `example.com##.sponsored`、排除域 `~shop.com##.promo`、正则 `/regex/$script`。

> 规则 id 段规划（Chrome DNR 要求扩展内全局唯一）：上游静态 `ruleset_core` 1..150000 / 自定义静态 200001+ / 动态白名单 400000+ / 动态远程 410000+。自定义层与任何层级都不冲突，也不会覆盖你选项页里勾选的站点白名单语义（两者针对不同粒度）。

### 3.3 评测集与静态覆盖率（evaluation/）

评测集用于定义拦截率 baseline（PRD RTM 评审 REV-07 遗留项闭环）。完整口径见 `evaluation/README.md`。

```bash
python3 evaluation/gen-sites.py   # 生成 57 站点 / 171 样本 -> sites.json
node evaluation/run-eval.js       # 静态覆盖率（无浏览器依赖，近似 DNR 匹配）
```

**关键发现（2026-11-04）**：修复前规则覆盖率 **0%**——根因是 ABP `||host^` 被原样写入 DNR `urlFilter`（DNR 不识别 `||`/`^` 锚点），导致全部漏拦；已在 `tools/build-rules.js` 的 `transformPattern` 中改为 `requestDomains`（含子域，语义等价）修复。

**当前基线（2026-11-04，评测集已改为真实子域，避免裸域名误判）**：
- 18 条示例规则 → 网络层静态覆盖率 **62.0%**（Google/国际网络 100%，部分国内网络 0%）。
- **接入真实源（117k 规则）后 → 网络层 72.5% / 综合(网络∪站点专属 cosmetic) 87.1%**。Google、Criteo、Adnxs、Taboola、百度 pos.baidu、阿里 tanx、新浪 sax 等绝大多数网络 100% 命中；剩余纯漏拦（网络层 0 且无 cosmetic）约 12.9%，集中在 `adservice.google.com`、`ad.toutiao.com`、`mimg.126.net` 等——这些在 EasyList 体系内网络层零规则，需后续补 AdGuard 中文列表或更强 cosmetic（已在 `evaluation/eval-last.json` 记录明细）。

> 静态覆盖率只测网络层 DNR 规则，不含 cosmetic 层与真机渲染，是覆盖广度的下界参考；综合覆盖率叠加了「站点专属元素隐藏」维度，更接近用户实际观感。真机拦截率须用 Puppeteer 复用 `sites.json` 实测（见 `evaluation/README.md`）。

## 4. 自测

完整用例见 `tests/self-test.md`（TC-01 ~ TC-08，与 PRD 第十五节 RTM 及本轮新增能力对应）。最小验证路径：

| 验证点 | 怎么做 |
|--------|--------|
| 拦截生效 | 访问含 `doubleclick.net` 等资源的页面，DevTools Network 里该请求显示 `(blocked:other)` / failed |
| 白名单生效 | popup 点「暂停本域」→ 刷新 → 该站广告恢复加载 |
| 全局开关 | options 关闭全局拦截 → 刷新 → 静态规则集停用 |
| 匿名上报 | 启动 mock 后端 → 打开 popup 触发 Service Worker 唤醒 → 后端控制台打印 `[OK]` |
| 关闭统计零外发 | 关闭统计 → 抓包/后端日志确认 **无任何 POST** |
| 字段合规 | 后端对越权字段返回 400，可手工 POST 含 `url` 的 payload 验证 |

---

## 5. dev / prod 差异（上线前必改）

| 项 | dev（当前） | prod（上架前） |
|----|-------------|----------------|
| `STATS_ENDPOINT` | `http://127.0.0.1:8787/v1/stats` | 改为 **HTTPS** 正式端点，配置 `prod` 常量或从远程配置读取 |
| 统计后端 | 内存聚合、零依赖 mock | 需鉴权/限流、原始日志 ≤ 30 日清理、访问审计、监控告警 |
| 规则集 | 12 条示例静态规则 | 预编译完整规则集进静态 ruleset（**靠发版更新**）+ 带签名校验的精选动态规则 |
| 远程规则校验 | 仅结构 + 条数校验 | 必须追加 **哈希/签名校验**（PRD 11.2） |
| 拦截计数 | 每条 `onErrorOccurred` 直接写 storage | 改内存缓冲 + 批量落盘，避免高频 IO |
| 首启告知文案 | 草案 | 经**法务审阅**，与公开隐私政策逐字一致 |
| `POLICY_URL` | `https://【待填】/privacy` | 替换为公开可访问 URL |
| `manifest.json` | 无 `icons`、version 0.1.0 | 补齐各尺寸图标、版本号、商店描述 |
| 权限理由 | 已写入权限声明文档 | 提交商店时逐条粘贴，与 manifest 实际权限对齐 |
| 测试期免费 | — | 本期无支付/账号，见 PRD 4.2 |

---

### 5.1 统计后端 prod 化（server/stats-server.js）

dev 阶段为「零依赖 mock」；prod 需补齐鉴权、持久化、留存清理、审计、防滥用，否则无法过审与合规。
本仓库的 `server/stats-server.js` 已重写为 **prod-ready 自托管参考实现（零依赖 Node）**，对应 PRD 第十一节数据合规 8 项与隐私政策第 5 节：

| 合规要求 | 实现 |
|----------|------|
| 字段白名单（无 URL/PII） | `validate()`：顶层字段 + 嵌套白名单 + 禁用词扫描，越权字段 400 |
| 最小化 / 去标识 | 仅存聚合字段；device_id 仅随机 UUID，与身份无关 |
| 留存清理（≤30 日） | 按日日志 `raw-YYYY-MM-DD.log` + `purgeOld()` 删 mtime > RETENTION_DAYS；启动与每日定时清理 |
| 审计 | `audit.log` 记录脱敏来源 IP + 时间 + 结果（不记 PII） |
| 防滥用 | 单 device_id 速率限制（`RATE_LIMIT_PER_MIN`） |
| 访问控制 | Bearer token 鉴权 + CORS Origin 白名单（`ALLOWED_ORIGIN`） |

**环境变量**：`PORT` / `HOST` / `STATS_AUTH_TOKEN`（prod 必填）/ `STATS_DATA_DIR` / `RETENTION_DAYS` / `RATE_LIMIT_PER_MIN` / `ALLOWED_ORIGIN`。

**前端联动**：`src/shared.js` 暴露 `sendReport(payload)` 与 `DEV_STATS_ENDPOINT` / `PROD_STATS_ENDPOINT` / `STATS_AUTH_TOKEN`；`background.js` 的 `maybeUpload()` 改调 `sendReport` 并自动附加 Bearer 头。上线时把生效端点指向 `PROD_STATS_ENDPOINT`（HTTPS），并通过打包脚本注入 `STATS_AUTH_TOKEN`（**切勿硬编码/提交真实值**）。

**部署建议**：生产环境前置 HTTPS 反向代理（Nginx / 云 LB）+ 托管数据库（Supabase / 云函数）；本参考实现适合中小规模自托管或作为对接契约。多实例部署时速率限制建议改用 Redis。

**验证结果（2026-11-04 已跑通）**：dev 无 token 200 / 越权 400 / health 聚合正确；token 模式无头·错头 401、正确 200；速率限制第 3 次 429；留存清理按 mtime 删过期文件（详见 `tests/self-test.md` TC-06）。

## 6. 打包与上架

> **打包前必做**：先跑 `node tools/build-rules.js` 生成最新 ruleset，确认 `tools/build-report.json` 的跳过分布符合预期；改过规则后还要在 `chrome://extensions` 点「重新加载」才会生效。

```bash
# 打包（在 extension/ 目录内，只打运行时文件；tools/ 与 filters/ 不进包）
zip -r cleanview-v0.1.0.zip manifest.json rules/ src/ \
  -x '*.map' '*/.DS_Store'
```

提交清单：
1. Chrome Web Store / Edge Add-ons / Firefox Add-ons 开发者账号（**阻塞依赖**）。
2. 权限理由说明：直接取 `store-permissions-declaration.md` 第二节。
3. 隐私政策：托管 `privacy-policy.md` 法务定稿版为公开 HTTPS 页面，把 URL 填入商店表单与 `options.js` 的 `POLICY_URL`。
4. 商品描述：取 `store-permissions-declaration.md` 第五节。
5. 确认规则源许可证（**阻塞依赖**）。

---

## 7. 常见坑（踩过会浪费半天）

- **Service Worker 随时会被终止**：不要依赖模块级变量或 `setInterval` 保存状态，一切状态写 `chrome.storage.local`；定时任务用 `chrome.alarms`。
- **`updateDynamicRules` 必须先 remove 再 add**：直接 add 会与旧 id 冲突报 `Rule with id X does not have a unique ID`。本实现先 `getDynamicRules()` 全量取 id 再移除。
- **白名单 `allow` 规则的 priority 必须高于静态 `block` 规则**：本实现 allow=100 vs block=1；且 `condition` 用 `initiatorDomains`（发起方站点），不是 `requestDomains`（被请求资源域）。
- **`updateEnabledRulesets` 的 ruleset id 必须已在 manifest 的 `rule_resources` 声明**，否则报错。
- **静态 ruleset 路径相对扩展根目录**，写错会导致整个扩展加载失败。
- **`onRuleMatchedDebug` 只在不打包的 dev 扩展可用**，别拿它做生产统计。
- **content script 隐藏元素要打标记**：`MutationObserver` 会反复看到同一节点，本实现用 `data-cv-hidden` 去重，否则计数会虚高。
- **误杀**：`[class^="ad-"]` 这类前缀选择器可能命中 `ad-note` 等非广告元素，实测后按需收紧；发现问题优先加白名单而非全局放宽选择器。
- **统计里的域名取"被拦请求"而非"访问站点"**：前者不暴露用户浏览意图，隐私更稳。

---

## 8. 风险与后续

| 风险 | 现状 | 缓解 |
|------|------|------|
| 规则覆盖不足 | 示例输入仅 18 条 | 预编译脚本已交付；接已确认许可证的完整规则源 + 建 50 站点评测集（PRD 第十三节） |
| 误杀正常内容 | 保守选择器，仍可能发生 | 白名单 + 误杀反馈入口 + 灰度 Top20 回归 |
| 拦截计数为近似值 | `onErrorOccurred` 口径 | 在指标口径中标注排除项，不用于对外宣称 |
| 商店审核 | 权限理由已备 | 提交前逐项核对权限声明自检清单 |
| 统计后端安全 | dev mock 无鉴权 | prod 补鉴权、限流、留存与审计 |

**已完成**：② 规则预编译脚本（`tools/build-rules.js`，含语法映射、保守跳过、裁剪与输出自检）。

**下一步（开发侧）**：① 50 站点评测集与拦截率 baseline；③ 统计后端 prod 化；④ Firefox 适配；⑤ 接入已确认许可证的完整规则源，跑一次真实规模预编译（验证 30,000 条裁剪、加载耗时与误杀率）。

> 合规声明：本方案为产品设计参考，具体实施请结合公司合规与法务要求确认；商店提交材料须以各平台现行开发者政策与当地法律法规为准。
