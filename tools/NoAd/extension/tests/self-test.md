# 自测清单（对应 PRD 第十五节 RTM）

> 环境：Chrome / Edge ≥ 110，扩展以「加载已解压的扩展程序」方式运行。
> 前置：`node server/stats-server.js` 已启动（TC-06 需要）。

## TC-01 网络层拦截（FR-01）
- **Given** 扩展已加载、全局开关开启、静态 ruleset 已启用。
- **When** 访问包含 `doubleclick.net` / `googlesyndication.com` 等资源请求的页面。
- **Then** DevTools → Network 中对应请求状态为 `(blocked:other)` 或 failed；控制台无扩展报错。
- **验证补充**：在 Service Worker 控制台执行
  `await chrome.declarativeNetRequest.getEnabledRulesets()` → 应返回 `["ruleset_core"]`。

## TC-02 元素隐藏（FR-02 / Cosmetic）
- **Given** 页面含 `ins.adsbygoogle`、`[data-ad-slot]` 或 `id="ad-container"` 之类容器。
- **When** 页面加载到 `document_idle` 后。
- **Then** 该容器带 `data-cv-hidden="1"` 且不可见；页面正常内容未被隐藏。
- **动态场景**：滚动触发懒加载广告后，新增节点同样被标记（MutationObserver 生效）。
- **误杀检查**：确认正常内容（如含 `ad-` 前缀但非广告的模块）未消失；发现误杀走 TC-04 白名单。

## TC-03 规则更新与回滚（FR-03 / 9.1）
- **Given** 在 options 填入一个返回合法 DNR 规则数组的 HTTPS 地址（可本地起静态服务）。
- **When** 点「立即更新」或等待每日 alarm。
- **Then** 规则被应用，`lastRuleCheck` 更新；用户无感知。
- **失败场景**：把地址改为返回非法 JSON 或超限数组 → 更新失败，控制台打印 `规则更新失败 n/3，保留旧规则`，**原有拦截仍正常**。
- **限次校验**：确认最多 3 次尝试、退避 5s / 10s，无无限重试。

## TC-04 白名单与全局开关（FR-04 / 9.2）
- **Given** 页面广告被成功拦截。
- **When** popup 点「暂停本域」→ 刷新。
- **Then** 该域广告恢复加载；`chrome.declarativeNetRequest.getDynamicRules()` 中存在 `priority:100`、`action.type:"allow"`、`condition.initiatorDomains` 为该域的规则。
- **子域**：`example.com` 白名单应覆盖 `www.example.com`（`isWhitelisted` 判定）。
- **恢复**：再点「恢复本域拦截」→ allow 规则被移除。
- **全局开关**：options 关闭后 `getEnabledRulesets()` 返回空数组，所有站点恢复正常。

## TC-05 本地统计（FR-05 / 10）
- **Given** 已产生若干拦截与元素隐藏。
- **When** 打开 popup。
- **Then** 显示「今日拦截」「今日隐藏元素」数值，且**不含任何完整 URL**。
- **跨日重置**：把系统日期推进一天（或改 `stats.date`），统计归零。
- **口径提醒**：拦截数为 `onErrorOccurred` 近似值，不含 cosmetic 隐藏与缓存命中，验证时不要按"广告元素总数"对齐。

## TC-06 匿名统计上报与字段合规（FR-06 / 10.2 / 11.3）
- **Given** 匿名统计开启、mock 后端已启动、Service Worker 被唤醒（打开 popup 即可）。
- **When** 触发 `maybeUpload()`（首次唤醒或每日 alarm）。
- **Then** 后端打印 `[OK] <date> blocked=… domains=…`；`GET /health` 聚合值增长。
- **每日一次**：同一天重复唤醒，后端 `requests` 不增加。
- **字段白名单**：手工 POST 含 `url` / `total_hidden` 等多余字段 → 返回 **400**，日志 `[REJECT] 未授权字段`。
- **抓包验证（关键）**：关闭「匿名效果统计」后，用 DevTools Network / Fiddler 确认**无任何发往统计端点的请求**。
- **首启告知**：全新安装（换一个 Chrome 用户配置）应自动打开 `options.html?welcome=1`，横幅明示统计内容与关闭入口。
- **前端联动**：`background.js` 经 `shared.js` 的 `sendReport()` 上报；端点/令牌集中在 `src/shared.js`（dev=本地 http、prod=HTTPS 常量），上线改指 PROD 并注入 token（勿提交真实值）。
- **鉴权（prod）**：设 `STATS_AUTH_TOKEN=xxx` 启动后端 → 无 / 错 `Authorization` 头返回 **401**，正确 `Bearer xxx` 返回 **200**。
- **速率限制**：设 `RATE_LIMIT_PER_MIN=2` → 同 device_id 第 3 次上报返回 **429**。
- **留存清理**：在 `STATS_DATA_DIR` 写 `raw-YYYY-MM-DD.log` 并将 mtime 设为 > RETENTION_DAYS 天前 → 重启服务，启动日志 `已删除 N 个过期原始日志文件`、该过期文件被删（当日文件保留）。

## TC-07 权限最小化（FR-07 / 11.1）
- **Given** `manifest.json` 与 `store-permissions-declaration.md` 并排。
- **When** 逐项比对权限表。
- **Then** manifest 中无 `webRequestBlocking`、无读取 Cookie/历史的权限；`host_permissions` 的 `<all_urls>` 已在权限声明中给出用途理由。
- **设置持久化**：修改白名单/开关 → 关闭再打开浏览器 → 设置保持。

## TC-08 规则预编译（本轮新增能力）

- **Given** `filters/sample-filters.txt` 存在、Node >= 18。
- **When** 执行 `node tools/build-rules.js --input filters/sample-filters.txt`。
- **Then** 退出码 0；输出 `静态 DNR 规则: 18`、`Cosmetic 选择器: 6`、`跳过 / 裁剪: 3 条（语法跳过 3 / 静态超限 0 / 正则超限 0）`；生成 3 个产物文件。
- **产物校验**：
  - `rules/ruleset_core.json` 可 `JSON.parse`；id 唯一且连续；`action.type` 仅 `block` / `allow` / `allowAllRequests`；`resourceTypes` 全在 DNR 合法枚举内（如 `subdocument` → `sub_frame`）。
  - `src/cosmetic-selectors.js` 通过 `node --check`，挂载 `globalThis.CV_COSMETIC_SELECTORS`。
  - `tools/build-report.json` 的 `skipReasons` 含 `redirect`、`csp`、`cosmetic 例外(#@#)` 各 1，`truncatedBy` 两项均为 0。
- **CLI 参数生效（回归用例，曾发现 bug）**：用 100 条规则输入 + `--max-static 50` + 自定义 `--out-static/--out-cosmetic/--report` 运行 → 输出 **50** 条到**指定路径**（不得覆盖正式产物），报告 `truncated=true`、`truncatedBy.staticLimit=50`、`skippedTotal=50`、id 1..50 唯一。
  > 历史缺陷：`parseArgs` 曾把 `--max-static` 存成 `CFG["max-static"]` 而配置读 `CFG.maxStatic`，导致参数静默失效并覆盖正式产物。已改为 kebab-case → camelCase 映射，并对缺取值参数报错。
- **域名限定**：`.sponsored-card` 仅在 `news.example.com` 生效；`.promo-banner` 在 `shop.example.com` 不生效、其他站点生效。
- **自检防线**：手工往 ruleset 塞入重复 id 或非法 resourceType 后再次运行脚本 → 应退出码 1 且不覆盖产物。
- **失败路径**：`--input` 指向不存在文件 → 退出码 1，`[FAIL] 输入文件不存在`。
- **接入验证**：`chrome://extensions` 点「重新加载」后，含 `ins.adsbygoogle` 的页面元素带 `data-cv-hidden="1"`。
- **顺序验证**：调换 manifest 中 `content_scripts.js` 顺序不会崩（内置兜底选择器仍工作），但编译规则失效——用于确认兜底设计有效。

---

### TC-09 评测集与静态覆盖率基线（evaluation/）

**前置**：`python3 evaluation/gen-sites.py` 已生成 `evaluation/sites.json`（≥ 50 站点、≥ 150 样本）。

**Given** 当前 `rules/ruleset_core.json` 为已编译产物
**When** 运行 `node evaluation/run-eval.js`
**Then**
- 退出码 0，输出**网络层**覆盖率与**综合（网络∪站点专属 cosmetic）**覆盖率；
- 评测集广告网络样本已改为真实子域（`ad.doubleclick.net`、`pagead2.googlesyndication.com` 等）；
- Google/Criteo/Adnxs/Taboola/百度 pos.baidu/阿里 tanx/新浪 sax 等绝大多数网络网络层命中 = 100%；
- `ad.qt.qq.com`/`ad.toutiao.com`/`mimg.126.net` 在 EasyList 体系内网络层零规则（已知缺口，靠 cosmetic 或补充列表覆盖，非 bug）；
- 生成 `evaluation/eval-last.json`。

---

### TC-10 真实规则源流水线（tools/fetch-rules.js）

**前置**：`rules-sources.json` 已配置启用源（默认 EasyList + EasyPrivacy + EasyList China）。

**Given** 仓库初始状态（仅有 `filters/sample-filters.txt`，无完整规则产物）
**When** 运行 `npm run build:full`（或 `node tools/fetch-rules.js`）
**Then**
- 退出码 0；依次完成「下载 → 拼接 → 编译」三步，输出静态规则 **≥ 100,000** 条、`src/cosmetic-selectors.js` 选择器数 = `--max-cosmetic`（默认 8000）；
- `rules/ruleset_core.json` 可 `JSON.parse`、`id` 唯一、`action.type` 合法（即 build-rules 自检通过）；
- 产物体积报告出现（`ruleset_core.json` 约 7–13 MB，取决于是否 `--minify`）；
- 重新运行 `node evaluation/run-eval.js` 后，网络层覆盖率较 18 条示例基线明显提升（实测 62% → 72.5%，综合 87.1%）；
- `filters/_fetched.txt` 与产物均被 `.gitignore` 忽略，**未纳入版本控制**；
- 构建报告 `tools/build-report.json` 的 `skipReasons` 含 `popup`、`redirect=*`、`csp=*`、`cosmetic 例外(#@#)` 等（说明保守跳过策略生效）。

**Given** 运行 `node tools/fetch-rules.js --dry-run`
**Then** 仅下载到 `filters/_fetched/`，不调用 build-rules、不覆盖产物。

**Given** 运行 `node tools/fetch-rules.js --only easylist`
**Then** 仅下载 EasyList 一个源并编译，其余源不参与。

**TC-11｜本地自定义规则（filters/custom.txt）可从零生成并优先生效**
**Given** 在 `filters/custom.txt` 中写入启用规则（如 `||ads.test-custom.com^`、`@@||ok.test-custom.com^$document`、`##.my-ad`）
**When** 运行 `npm run build:custom`
**Then**
- 生成 `rules/ruleset_custom.json` 与 `src/cosmetic-custom.js`；网络规则 `id` 从 **200001** 起、`priority` 为原值 **+100**（普通 block=101、例外 allowAllRequests=200）；`cosmetic-custom.js` 挂到 `globalThis.CV_COSMETIC_CUSTOM`；
- `manifest.json` 的 `declarative_net_request.rule_resources` 含 `ruleset_custom`（enabled:true），`content_scripts.js` 列表含 `src/cosmetic-custom.js` 且顺序在 `src/content.js` 之前；
- `background.js` 的 `RULESET_ALL` 含 `ruleset_core` 与 `ruleset_custom`，全局开关关闭时两者同时被禁用；
- 改完 `filters/custom.txt` 保存后，扩展管理页「刷新 (Reload)」即生效，无需重启浏览器；
- 若 `filters/custom.txt` 全为注释，则 `ruleset_custom.json = []`（开箱即用、合法，不影响扩展加载）。

## 回归触发点
以下改动后需重跑全量 TC：
- 修改 `filters/*.txt`、`tools/build-rules.js` 或重新生成 ruleset（必须重跑 TC-08 + TC-01/02）；
- 修改 `ruleset_core.json`（注意：这是生成物，应改输入而非直接改产物）；
- 修改 `buildReport()` 字段（会同时影响隐私政策与后端校验）；
- 调整动态规则 id 分段（与静态规则 id 1..N 的隔离）；
- 升级 manifest 权限清单或 `content_scripts.js` 注入顺序。
- 修改 `tools/build-rules.js` 的 `transformPattern`（ABP→DNR 转换，曾因 `||host^` 写入 urlFilter 导致 0% 覆盖率）；
- 修改 `evaluation/run-eval.js` 匹配器（`matchUrlFilter` / `domainMatches` / `registrable`）或 `gen-sites.py` 站点/广告网络模板（需重跑 TC-09）。
- 修改 `tools/fetch-rules.js`、`rules-sources.json` 或 `tools/build-rules.js` 的 `maxStatic`/`maxCosmetic`/`minify` 默认值（需重跑 TC-10）。
- 修改 `content.js` 的 cosmetic 注入（`<style>` 注入、非 CSS 伪类过滤）或 `build-rules.js` 的 cosmetic 裁剪（需重跑 TC-08 的 `src/cosmetic-selectors.js` 校验）；
- 修改 `filters/custom.txt`、`rules-sources.json` 的 `customFilterPath`、或 `fetch-rules.js` 的自定义编译段（id-base 200000 / priority-offset 100 / cosmetic-var CV_COSMETIC_CUSTOM）（需重跑 TC-11；并确认 `manifest.json` 中 `ruleset_custom` 已注册、`content_scripts.js` 注入顺序正确）；
- 修改 `build-rules.js` 的 `--id-base` / `--priority-offset` / `--cosmetic-var` 参数（影响自定义层隔离与优先级，需重跑 TC-11）；
- 调整规则 id 段规划（core 1..150000 / custom 200001+ / 白名单 400000+ / 远程 410000+，必须互不重叠，否则 `updateDynamicRules` 会因 id 冲突报错）。

> 合规声明：本测试用例为产品设计参考，具体实施请结合公司合规与法务要求确认。
