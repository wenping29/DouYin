/**
 * shared.js — 扩展各页面共享的存储 schema、统计与匿名上报契约（ES module）
 * 注意：content script 不使用本模块（content script 默认非 module），
 *       它通过 chrome.runtime.sendMessage 让 background 代为写统计。
 */

/** 默认设置（存储于 chrome.storage.local） */
export const DEFAULTS = {
  enabled: true,          // 全局拦截开关
  statsEnabled: true,     // 匿名统计开关（默认开启，可一键关闭）
  whitelist: [],          // 站点白名单（hostname 列表）
  rulesUrl: "",            // 远程精选规则 URL；空 = 不拉取（dev 默认）
  lastUploadDate: "",     // 上次成功上报日期（每日一次）
  lastRuleCheck: ""       // 上次规则检查时间
};

/** 本地日期（口径：浏览器本地自然日，非 UTC） */
export function today() {
  const d = new Date();
  const pad = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** 空的当日统计对象 */
export function emptyStats() {
  return {
    date: today(),
    totalBlocked: 0,     // 被拦截的请求数（webRequest 观察值）
    totalHidden: 0,      // 被隐藏的元素数（content script 上报，仅本地）
    domains: {},         // 被拦请求域名 -> 计数（不含访问站点、不含完整 URL）
    ruleHits: {}         // 规则集 -> 命中计数（用于命中分布）
  };
}

/* ---------------- 设置 ---------------- */

export async function getSettings() {
  const saved = await chrome.storage.local.get(Object.keys(DEFAULTS));
  return { ...DEFAULTS, ...saved };
}

export async function setSettings(patch) {
  await chrome.storage.local.set(patch);
}

/* ---------------- 本地统计 ---------------- */

export async function getStats() {
  const { stats } = await chrome.storage.local.get("stats");
  const fresh = emptyStats();
  // 跨自然日自动重置当日统计
  if (!stats || stats.date !== fresh.date) return fresh;
  return { ...fresh, ...stats };
}

/** 记录一次请求级拦截。domain 取「被拦请求」的 hostname，不取用户访问站点，避免暴露浏览意图 */
export async function addBlocked(domain, source = "ruleset_core") {
  const s = await getStats();
  s.totalBlocked += 1;
  if (domain) s.domains[domain] = (s.domains[domain] || 0) + 1;
  s.ruleHits[source] = (s.ruleHits[source] || 0) + 1;
  await chrome.storage.local.set({ stats: s });
}

/** 记录元素隐藏数量（仅本地展示，不进入匿名上报） */
export async function addHidden(count) {
  if (!Number.isFinite(count) || count <= 0) return;
  const s = await getStats();
  s.totalHidden += count;
  await chrome.storage.local.set({ stats: s });
}

/* ---------------- 匿名标识 ---------------- */

/** 随机匿名标识；与真实身份无关，可重置 */
export async function getDeviceId() {
  const { deviceId } = await chrome.storage.local.get("deviceId");
  if (deviceId) return deviceId;
  const fresh = crypto.randomUUID();
  await chrome.storage.local.set({ deviceId: fresh });
  return fresh;
}

/** 重置后新旧标识不可关联 */
export async function resetDeviceId() {
  const fresh = crypto.randomUUID();
  await chrome.storage.local.set({ deviceId: fresh });
  return fresh;
}

/* ---------------- 工具 ---------------- */

export function hostnameOf(url) {
  try {
    return new URL(url).hostname;
  } catch {
    return "";
  }
}

/** 是否命中白名单（支持子域：example.com 覆盖 www.example.com） */
export function isWhitelisted(host, list) {
  if (!host) return false;
  return list.some((d) => host === d || host.endsWith(`.${d}`));
}

/* ---------------- 匿名上报契约 ---------------- */

/**
 * 构造上报 payload —— 严格字段白名单，与 PRD 第十节 10.2 一致。
 * 禁止出现：完整 URL、搜索词、页面文本、任何 PII。
 * 注：totalHidden 仅本地展示，按要求不进入上报。
 */
export function buildReport(stats, deviceId, clientVersion) {
  const topDomains = Object.entries(stats.domains || {})
    .sort((a, b) => b[1] - a[1])
    .slice(0, 100)
    .map(([domain, count]) => ({ domain, count }));

  const ruleHitDistribution = {};
  const hitValues = Object.values(stats.ruleHits || {});
  const total = hitValues.reduce((a, b) => a + b, 0) || 1;
  for (const [k, v] of Object.entries(stats.ruleHits || {})) {
    ruleHitDistribution[k] = Number((v / total).toFixed(4));
  }

  return {
    device_id: deviceId,
    date: stats.date,
    total_blocked: stats.totalBlocked,
    top_domains: topDomains,
    rule_hit_distribution: ruleHitDistribution,
    client_version: clientVersion
  };
}

/* ---------------- 上报端点与发送 ---------------- */

/**
 * 上报端点与鉴权。
 * - dev：本地零依赖服务（server/stats-server.js，AUTH_TOKEN 留空）
 * - prod：替换为真实 HTTPS 端点；STATS_AUTH_TOKEN 由构建/环境变量注入，切勿硬编码进仓库
 * 切换：上线时把 STATS_ENDPOINT 指向 PROD 值，并通过打包脚本注入 STATS_AUTH_TOKEN。
 */
export const DEV_STATS_ENDPOINT = "http://127.0.0.1:8787/v1/stats";
export const PROD_STATS_ENDPOINT = "https://stats.cleanview.example/v1/stats"; // TODO 上线替换
export const STATS_ENDPOINT = DEV_STATS_ENDPOINT; // 当前生效端点（dev 联调用）
export const STATS_AUTH_TOKEN = ""; // TODO 上线注入真实共享密钥（不要提交真实值）

/**
 * 发送匿名上报。prod 自动附加 Bearer 鉴权头。
 * @returns {Promise<boolean>} 成功 true；失败 false（调用方据此静默丢弃，不重试轰炸）
 */
export async function sendReport(payload, opts = {}) {
  const endpoint = opts.endpoint || STATS_ENDPOINT;
  const token = opts.token ?? STATS_AUTH_TOKEN;
  const headers = { "Content-Type": "application/json" };
  if (token) headers["Authorization"] = `Bearer ${token}`;
  try {
    const res = await fetch(endpoint, { method: "POST", headers, body: JSON.stringify(payload) });
    return res.ok;
  } catch {
    return false;
  }
}

/* ---------------- 常量 ---------------- */

/** Chrome MV3 动态规则上限（当前为 30000），留余量给远程精选规则 */
export const MAX_REMOTE_RULES = 5000;
/**
 * 规则 id 段规划（Chrome DNR 要求所有规则 id 在扩展内全局唯一，跨静态/动态 ruleset）：
 *   - 静态 ruleset_core     : 1 .. ~150000（编译生成）
 *   - 静态 ruleset_custom   : 200001 .. （编译生成，id-base 200000）
 *   - 动态白名单 allow 规则  : WHITELIST_RULE_ID_BASE ..
 *   - 动态远程精选规则      : REMOTE_RULE_ID_BASE ..
 * 必须互不重叠，否则 updateDynamicRules 会因 id 冲突报错。
 */
export const WHITELIST_RULE_ID_BASE = 400000;
export const REMOTE_RULE_ID_BASE = 410000;
