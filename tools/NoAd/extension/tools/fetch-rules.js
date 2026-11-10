#!/usr/bin/env node
/**
 * fetch-rules.js — 规则源获取与编译流水线（零依赖，Node >= 18）
 *
 * 作用：
 *   读取 rules-sources.json 中启用的公开规则源 → 下载并拼接 → 调用 build-rules.js
 *   编译为 MV3 静态 DNR 规则 + cosmetic 选择器，覆盖到扩展实际使用的文件。
 *
 * 为什么这样设计（许可证红线）：
 *   公开规则源（EasyList 等）受各自许可证约束，**不应把完整规则内容提交进仓库**。
 *   本脚本在「构建期」下载并生成产物，且生成文件已被 .gitignore 忽略；
 *   产物须随上游许可证署名（见 NOTICE）。商业发布前请确认各源许可证的
 *   商业使用条款（EasyList 系默认禁止商业使用，须获许可）。
 *
 * 用法：
 *   node tools/fetch-rules.js                  # 用 rules-sources.json 默认配置
 *   node tools/fetch-rules.js --max-static 100000 --max-cosmetic 8000
 *   node tools/fetch-rules.js --only easylist,easylist-china
 *   node tools/fetch-rules.js --dry-run        # 只下载，不编译（调试用）
 *
 * 产物（均被 .gitignore 忽略，不入库）：
 *   rules/ruleset_core.json        ← 扩展使用的静态规则
 *   src/cosmetic-selectors.js      ← 扩展使用的元素隐藏选择器
 *   tools/build-report.json        ← 编译报告
 *   filters/_fetched/              ← 下载的原始源（中间文件）
 */

"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { execSync } = require("node:child_process");

const ROOT = path.resolve(__dirname, "..");
const abs = (p) => path.resolve(ROOT, p);

/* ---------- CLI ---------- */
const args = {};
for (let i = 2; i < process.argv.length; i += 2) {
  const k = process.argv[i];
  const v = process.argv[i + 1];
  if (!k || !k.startsWith("--")) throw new Error(`非法参数: ${k}`);
  if (v === undefined || v.startsWith("--")) throw new Error(`参数 ${k} 缺少取值`);
  args[k.slice(2)] = v;
}

const DRY_RUN = args["dry-run"] === "true" || args["dry-run"] === "1";
const ONLY = args.only ? args.only.split(",").map((s) => s.trim()) : null;

/* ---------- 读取源配置 ---------- */
const cfgPath = abs("rules-sources.json");
if (!fs.existsSync(cfgPath)) {
  console.error(`[FAIL] 找不到规则源配置: ${cfgPath}`);
  process.exit(1);
}
const sourcesCfg = JSON.parse(fs.readFileSync(cfgPath, "utf8"));
const maxStatic = Number(args["max-static"]) || Number(sourcesCfg.defaultMaxStatic) || 100000;
const maxCosmetic = Number(args["max-cosmetic"]) || Number(sourcesCfg.defaultMaxCosmetic) || 8000;

const enabled = sourcesCfg.sources.filter((s) => s.enabled && (!ONLY || ONLY.includes(s.name)));
if (!enabled.length) {
  console.error("[FAIL] 没有启用的规则源（检查 rules-sources.json 的 enabled 或 --only）");
  process.exit(1);
}

/* ---------- 下载单个源（带超时与 UA） ---------- */
async function downloadSource(src) {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), 60000);
  try {
    const res = await fetch(src.url, {
      signal: ctrl.signal,
      redirect: "follow",
      headers: { "User-Agent": "CleanView-RuleFetcher/0.1 (+https://example.com)" }
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const text = await res.text();
    return text;
  } finally {
    clearTimeout(timer);
  }
}

async function main() {
  const fetchedDir = abs("filters/_fetched");
  fs.mkdirSync(fetchedDir, { recursive: true });
  const combinedPath = abs("filters/_fetched.txt");
  const parts = [];
  let okCount = 0;

  console.log(`[1/3] 下载启用的规则源 (${enabled.length} 个)…`);
  for (const src of enabled) {
    process.stdout.write(`  · ${src.name.padEnd(18)} ${src.url} … `);
    try {
      const text = await downloadSource(src);
      const safe = src.name.replace(/[^a-z0-9_-]/gi, "_");
      fs.writeFileSync(path.join(fetchedDir, `${safe}.txt`), text, "utf8");
      parts.push(`\n! ===== source: ${src.name} (${src.url}) =====\n` + text);
      okCount++;
      console.log(`OK (${(text.length / 1024).toFixed(0)} KB)`);
    } catch (err) {
      console.log(`FAIL (${err.message}) — 跳过，继续其余源`);
    }
  }

  if (!okCount) {
    console.error("\n[FAIL] 所有规则源下载失败，无法继续。");
    process.exit(1);
  }

  fs.writeFileSync(combinedPath, parts.join("\n"), "utf8");
  console.log(`\n[2/3] 拼接完成 → ${path.relative(ROOT, combinedPath)} (${(parts.join("").length / 1024 / 1024).toFixed(1)} MB)`);

  if (DRY_RUN) {
    console.log("\n[dry-run] 仅下载，跳过编译。");
    return;
  }

  console.log(`\n[3/3] 编译为 MV3 DNR 规则 (maxStatic=${maxStatic}, maxCosmetic=${maxCosmetic})…`);

  // 3.a 上游核心规则集
  const coreCmd = [
    "node", path.relative(ROOT, abs("tools/build-rules.js")),
    "--input", path.relative(ROOT, combinedPath),
    "--out-static", "rules/ruleset_core.json",
    "--out-cosmetic", "src/cosmetic-selectors.js",
    "--report", "tools/build-report.json",
    "--max-static", String(maxStatic),
    "--max-cosmetic", String(maxCosmetic),
    "--minify", "true"
  ].join(" ");
  try {
    const out = execSync(coreCmd, { cwd: ROOT, stdio: "pipe" }).toString();
    process.stdout.write(out);
  } catch (err) {
    console.error("\n[FAIL] 核心规则编译失败：");
    console.error((err.stdout || "").toString());
    console.error((err.stderr || "").toString());
    process.exit(1);
  }

  // 3.b 用户自定义规则集（独立 id 段 200000+，priority +100，确保高于上游）
  const customPath = sourcesCfg.customFilterPath;
  if (customPath && fs.existsSync(abs(customPath))) {
    const customCmd = [
      "node", path.relative(ROOT, abs("tools/build-rules.js")),
      "--input", path.relative(ROOT, abs(customPath)),
      "--out-static", "rules/ruleset_custom.json",
      "--out-cosmetic", "src/cosmetic-custom.js",
      "--report", "tools/build-report-custom.json",
      "--id-base", "200000",
      "--priority-offset", "100",
      "--cosmetic-var", "CV_COSMETIC_CUSTOM",
      "--max-static", "50000",
      "--max-cosmetic", "10000",
      "--minify", "true"
    ].join(" ");
    try {
      console.log(`\n[3.b] 编译用户自定义规则 → ${customPath}`);
      const out = execSync(customCmd, { cwd: ROOT, stdio: "pipe" }).toString();
      process.stdout.write(out);
    } catch (err) {
      console.error("\n[FAIL] 自定义规则编译失败：");
      console.error((err.stdout || "").toString());
      console.error((err.stderr || "").toString());
      process.exit(1);
    }
  } else {
    console.warn(`\n[3.b] 未找到自定义规则文件 ${customPath}，跳过（如需自定义请在 rules-sources.json 配置 customFilterPath）`);
  }

  // 产物体积提示
  const ruleSize = (fs.statSync(abs("rules/ruleset_core.json")).size / 1024 / 1024).toFixed(1);
  const selSize = (fs.statSync(abs("src/cosmetic-selectors.js")).size / 1024 / 1024).toFixed(1);
  console.log(`\n[完成] 产物体积  ruleset_core.json ≈ ${ruleSize} MB ｜ cosmetic-selectors.js ≈ ${selSize} MB`);
  if (fs.existsSync(abs("rules/ruleset_custom.json"))) {
    const cSize = (fs.statSync(abs("rules/ruleset_custom.json")).size / 1024 / 1024).toFixed(2);
    console.log(`          ruleset_custom.json ≈ ${cSize} MB（用户自定义，优先级高于上游）`);
  }
  console.log("注意：生成文件已被 .gitignore 忽略，不入库；发布前请按 NOTICE 添加上游许可证署名。");
}

main().catch((err) => {
  console.error("[FAIL]", err.message);
  process.exit(1);
});
