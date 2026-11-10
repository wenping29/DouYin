/**
 * background.js — MV3 Service Worker
 * 职责：
 *  1) 动态规则管理：站点白名单 allow 规则 + 远程精选规则（含启停静态规则集）
 *  2) 规则静默更新（每日 alarm + 限次退避重试，失败保留旧规则）
 *  3) 本地拦截计数（webRequest.onErrorOccurred 观察 ERR_BLOCKED_BY_CLIENT）
 *  4) 匿名统计每日一次上报（可关闭、失败静默丢弃）
 *  5) 首启告知（FR-06）
 */
import {
  getSettings, setSettings, getStats, addBlocked, addHidden,
  getDeviceId, resetDeviceId, hostnameOf, isWhitelisted,
  buildReport, sendReport, today, MAX_REMOTE_RULES, WHITELIST_RULE_ID_BASE,
  REMOTE_RULE_ID_BASE
} from "./shared.js";

const VERSION = chrome.runtime.getManifest().version;
const RULESET_CORE = "ruleset_core";
const RULESET_CUSTOM = "ruleset_custom";
const RULESET_ALL = [RULESET_CORE, RULESET_CUSTOM];
const ALARM_RULES = "cv-rules-daily";
const ALARM_STATS = "cv-stats-daily";

/* ============================================================
 * 1. 安装 / 启动初始化
 * ============================================================ */

chrome.runtime.onInstalled.addListener(async (details) => {
  // 两个每日 alarm（24h）。dev 验证时可临时改小周期。
  chrome.alarms.create(ALARM_RULES, { periodInMinutes: 60 * 24 });
  chrome.alarms.create(ALARM_STATS, { periodInMinutes: 60 * 24 });

  await rebuildDynamicRules();

  if (details.reason === "install") {
    // FR-06：首次运行明示统计内容与关闭方式，未经提示不默认采集
    await chrome.tabs.create({
      url: chrome.runtime.getURL("src/options.html?welcome=1")
    });
  }
});

// Service Worker 每次唤醒也尝试一次上报（内部有"每日一次"与开关判断）
maybeUpload();

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === ALARM_RULES) updateRemoteRulesWithRetry();
  if (alarm.name === ALARM_STATS) maybeUpload();
});

/* ============================================================
 * 2. 动态规则：白名单 + 远程精选规则
 * ============================================================ */

/**
 * 重建全部动态规则（先取现有 id 全量移除，再添加）。
 * 白名单 allow 规则 priority=100，高于静态 block 规则 priority=1。
 */
async function rebuildDynamicRules() {
  const settings = await getSettings();
  const { remoteRules = [] } = await chrome.storage.local.get("remoteRules");
  const rules = [];

  // 2.1 站点白名单：allow 规则，condition 用 initiatorDomains（发起方 = 用户访问站点）
  settings.whitelist.forEach((domain, i) => {
    if (!domain) return;
    rules.push({
      id: WHITELIST_RULE_ID_BASE + i,
      priority: 100,
      action: { type: "allow" },
      condition: { initiatorDomains: [domain] }
    });
  });

  // 2.2 远程精选规则：id 用独立段 REMOTE_RULE_ID_BASE，与白名单/静态规则隔离
  remoteRules.slice(0, MAX_REMOTE_RULES).forEach((r, i) => {
    rules.push({ ...r, id: REMOTE_RULE_ID_BASE + i });
  });

  const existing = await chrome.declarativeNetRequest.getDynamicRules();
  await chrome.declarativeNetRequest.updateDynamicRules({
    removeRuleIds: existing.map((r) => r.id),
    addRules: rules
  });

  // 2.3 全局开关：启停静态规则集（比清空所有 block 规则更省事、更可靠）
  // 同时管理上游核心集与用户自定义集，保证总开关语义一致
  await chrome.declarativeNetRequest.updateEnabledRulesets({
    enableRulesetIds: settings.enabled ? RULESET_ALL : [],
    disableRulesetIds: settings.enabled ? [] : RULESET_ALL
  });
}

/** 远程规则合法性校验：结构 + 条数。prod 应追加签名/哈希校验 */
function validateRemoteRules(data) {
  if (!Array.isArray(data)) return false;
  if (data.length > MAX_REMOTE_RULES) return false;
  return data.every(
    (r) =>
      r &&
      r.action &&
      ["block", "allow", "allowAllRequests"].includes(r.action.type) &&
      r.condition &&
      typeof r.condition === "object"
  );
}

/**
 * FR-03：每 24h 检查更新；本次最多 3 次尝试、递增退避；
 * 校验失败/拉取失败一律保留旧规则并静默退出，不阻断现有拦截。
 */
async function updateRemoteRulesWithRetry() {
  const settings = await getSettings();
  if (!settings.rulesUrl) return; // dev 未配置订阅源则跳过

  for (let attempt = 1; attempt <= 3; attempt++) {
    try {
      const res = await fetch(settings.rulesUrl, { cache: "no-store" });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();
      if (!validateRemoteRules(data)) throw new Error("规则校验失败");

      await chrome.storage.local.set({
        remoteRules: data,
        lastRuleCheck: new Date().toISOString()
      });
      await rebuildDynamicRules();
      return;
    } catch (err) {
      console.warn(`[CleanView] 规则更新失败 ${attempt}/3，保留旧规则`, err);
      if (attempt < 3) {
        await new Promise((resolve) => setTimeout(resolve, 5000 * attempt));
      }
    }
  }
}

/* ============================================================
 * 3. 本地拦截计数
 * ------------------------------------------------------------
 * 坑：onRuleMatchedDebug / declarativeNetRequestFeedback 仅在
 * unpacked(dev) 扩展可用，生产安装的扩展拿不到规则命中详情。
 * 因此生产用 webRequest.onErrorOccurred 观察
 * net::ERR_BLOCKED_BY_CLIENT 近似统计「请求级拦截数」。
 * 这是近似值：不含 cosmetic 元素隐藏，也不含被缓存命中的请求。
 * ============================================================ */

chrome.webRequest.onErrorOccurred.addListener(
  (details) => {
    if (details.error !== "net::ERR_BLOCKED_BY_CLIENT") return;
    // 取「被拦请求」的域名，不取用户访问站点，避免统计里出现浏览意图
    const domain = hostnameOf(details.url);
    // dev 简化：逐条写 storage。prod 优化：内存缓冲 + 批量落盘，降低 IO。
    addBlocked(domain).catch(() => {});
  },
  { urls: ["<all_urls>"] }
);

/* ============================================================
 * 4. 匿名统计上报（FR-06）
 * ============================================================ */

async function maybeUpload() {
  const settings = await getSettings();
  if (!settings.statsEnabled) return; // 关闭后零外发，TC-06 可抓包验证

  const day = today();
  if (settings.lastUploadDate === day) return; // 每日一次聚合上报

  const deviceId = await getDeviceId();
  const stats = await getStats();
  const payload = buildReport(stats, deviceId, VERSION);

  try {
    const ok = await sendReport(payload);
    if (!ok) throw new Error("上报未成功");
    await setSettings({ lastUploadDate: day });
  } catch (err) {
    // 失败静默丢弃：不重试轰炸、不阻塞主流程
    console.warn("[CleanView] 统计上报失败，已丢弃本次数据", err);
  }
}

/* ============================================================
 * 5. 消息路由（popup / options / content script）
 * ============================================================ */

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  // content script 询问当前站点是否放行
  if (msg?.type === "IS_WHITELISTED") {
    (async () => {
      const settings = await getSettings();
      const host = hostnameOf(sender?.url || "");
      sendResponse({
        enabled: settings.enabled,
        whitelisted: isWhitelisted(host, settings.whitelist)
      });
    })();
    return true; // 异步响应
  }

  // content script 上报本次页面隐藏元素数量
  if (msg?.type === "HIDDEN_COUNT") {
    addHidden(msg.count).catch(() => {});
    return false;
  }

  // 设置变更后重建动态规则
  if (msg?.type === "SETTINGS_CHANGED") {
    rebuildDynamicRules().catch((e) => console.warn("[CleanView] 规则重建失败", e));
    return false;
  }

  // 手动触发规则更新
  if (msg?.type === "MANUAL_RULE_UPDATE") {
    updateRemoteRulesWithRetry();
    return false;
  }

  // 重置匿名标识
  if (msg?.type === "RESET_DEVICE_ID") {
    (async () => {
      const id = await resetDeviceId();
      sendResponse({ deviceId: id });
    })();
    return true;
  }

  return false;
});
