#!/usr/bin/env node
/**
 * run-eval.js — 静态拦截覆盖率评测（无浏览器依赖）
 *
 * 做什么：读取评测集(evaluation/sites.json)与当前规则产物(rules/ruleset_core.json)，
 * 用「近似 DNR 语义」的匹配器，对每个样本广告请求判定是否被规则拦截，
 * 输出分类覆盖率，暴露规则缺口。
 *
 * 重要：这是「静态」评测——只验证网络层 DNR 规则能否匹配样本 URL，
 * 不含 cosmetic 层、不含真机渲染、不含缓存命中。真机评测见 README。
 *
 * 用法：node evaluation/run-eval.js [--rules <path>] [--sites <path>]
 */
"use strict";

const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const ROOT = path.resolve(__dirname, "..");
const args = {};
for (let i = 2; i < process.argv.length; i += 2) {
  args[process.argv[i]] = process.argv[i + 1];
}
const RULES = path.resolve(ROOT, args["--rules"] || "rules/ruleset_core.json");
const SITES = path.resolve(ROOT, args["--sites"] || "evaluation/sites.json");

/* ---------- 加载编译后的 cosmetic 选择器（元素隐藏层） ---------- */
function loadCosmetic() {
  const p = path.resolve(ROOT, "src/cosmetic-selectors.js");
  if (!fs.existsSync(p)) return [];
  const src = fs.readFileSync(p, "utf8");
  try {
    const ctx = { globalThis: {} };
    vm.createContext(ctx);
    vm.runInContext(src, ctx);
    return ctx.globalThis.CV_COSMETIC_SELECTORS || [];
  } catch {
    return [];
  }
}
function buildCosmeticDomains(cosmetic) {
  const set = new Set();
  for (const r of cosmetic) {
    for (const d of r.domains || []) set.add(d.toLowerCase());
  }
  return set;
}
function cosmeticApplicable(domains, host) {
  host = (host || "").toLowerCase();
  for (const d of domains) {
    if (host === d || host.endsWith("." + d)) return true;
  }
  return false;
}

/* ---------- 工具：注册域(eTLD+1)近似 ---------- */
const MULTI_TLD = ["com.cn", "net.cn", "org.cn", "gov.cn", "co.uk", "com.au", "com.hk"];
function registrable(host) {
  host = (host || "").toLowerCase();
  for (const tld of MULTI_TLD) {
    if (host === tld || host.endsWith("." + tld)) {
      const parts = host.split(".");
      return parts.slice(-3).join(".");
    }
  }
  const parts = host.split(".");
  return parts.slice(-2).join(".");
}

/* ---------- 工具：DNR urlFilter 匹配（近似） ---------- */
function escapeRe(s) {
  return s.replace(/[.+?^${}()|[\]\\]/g, "\\$&");
}
function matchUrlFilter(pattern, url) {
  if (typeof pattern !== "string" || !pattern) return false;
  let p = pattern;
  let anchorStart = false;
  let anchorEnd = false;
  if (p.startsWith("|")) { anchorStart = true; p = p.slice(1); }
  if (p.endsWith("|")) { anchorEnd = true; p = p.slice(0, -1); }
  const parts = p.split("*").map(escapeRe);
  let re = (anchorStart ? "^" : "") + parts.join(".*") + (anchorEnd ? "$" : "");
  try {
    return new RegExp(re).test(url);
  } catch {
    return false;
  }
}
function domainMatches(list, host) {
  if (!list || !list.length) return true;
  host = (host || "").toLowerCase();
  return list.some((d) => {
    d = d.toLowerCase();
    return host === d || host.endsWith("." + d);
  });
}
function domainExcluded(list, host) {
  if (!list || !list.length) return false;
  host = (host || "").toLowerCase();
  return list.some((d) => host === d || host.endsWith("." + d));
}
function hostOf(url) {
  try { return new URL(url).hostname; } catch { return ""; }
}

/* ---------- 核心：单条样本是否命中某规则 ---------- */
function ruleApplies(rule, sample) {
  const c = rule.condition || {};
  const url = sample.url;
  const urlHost = hostOf(url);
  const initHost = sample.initiator || "";

  if (c.resourceTypes && !c.resourceTypes.includes(sample.resourceType)) return false;
  if (c.excludedResourceTypes && c.excludedResourceTypes.includes(sample.resourceType)) return false;
  if (c.requestDomains && !domainMatches(c.requestDomains, urlHost)) return false;
  if (c.excludedRequestDomains && domainExcluded(c.excludedRequestDomains, urlHost)) return false;
  if (c.initiatorDomains && !domainMatches(c.initiatorDomains, initHost)) return false;
  if (c.excludedInitiatorDomains && domainExcluded(c.excludedInitiatorDomains, initHost)) return false;
  if (c.domainType) {
    const same = registrable(urlHost) === registrable(initHost);
    if (c.domainType === "firstParty" && !same) return false;
    if (c.domainType === "thirdParty" && same) return false;
  }
  if (typeof c.regexFilter === "string") {
    try { if (!new RegExp(c.regexFilter).test(url)) return false; }
    catch { return false; }
  } else if (typeof c.urlFilter === "string") {
    if (!matchUrlFilter(c.urlFilter, url)) return false;
  }
  return true;
}

/* ---------- 核心：判定样本最终状态 ---------- */
function evaluate(sample, rules) {
  let best = null; // {type, priority}
  for (const r of rules) {
    if (!ruleApplies(r, sample)) continue;
    const type = r.action.type;
    const prio = r.priority || 1;
    if (!best) { best = { type, prio }; continue; }
    // 同优先级：allow 优先于 block（DNR 语义）
    if (prio > best.prio) best = { type, prio };
    else if (prio === best.prio && type.startsWith("allow") && !best.type.startsWith("allow")) {
      best = { type, prio };
    }
  }
  if (!best) return "leak"; // 无规则命中 = 漏拦
  return best.type.startsWith("allow") ? "allow" : "block";
}

/* ---------- 主流程 ---------- */
function main() {
  const rules = JSON.parse(fs.readFileSync(RULES, "utf8"));
  const data = JSON.parse(fs.readFileSync(SITES, "utf8"));
  const sites = data.sites;
  const cosmetic = loadCosmetic();
  const cosmeticDomains = buildCosmeticDomains(cosmetic);

  let total = 0, blocked = 0, leak = 0, allowed = 0, combined = 0;
  const byCat = {};
  const byNetwork = {}; // 按广告网络 host 统计命中
  const leaks = [];

  for (const site of sites) {
    byCat[site.category] = byCat[site.category] || { total: 0, blocked: 0, combined: 0 };
    for (const s of site.samples) {
      total++;
      byCat[site.category].total++;
      const net = hostOf(s.url);
      byNetwork[net] = byNetwork[net] || { total: 0, blocked: 0 };
      byNetwork[net].total++;
      const res = evaluate(s, rules);
      const cosmeticHit = cosmeticApplicable(cosmeticDomains, s.initiator);
      if (res === "block") { blocked++; byCat[site.category].blocked++; byNetwork[net].blocked++; }
      else if (res === "allow") { allowed++; }
      else { leak++; leaks.push({ site: site.name, net, type: s.type, party: s.party, url: s.url, cosmetic: cosmeticHit }); }
      // 综合覆盖：网络层拦截 或 站点有专属 cosmetic 元素隐藏规则
      if (res === "block" || cosmeticHit) {
        combined++;
        byCat[site.category].combined++;
      }
    }
  }

  const cov = ((blocked / total) * 100).toFixed(1);
  const covC = ((combined / total) * 100).toFixed(1);
  console.log("=".repeat(60));
  console.log(`规则文件 : ${path.relative(ROOT, RULES)} (${rules.length} 条)`);
  console.log(`评测集   : ${path.relative(ROOT, SITES)} (${sites.length} 站 / ${total} 样本)`);
  console.log("-".repeat(60));
  console.log(`网络层静态覆盖率 : ${blocked}/${total} = ${cov}%  (blocked / total)`);
  console.log(`综合覆盖率(网络∪站点专属cosmetic) : ${combined}/${total} = ${covC}%`);
  console.log(`  漏拦 leak : ${leak}   | 放行 allow : ${allowed}   | cosmetic 站点专属域名 : ${cosmeticDomains.size}`);
  console.log("-".repeat(60));
  console.log("按类别覆盖率 (网络 / 综合):");
  for (const [cat, v] of Object.entries(byCat)) {
    const c = ((v.blocked / v.total) * 100).toFixed(0);
    const cc = ((v.combined / v.total) * 100).toFixed(0);
    console.log(`  ${cat.padEnd(4, "　")} 网络 ${String(v.blocked).padStart(3)}/${String(v.total).padStart(3)} = ${c}%  | 综合 ${cc}%`);
  }
  console.log("-".repeat(60));
  console.log("按广告网络网络层命中率 (前 15 大流量源):");
  const sorted = Object.entries(byNetwork).sort((a, b) => b[1].total - a[1].total).slice(0, 15);
  for (const [net, v] of sorted) {
    const c = ((v.blocked / v.total) * 100).toFixed(0);
    const tag = v.blocked === 0 ? "  ← 完全漏拦" : "";
    console.log(`  ${net.padEnd(28)} ${String(v.blocked).padStart(3)}/${String(v.total).padStart(3)} = ${c}%${tag}`);
  }
  console.log("-".repeat(60));
  console.log(`漏拦样本示例 (前 8/${leak}，cosmetic=是否有站点专属元素隐藏规则):`);
  for (const l of leaks.slice(0, 8)) {
    console.log(`  [${l.party}] ${l.site} → ${l.net} (${l.type}) cosmetic=${l.cosmetic}`);
  }
  console.log("=".repeat(60));
  // 给 CI / 后续判定用：输出 JSON
  fs.mkdirSync(path.dirname(path.resolve(ROOT, "evaluation/eval-last.json")), { recursive: true });
  fs.writeFileSync(path.resolve(ROOT, "evaluation/eval-last.json"),
    JSON.stringify({ total, blocked, leak, allowed, combined, coveragePct: Number(cov), combinedPct: Number(covC), byCat, leaks: leaks.length }, null, 2));
  console.log(`明细已写 evaluation/eval-last.json`);
}

main();
