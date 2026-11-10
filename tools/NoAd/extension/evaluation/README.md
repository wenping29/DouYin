# 评测集与拦截覆盖率（evaluation/）

## 1. 用途

本目录用于**定义并量化广告拦截的 baseline 覆盖率**——这是 PRD 第十五节 RTM 评审遗留项（REV-07）的闭环。

- `gen-sites.py`：生成评测集 `sites.json`（50+ 主流站点 × 典型广告请求样本）。
- `sites.json`：评测集数据（构造样本，非真实抓包）。
- `run-eval.js`：**无浏览器依赖**的静态覆盖率评测器，用近似 DNR 语义的匹配器判定每条样本是否被当前规则拦截。
- `eval-last.json`：最近一次评测明细（自动生成）。

## 2. 评测集构成

| 项 | 值 |
|----|----|
| 站点数 | 57（覆盖新闻/视频/社交/电商/搜索/门户/论坛/科技/金融/游戏/国际 11 类） |
| 样本数 | 171（每站点 3 个广告请求样本：1 个 Google 系 + 1 个国际第三方 + 1 个国内网络） |
| 样本字段 | `url`(广告请求URL)、`type`(展示/信息流/贴片/弹窗)、`resourceType`、`party`(first/third)、`initiator`(发起站点host)、`expected`(均 block) |

> 样本为「构造的预期广告请求」，基于公开已知的广告网络域名生成，不代表真实抓包。用途是测**规则能否匹配**，而非测真实页面。

## 3. 覆盖口径（务必理解边界）

```
网络层静态覆盖率 = 被规则命中 block 的样本数 / 总样本数
综合覆盖率       = (网络层 block ∪ 站点有专属 cosmetic 元素隐藏规则) 的样本数 / 总样本数
```

**网络层只测 DNR 规则**（`rules/ruleset_core.json`）。`run-eval.js` 额外统计**综合覆盖率**：若样本发起站点存在专属 cosmetic 元素隐藏规则（即使该广告请求未被网络层拦截，其容器在页面上也会被隐藏），则计入「覆盖」。

**不含**：

- 通用 cosmetic 规则（无域名限定，已注入但不计入综合指标，避免虚高）；
- 真机渲染后的实际拦截（缓存命中、service worker 计数偏差）；
- 视频贴片在正片流内的覆盖（DNR 无法按内容区分同域同流）。

**评测集使用真实子域**：广告网络样本 URL 已改为 EasyList 实际在网络层拦截的子域（如 `ad.doubleclick.net`、`pagead2.googlesyndication.com`），避免用裸域名导致网络层评测失真。其中 `ad.qt.qq.com`/`ad.toutiao.com`/`mimg.126.net` 在 EasyList 体系内网络层零规则，用于暴露真实缺口。

因此**静态/综合覆盖率 ≠ 用户体验层面的真实拦截率**，它们分别是「网络层覆盖广度」与「网络+站点隐藏覆盖」的参考。真实拦截率需真机评测（见第 6 节）。

## 4. 运行

```bash
# 重新生成评测集（站点/样本调整后用）
python3 evaluation/gen-sites.py

# 对当前规则产物跑静态覆盖率
node evaluation/run-eval.js
# 指定规则/评测集
node evaluation/run-eval.js --rules rules/ruleset_core.json --sites evaluation/sites.json
```

输出：总覆盖率、按类别覆盖率、按广告网络命中率（一眼看出漏拦的网络）、漏拦样本示例、明细 JSON。

## 5. 如何扩展

- 加站点：编辑 `gen-sites.py` 的 `SITES` 列表（name, url, category, ad_type）。
- 加广告网络：编辑 `GOOGLE/INTL/CN` 三元组（host, resourceType, party）。
- 调样本分布：改 `build_samples` 的 `picks` 组合。
- 重新运行第 4 节命令即可。

## 6. 真机评测建议（上线前必做）

静态评测通过后，应补**真机评测**以逼近真实拦截率：

1. 用 Puppeteer/Playwright 加载 `sites.json` 中每个站点，记录实际发出的请求与是否被拦。
2. 用 `page.on('requestfinished'/'requestfailed')` 统计被 DNR 拦截（`ERR_BLOCKED_BY_CLIENT`）的请求占比。
3. 对 cosmetic 层，截图对比开启/关闭扩展的元素隐藏差异。
4. 误杀检查：在 Top 20 核心站点验证正常内容（图片/视频/正文）未被误拦。

真机评测脚本不在本仓库（需浏览器环境），但可直接复用 `sites.json` 作为输入。

## 7. 基线记录

| 日期 | 规则数 | 静态覆盖率 | 备注 |
|------|--------|------------|------|
| 2026-11-04 | 18（修复 `||host^`→requestDomains 后） | **62.0%** | Google/国际网络 100%，国内网络 0%（需接入含国内网络的完整规则源） |
| 2026-11-04 | 18（修复前） | 0.0% | 证明 `||...^` 被原样写入 urlFilter 导致全部漏拦的 bug |
