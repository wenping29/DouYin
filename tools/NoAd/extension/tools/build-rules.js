#!/usr/bin/env node
/**
 * build-rules.js — 规则预编译：ABP / filter 列表语法 → Chrome MV3 DNR 静态规则 + Cosmetic 选择器
 *
 * 为什么需要它：
 *   MV3 的静态 ruleset 只能随扩展包发布，运行时不能替换文件内容；
 *   动态规则上限 30,000 条，而公开规则集是十万条级。
 *   因此完整规则必须在本机构建阶段预编译进静态 ruleset，靠发版更新。
 *
 * 零依赖，Node >= 18。
 *
 * 用法（任意目录运行均可，路径相对 extension/ 根）：
 *   node tools/build-rules.js \
 *     --input filters/sample-filters.txt \
 *     --out-static rules/ruleset_core.json \
 *     --out-cosmetic src/cosmetic-selectors.js \
 *     --report tools/build-report.json \
 *     --max-static 30000
 *
 * 输出：
 *   - 静态 DNR 规则数组（rules/ruleset_core.json）
 *   - cosmetic 选择器（src/cosmetic-selectors.js，挂到 globalThis，供 content.js 读取）
 *   - 构建报告（tools/build-report.json，含跳过原因分布，便于追溯）
 *
 * 设计原则：遇到无法无损映射的选项一律「保守跳过」，宁可少拦也不误拦。
 */

"use strict";

const fs = require("node:fs");
const path = require("node:path");

const ROOT = path.resolve(__dirname, "..");

/* ============================================================
 * 1. CLI 参数
 * ============================================================ */

function parseArgs(argv) {
  const args = {};
  // CLI 用 kebab-case（--out-static），内部配置用 camelCase（outStatic）
  const toCamel = (s) => s.replace(/-([a-z])/g, (_, c) => c.toUpperCase());
  for (let i = 0; i < argv.length; i += 2) {
    const key = argv[i];
    if (!key || !key.startsWith("--")) throw new Error(`非法参数: ${key}`);
    const value = argv[i + 1];
    if (value === undefined || value.startsWith("--")) {
      throw new Error(`参数 ${key} 缺少取值`);
    }
    args[toCamel(key.slice(2))] = value;
  }
  return args;
}

const CFG = {
  input: "filters/sample-filters.txt",
  outStatic: "rules/ruleset_core.json",
  outCosmetic: "src/cosmetic-selectors.js",
  report: "tools/build-report.json",
  // Chrome 保证可用的静态规则下限是 30,000 条；理论总量上限更高，
  // 但保守按 30,000 裁剪，避免低端设备/未来策略变化导致加载失败。
  maxStatic: 30000,
  // 正则规则成本高（RE2 匹配 + 数量/长度限制），单独限量
  maxRegex: 200,
  // cosmetic 选择器上限：真实规则源（EasyList 等）含数万条元素隐藏规则，
  // 全量注入会拖慢页面；领域限定的优先保留，通用规则限量填充。
  maxCosmetic: 8000,
  // 静态 ruleset 体积敏感：minify 关闭美化缩进，可将产物体积压缩约一半
  minify: false,
  ...parseArgs(process.argv.slice(2))
};
CFG.maxStatic = Number(CFG.maxStatic) || 30000;
CFG.maxRegex = Number(CFG.maxRegex) || 200;
CFG.maxCosmetic = Number(CFG.maxCosmetic) || 8000;
CFG.minify = CFG.minify === "true" || CFG.minify === true;
// 独立 id 段起点：默认 0（从 1 起）；自定义层用 200000+ 与上游/动态规则隔离
CFG.idBase = Number(CFG.idBase) || 0;
// 整体优先级偏移：自定义层用 +100，使其高于上游（block 101 > important 50 > block 1）
CFG.priorityOffset = Number(CFG.priorityOffset) || 0;
// 生成的 cosmetic 全局变量名：上游用 CV_COSMETIC_SELECTORS，自定义用 CV_COSMETIC_CUSTOM
CFG.cosmeticVar = CFG.cosmeticVar || "CV_COSMETIC_SELECTORS";

const abs = (p) => path.resolve(ROOT, p);

/* ============================================================
 * 2. 常量：ABP 选项 → DNR 映射
 * ============================================================ */

/** DNR 合法 resourceTypes（写错会导致整个扩展加载失败） */
const VALID_RESOURCE_TYPES = new Set([
  "main_frame", "sub_frame", "stylesheet", "script", "image", "font",
  "object", "xmlhttprequest", "ping", "csp_report", "media",
  "websocket", "webtransport", "webbundle", "other"
]);

/** ABP 资源选项 → DNR resourceTypes */
const RESOURCE_TYPE_MAP = {
  script: "script",
  image: "image",
  stylesheet: "stylesheet",
  css: "stylesheet",
  subdocument: "sub_frame",
  frame: "sub_frame",
  xmlhttprequest: "xmlhttprequest",
  xhr: "xmlhttprequest",
  font: "font",
  media: "media",
  object: "object",
  ping: "ping",
  websocket: "websocket",
  csp_report: "csp_report",
  other: "other",
  document: "main_frame"
};

/** 中性选项：不影响拦截语义，直接忽略 */
const NEUTRAL_OPTIONS = new Set([
  "collapse", "~collapse", "generichide", "elemhide", "genericblock",
  "~generichide", "~elemhide", "~genericblock"
]);

/** 无法无损映射 → 整条规则跳过（不做猜测转换） */
const UNSUPPORTED_OPTIONS = new Set([
  "redirect", "rewrite", "csp", "header", "sitekey", "popup", "popunder",
  "inline-script", "inline-font", "webrtc", "app", "empty", "mp4",
  "object-subrequest"
]);

/** urlFilter 长度上限（Chrome DNR 限制） */
const MAX_URL_FILTER_LENGTH = 1011;
/** 正则规则长度上限（保守值，避免 RE2 编译成本过高） */
const MAX_REGEX_LENGTH = 500;

/* ============================================================
 * 3. 行解析
 * ============================================================ */

function splitOptions(part) {
  if (!part) return [];
  return part.split(",").map((s) => s.trim()).filter(Boolean);
}

/** 解析 cosmetic 规则：domain##selector / ~domain##.ad / #@# 例外 */
function parseCosmetic(line, skipped) {
  if (line.includes("#@#")) {
    skipped.push("cosmetic 例外(#@#)：需成对处理，保守跳过");
    return null;
  }
  const sep = line.includes("#?#") ? "#?#" : "##";
  const idx = line.indexOf(sep);
  if (idx < 0) {
    skipped.push("无法识别的 cosmetic 语法");
    return null;
  }
  const domainPart = line.slice(0, idx).trim();
  const selector = line.slice(idx + sep.length).trim();
  if (!selector) {
    skipped.push("cosmetic 空选择器");
    return null;
  }

  const domains = [];
  const excludedDomains = [];
  if (domainPart) {
    for (const raw of domainPart.split(",")) {
      const t = raw.trim();
      if (!t) continue;
      if (t.startsWith("~")) excludedDomains.push(t.slice(1));
      else domains.push(t);
    }
  }
  return { selector, domains, excludedDomains };
}

/** 解析单条规则行 */
function parseLine(rawLine, skipped) {
  let line = rawLine.trim();
  if (!line || line.startsWith("!") || line.startsWith("[Adblock")) return null;

  // 3.1 cosmetic / 元素隐藏
  if (line.includes("##") || line.includes("#?#") || line.includes("#@#")) {
    const c = parseCosmetic(line, skipped);
    return c ? { kind: "cosmetic", value: c } : null;
  }

  // 3.2 例外规则 @@
  let isException = false;
  if (line.startsWith("@@")) {
    isException = true;
    line = line.slice(2);
  }

  let pattern;
  let optionsPart = "";
  let isRegex = false;

  // 3.3 正则规则：/pattern/ 或 /pattern/$options
  const regexMatch = line.match(/^\/(.*)\/(?:\$(.*))?$/);
  if (regexMatch) {
    isRegex = true;
    pattern = regexMatch[1];
    optionsPart = regexMatch[2] || "";
  } else {
    // 普通规则：最后一个 $ 之后是选项
    const dollar = line.lastIndexOf("$");
    if (dollar >= 0) {
      pattern = line.slice(0, dollar).trim();
      optionsPart = line.slice(dollar + 1);
    } else {
      pattern = line;
    }
  }

  if (!pattern) {
    skipped.push("空匹配模式");
    return null;
  }
  return {
    kind: "network",
    value: { pattern, isRegex, isException, options: splitOptions(optionsPart) }
  };
}

/* ============================================================
 * 4. network 规则 → DNR 规则对象
 * ============================================================ */

/**
 * ABP 匹配模式 → DNR 条件片段。
 * 关键修正：ABP 的 `||host^` 域名锚点在 DNR 中**无等价 urlFilter 语法**，
 * 原样写入 urlFilter 会导致整条规则永不匹配（DNR 不识别 `||` 与 `^`）。
 * 正确做法：纯 host 锚点用 `requestDomains`（含子域，等价 ABP ||host）；
 * 含路径的 `||host/path` 用 requestDomains + urlFilter 兜底。
 */
function transformPattern(pattern) {
  let p = pattern;
  const out = {};
  if (p.startsWith("||")) {
    p = p.slice(2);
    let end = p.length;
    const slash = p.indexOf("/");
    const caret = p.indexOf("^");
    if (slash >= 0) end = Math.min(end, slash);
    if (caret >= 0) end = Math.min(end, caret);
    const host = p.slice(0, end);
    const rest = p.slice(end).replace(/^\^/, "");
    if (host) out.requestDomains = [host];
    if (rest) {
      const urlFilter = rest.replace(/\^/g, "*"); // ABP 边界近似为通配
      if (urlFilter.length > MAX_URL_FILTER_LENGTH) return null;
      out.urlFilter = urlFilter;
    }
  } else if (p.startsWith("|") && p.endsWith("|")) {
    // 精确地址栏锚定，DNR 原生支持 |...|
    const urlFilter = p.replace(/\^/g, "*");
    if (urlFilter.length > MAX_URL_FILTER_LENGTH) return null;
    out.urlFilter = urlFilter;
  } else {
    const urlFilter = p.replace(/\^/g, "*");
    if (urlFilter.length > MAX_URL_FILTER_LENGTH) return null;
    out.urlFilter = urlFilter;
  }
  return out;
}

function toDnr(parsed, skipped) {
  const { pattern, isRegex, isException, options } = parsed;
  const condition = {};
  const resourceTypes = [];
  const excludedResourceTypes = [];
  let initiatorDomains = [];
  let excludedInitiatorDomains = [];
  let important = false;
  let caseSensitive = false;
  let hasDocumentOption = false;

  // 4.1 匹配模式
  if (isRegex) {
    if (pattern.length > MAX_REGEX_LENGTH) {
      skipped.push("正则规则过长");
      return null;
    }
    condition.regexFilter = pattern;
  } else {
    const transformed = transformPattern(pattern);
    if (transformed === null) {
      skipped.push("urlFilter 超长");
      return null;
    }
    if (transformed.urlFilter) condition.urlFilter = transformed.urlFilter;
    if (transformed.requestDomains) condition.requestDomains = transformed.requestDomains;
  }

  // 4.2 选项解析
  for (const raw of options) {
    const neg = raw.startsWith("~");
    const key = neg ? raw.slice(1) : raw;
    const lower = key.toLowerCase();

    if (NEUTRAL_OPTIONS.has(raw.toLowerCase()) || NEUTRAL_OPTIONS.has(raw)) continue;

    // domain= 限定（ABP 用 | 分隔多个域名）
    if (key.startsWith("domain=")) {
      const list = key.slice("domain=".length).split("|").filter(Boolean);
      if (neg) excludedInitiatorDomains.push(...list);
      else initiatorDomains.push(...list);
      continue;
    }

    // 带参数的选项（如 redirect=nooptext / csp=...）按前缀判定
    const base = lower.split("=")[0];
    if (UNSUPPORTED_OPTIONS.has(lower) || UNSUPPORTED_OPTIONS.has(base)) {
      skipped.push(`不支持的选项: ${raw}`);
      return null;
    }

    if (lower === "important") { important = true; continue; }
    if (lower === "match-case") { caseSensitive = true; continue; }
    if (lower === "third-party") {
      condition.domainType = neg ? "firstParty" : "thirdParty";
      continue;
    }
    if (lower === "document") { hasDocumentOption = true; continue; }

    const mapped = RESOURCE_TYPE_MAP[lower];
    if (mapped) {
      if (neg) excludedResourceTypes.push(mapped);
      else resourceTypes.push(mapped);
      continue;
    }

    // 未知选项：保守跳过，避免语义偏差导致误拦
    skipped.push(`未知选项: ${raw}`);
    return null;
  }

  // 4.3 condition 收尾
  if (resourceTypes.length) condition.resourceTypes = [...new Set(resourceTypes)];
  if (excludedResourceTypes.length) {
    condition.excludedResourceTypes = [...new Set(excludedResourceTypes)];
  }
  if (initiatorDomains.length) condition.initiatorDomains = [...new Set(initiatorDomains)];
  if (excludedInitiatorDomains.length) {
    condition.excludedInitiatorDomains = [...new Set(excludedInitiatorDomains)];
  }
  if (caseSensitive) condition.isUrlFilterCaseSensitive = true;

  // 4.4 action 与 priority
  // priority: 普通 block=1 / important=50 / allow=100
  // 与动态白名单 allow(priority=100) 同级别，DNR 同优先级下 allow 优先，语义一致。
  let actionType = "block";
  let priority = important ? 50 : 1;

  if (isException) {
    // @@ 例外：整页例外用 allowAllRequests，其余用 allow
    if (hasDocumentOption) {
      actionType = "allowAllRequests";
      // DNR 要求 allowAllRequests 的 resourceTypes 为 main_frame/sub_frame 或省略
      condition.resourceTypes = ["main_frame"];
    } else {
      actionType = "allow";
    }
    priority = 100;
  } else if (hasDocumentOption) {
    // 非例外的 $document 语义与 DNR 差异大，保守跳过
    skipped.push("$document 非例外规则语义不明");
    return null;
  }

  // 4.5 裁剪评分：决定「超限时先裁谁」。
  // 关键：纯通用拦截规则（如 ||doubleclick.net^，无选项/无域名限定）是广告拦截的
  // 骨干，必须优先保留；否则像 doubleclick 这类知名网络的拦截会先被裁掉、覆盖率反降。
  // 例外规则强制最高（避免破坏站点）；important 次之；通用骨干高于「带限定的具体规则」；
  // 正则规则成本高、且部分无法无损映射，优先裁。
  const isGenericBlock = !isException && !isRegex && !initiatorDomains.length && !resourceTypes.length;
  const score =
    (isException ? 1000 : 0) +
    (important ? 400 : 0) +
    (isGenericBlock ? 120 : 0) +
    (initiatorDomains.length ? 20 : 0) +
    (resourceTypes.length ? 10 : 0) +
    (isRegex ? -30 : 0);

  return {
    rule: { action: { type: actionType }, priority, condition },
    score,
    isRegex
  };
}

/* ============================================================
 * 5. 输出自检（失败即退出，避免产出非法 ruleset）
 * ============================================================ */

function validateRules(rules) {
  const errors = [];
  const ids = new Set();
  let maxUrlFilterLength = 0;

  rules.forEach((r, i) => {
    if (!Number.isInteger(r.id) || r.id < 1) errors.push(`规则 ${i}: id 非法`);
    if (ids.has(r.id)) errors.push(`规则 ${i}: id 重复 ${r.id}`);
    ids.add(r.id);

    if (!r.action || !["block", "allow", "allowAllRequests"].includes(r.action.type)) {
      errors.push(`规则 ${i}: action.type 非法`);
    }
    if (!Number.isInteger(r.priority)) errors.push(`规则 ${i}: priority 非整数`);

    const c = r.condition || {};
    const hasUrlFilter = typeof c.urlFilter === "string" && c.urlFilter.length > 0;
    const hasRegexFilter = typeof c.regexFilter === "string" && c.regexFilter.length > 0;
    const hasReqDom = Array.isArray(c.requestDomains) && c.requestDomains.length > 0;
    const hasInitDom = Array.isArray(c.initiatorDomains) && c.initiatorDomains.length > 0;
    const hasExReq = Array.isArray(c.excludedRequestDomains) && c.excludedRequestDomains.length > 0;
    const hasExInit = Array.isArray(c.excludedInitiatorDomains) && c.excludedInitiatorDomains.length > 0;
    if (hasUrlFilter && hasRegexFilter) {
      errors.push(`规则 ${i}: urlFilter 与 regexFilter 互斥`);
    }
    if (!hasUrlFilter && !hasRegexFilter && !(hasReqDom || hasInitDom || hasExReq || hasExInit)) {
      errors.push(`规则 ${i}: 缺少 urlFilter/regexFilter 且无域名定位条件`);
    }
    if (hasUrlFilter) {
      maxUrlFilterLength = Math.max(maxUrlFilterLength, c.urlFilter.length);
      if (c.urlFilter.length > MAX_URL_FILTER_LENGTH) errors.push(`规则 ${i}: urlFilter 超长`);
    }
    for (const rt of c.resourceTypes || []) {
      if (!VALID_RESOURCE_TYPES.has(rt)) errors.push(`规则 ${i}: 非法 resourceType ${rt}`);
    }
    for (const rt of c.excludedResourceTypes || []) {
      if (!VALID_RESOURCE_TYPES.has(rt)) errors.push(`规则 ${i}: 非法 excludedResourceType ${rt}`);
    }
    if (c.domainType && !["firstParty", "thirdParty"].includes(c.domainType)) {
      errors.push(`规则 ${i}: domainType 非法`);
    }
  });

  return { errors, maxUrlFilterLength };
}

/* ============================================================
 * 6. 主流程
 * ============================================================ */

function main() {
  const inputPath = abs(CFG.input);
  if (!fs.existsSync(inputPath)) {
    console.error(`[FAIL] 输入文件不存在: ${inputPath}`);
    process.exit(1);
  }

  const lines = fs.readFileSync(inputPath, "utf8").split(/\r?\n/);
  const skipped = [];
  const cosmetic = [];
  const candidates = [];

  for (const raw of lines) {
    const parsed = parseLine(raw, skipped);
    if (!parsed) continue;
    if (parsed.kind === "cosmetic") {
      cosmetic.push(parsed.value);
      continue;
    }
    const converted = toDnr(parsed.value, skipped);
    if (converted) candidates.push(converted);
  }

  // 6.0 cosmetic 裁剪：领域限定的优先（精确、误伤低），通用规则限量填充
  let cosmeticTruncated = 0;
  if (cosmetic.length > CFG.maxCosmetic) {
    const score = (c) => (c.domains ? c.domains.length * 2 : 0) + (c.excludedDomains ? c.excludedDomains.length : 0);
    cosmetic.sort((a, b) => score(b) - score(a));
    cosmeticTruncated = cosmetic.length - CFG.maxCosmetic;
    cosmetic.length = CFG.maxCosmetic;
  }

  // 6.1 裁剪：按评分降序；正则规则单独限量；两类超限分别精确计数
  candidates.sort((a, b) => b.score - a.score);
  let regexCount = 0;
  let regexSkipped = 0;
  let staticLimitSkipped = 0;
  const picked = [];
  for (const c of candidates) {
    if (picked.length >= CFG.maxStatic) {
      staticLimitSkipped += 1;
      continue;
    }
    if (c.isRegex) {
      if (regexCount >= CFG.maxRegex) {
        regexSkipped += 1;
        continue;
      }
      regexCount += 1;
    }
    picked.push(c.rule);
  }

  // 6.2 分配唯一 id（idBase 起；默认 0 → 从 1 起）与优先级偏移。
  // 自定义层传 --id-base 200000（与上游静态、动态白名单/远程规则段隔离），
  // 并传 --priority-offset 100，确保「用户规则说了算」。
  const rules = picked.map((r, i) => ({
    id: CFG.idBase + i + 1,
    priority: (r.priority || 0) + CFG.priorityOffset,
    action: r.action,
    condition: r.condition
  }));

  // 6.3 自检
  const { errors, maxUrlFilterLength } = validateRules(rules);
  if (errors.length) {
    console.error("[FAIL] 输出规则自检未通过：");
    errors.slice(0, 20).forEach((e) => console.error("  -", e));
    process.exit(1);
  }

  // 6.4 写产物
  fs.mkdirSync(path.dirname(abs(CFG.outStatic)), { recursive: true });
  const staticBody = CFG.minify ? JSON.stringify(rules) : JSON.stringify(rules, null, 2);
  fs.writeFileSync(abs(CFG.outStatic), staticBody + (CFG.minify ? "" : "\n"), "utf8");

  fs.mkdirSync(path.dirname(abs(CFG.outCosmetic)), { recursive: true });
  const cosmeticSource =
    "// 本文件由 tools/build-rules.js 自动生成，请勿手工编辑。\n" +
    `// content.js 通过 globalThis.${CFG.cosmeticVar} 读取。\n` +
    `globalThis.${CFG.cosmeticVar} = ` +
    JSON.stringify(cosmetic, null, 2) +
    ";\n";
  fs.writeFileSync(abs(CFG.outCosmetic), cosmeticSource, "utf8");

  // 6.5 构建报告（跳过原因分布可追溯）
  const skipReasons = {};
  for (const reason of skipped) skipReasons[reason] = (skipReasons[reason] || 0) + 1;
  const report = {
    generatedAt: new Date().toISOString(),
    input: CFG.input,
    totalLines: lines.length,
    networkCandidates: candidates.length,
    staticRules: rules.length,
    regexRules: regexCount,
    cosmeticSelectors: cosmetic.length,
    cosmeticTruncated,
    maxCosmetic: CFG.maxCosmetic,
    truncated: staticLimitSkipped > 0 || regexSkipped > 0 || cosmeticTruncated > 0,
    truncatedBy: { staticLimit: staticLimitSkipped, regexLimit: regexSkipped },
    maxUrlFilterLength,
    skippedTotal: skipped.length + staticLimitSkipped + regexSkipped,
    skipReasons,
    checks: { uniqueIds: true, validResourceTypes: true, urlFilterLength: true }
  };
  fs.mkdirSync(path.dirname(abs(CFG.report)), { recursive: true });
  fs.writeFileSync(abs(CFG.report), JSON.stringify(report, null, 2) + "\n", "utf8");

  console.log("[OK] 规则预编译完成");
  console.log(`  静态 DNR 规则   : ${rules.length}  -> ${CFG.outStatic}`);
  console.log(`  Cosmetic 选择器 : ${cosmetic.length}  -> ${CFG.outCosmetic}`);
  console.log(
    `  跳过 / 裁剪     : ${skipped.length + staticLimitSkipped + regexSkipped + cosmeticTruncated} 条` +
    `（语法跳过 ${skipped.length} / 静态超限 ${staticLimitSkipped} / 正则超限 ${regexSkipped} / cosmetic 超限 ${cosmeticTruncated}）`
  );
  console.log(`  构建报告        : ${CFG.report}`);
  if (Object.keys(skipReasons).length) {
    for (const [reason, count] of Object.entries(skipReasons)) {
      console.log(`    · ${reason} × ${count}`);
    }
  }
}

try {
  main();
} catch (err) {
  console.error("[FAIL]", err.message);
  process.exit(1);
}
