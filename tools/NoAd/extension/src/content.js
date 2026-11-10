/**
 * content.js — Cosmetic Filtering（页面广告元素隐藏）
 * 设计原则：保守选择器，尽量不误杀正常内容；
 * 白名单站点 / 全局禁用时直接跳过；只统计隐藏数量，不读取页面文本。
 * 注：content script 默认非 module，不 import shared.js。
 *
 * 注入策略（v3）：
 *  1. 一次性注入 <style> 样式表到「主文档 + 每一个 open Shadow DOM」；
 *     普通选择器统一 display:none!important。这样可命中 MSN 等把广告放进
 *     #shadow-root 的站点（传统注入主文档的样式无法穿透 shadow DOM）。
 *  2. 支持程序化规则 `selector:has-text(文字)`：用 JS 扫描 DOM（含 shadow），
 *     隐藏 textContent 含指定文字的元素。用于拦截「自带‘广告’角标但缺少稳定
 *     class」的原生信息流广告（如 MSN）。无法表达为 CSS 的伪类仍被保守过滤。
 *  3. 对当前页与后续动态插入的节点（信息流/懒加载）自动生效，无需 MutationObserver。
 */

/** 内置兜底选择器（原创整理；编译产物缺失时仍可工作） */
const BUILTIN_SELECTORS = [
  '[id^="ad-"]',
  '[id^="ads-"]',
  '[class^="ad-"]',
  '[id*="ad-container"]',
  '[class*="ad-container"]',
  '[id*="advertisement"]',
  '[class*="advertisement"]',
  '[data-ad-slot]',
  'ins.adsbygoogle',
  'iframe[src*="/ads/"]'
];

/** 无法表达为 CSS 的 ABP 扩展语法（除 :has-text 外，后者走程序化分支） */
const UNCSSABLE = /:-abp-|:xpath|#\?#|#@#/;
/** 程序化规则标记：:has-text(...) */
const HAS_TEXT_RE = /:has-text\(\s*["']?([^"')]*?)["']?\s*\)/;

function isPureCss(selector) {
  if (!selector || selector.length > 300) return false;
  return !UNCSSABLE.test(selector);
}

/** 解析 :has-text 规则；返回 { base, text } 或 null（非程序化规则） */
function parseHasText(selector) {
  const m = selector.match(HAS_TEXT_RE);
  if (!m) return null;
  const text = m[1];
  let base = selector.slice(0, m.index).trim();
  if (!base) base = "*";
  return { base, text };
}

/**
 * 合并内置选择器与预编译产物，按当前站点做域名限定，并拆分为：
 *  - css：纯 CSS 选择器（注入 <style>）
 *  - proc：程序化规则 { base, text }（用 JS 隐藏 textContent 含 text 的元素）
 * 上游产物由 tools/build-rules.js 生成为 src/cosmetic-selectors.js，
 * 用户自定义产物生成为 src/cosmetic-custom.js，两者在 manifest 中均先于本文件注入，
 * 分别挂到 globalThis.CV_COSMETIC_SELECTORS 与 CV_COSMETIC_CUSTOM，在此合并。
 */
function collectRules(host) {
  const compiled = globalThis.CV_COSMETIC_SELECTORS || [];
  const custom = globalThis.CV_COSMETIC_CUSTOM || [];
  const matches = (list) =>
    (list || []).some((d) => host === d || host.endsWith(`.${d}`));

  const css = [];
  const proc = [];
  for (const rule of [...compiled, ...custom]) {
    if (!rule || !rule.selector) continue;
    if (rule.domains?.length && !matches(rule.domains)) continue;
    if (matches(rule.excludedDomains)) continue;

    const ht = parseHasText(rule.selector);
    if (ht) {
      // 程序化规则：base 必须是合法 CSS 选择器才能定位元素
      if (isPureCss(ht.base)) proc.push({ base: ht.base, text: ht.text });
      continue;
    }
    if (isPureCss(rule.selector)) css.push(rule.selector);
  }
  return {
    css: [...new Set([...BUILTIN_SELECTORS, ...css].filter(isPureCss))],
    proc
  };
}

/** 取得某 root（Document 或 ShadowRoot）的样式挂载父节点 */
function getStyleParent(root) {
  return root.nodeType === 9 ? (root.head || root.documentElement) : root;
}

/** 往单个 root 注入 / 更新 cosmetic 样式（复用已存在的 #cv-cosmetic） */
function injectStyleToRoot(root, cssText) {
  let style = root.querySelector ? root.querySelector("#cv-cosmetic") : null;
  if (!style) {
    style = document.createElement("style");
    style.id = "cv-cosmetic";
    getStyleParent(root).appendChild(style);
  }
  style.textContent = cssText;
}

/**
 * 递归遍历主文档与所有 open Shadow DOM，对每个 root 调用 cb。
 * 这是 v3 的关键：传统做法只在主文档注入样式，无法隐藏 shadow root 内的广告。
 */
function forEachRoot(root, cb) {
  cb(root);
  let els = [];
  try {
    els = root.querySelectorAll ? Array.from(root.querySelectorAll("*")) : [];
  } catch {
    els = [];
  }
  for (const el of els) {
    if (el.shadowRoot && el.shadowRoot.mode === "open") {
      forEachRoot(el.shadowRoot, cb);
    }
  }
}

/** 程序化规则：在 root 内找到 base 匹配且 textContent 含 text 的元素并隐藏 */
function applyProcedural(root, procRules) {
  let count = 0;
  for (const pr of procRules) {
    let els;
    try {
      els = root.querySelectorAll(pr.base);
    } catch {
      continue;
    }
    for (const el of els) {
      const text = (el.textContent || "").trim();
      if (text && text.includes(pr.text)) {
        el.style.setProperty("display", "none", "important");
        count++;
      }
    }
  }
  return count;
}

function injectCosmetic() {
  const { css, proc } = collectRules(location.hostname);
  // 每次重新计算隐藏数（覆盖式，避免 MutationObserver 重复累加导致统计虚高）
  let hidden = 0;
  const cssText = css.length
    ? css.map((s) => `${s}{display:none!important}`).join("\n")
    : "";

  forEachRoot(document, (root) => {
    if (cssText) {
      injectStyleToRoot(root, cssText);
      // 统计被隐藏的节点数（仅本地统计，不进入匿名上报）
      for (const s of css) {
        try {
          hidden += root.querySelectorAll(s).length;
        } catch {
          /* 忽略非法选择器 */
        }
      }
    }
    if (proc.length) hidden += applyProcedural(root, proc);
  });
  return hidden;
}

/**
 * 监听 DOM 变化，延迟注入的节点（如 MSN 在 load 之后 fetch 的信息流 / 新出现的
 * Shadow DOM）也能被隐藏。用防抖避免高频 mutation 造成性能抖动；每次回调重新
 * 遍历所有 root（含新挂载的 open Shadow DOM）并重跑程序化规则。
 */
function observeMutations() {
  if (!document.documentElement) return;
  let timer = null;
  const schedule = () => {
    if (timer) clearTimeout(timer);
    timer = setTimeout(() => {
      hiddenCount = injectCosmetic();
    }, 300);
  };
  try {
    const obs = new MutationObserver(schedule);
    obs.observe(document.documentElement, { childList: true, subtree: true });
  } catch {
    /* 极少数环境不支持 MutationObserver，跳过即可（load 复扫仍有效） */
  }
}

let hiddenCount = 0;

(async function init() {
  let resp;
  try {
    resp = await chrome.runtime.sendMessage({ type: "IS_WHITELISTED" });
  } catch {
    return; // background 不可用时静默跳过，不影响页面
  }
  if (!resp || !resp.enabled || resp.whitelisted) return;

  hiddenCount = injectCosmetic();

  // 页面继续加载期间可能有更多节点 / Shadow DOM 出现，load 时再复扫一次
  window.addEventListener("load", () => {
    hiddenCount = injectCosmetic();
    observeMutations();
    if (hiddenCount > 0) {
      chrome.runtime.sendMessage({ type: "HIDDEN_COUNT", count: hiddenCount }).catch(() => {});
    }
  });

  // 离开页面时上报隐藏数量（仅本地统计）
  window.addEventListener("pagehide", () => {
    if (hiddenCount > 0) {
      chrome.runtime.sendMessage({ type: "HIDDEN_COUNT", count: hiddenCount }).catch(() => {});
    }
  });
})();
