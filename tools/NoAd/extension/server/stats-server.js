#!/usr/bin/env node
"use strict";
/**
 * stats-server.js — 匿名统计后端（prod-ready 自托管参考实现，零依赖）
 * ----------------------------------------------------------------------------
 * 运行：STATS_AUTH_TOKEN=xxx node server/stats-server.js
 *
 * 环境变量：
 *   PORT / HOST          监听地址（默认 8787 / 127.0.0.1；prod 用 0.0.0.0 + HTTPS 前置）
 *   STATS_AUTH_TOKEN     上报 Bearer 令牌；**为空 = 关闭鉴权（仅 dev）**
 *   STATS_DATA_DIR       原始日志目录（默认 cwd/stats-data，勿打进扩展包）
 *   RETENTION_DAYS       原始日志留存天数（默认 30，对应隐私政策第 5 节）
 *   RATE_LIMIT_PER_MIN   单 device_id 每分钟上报上限（默认 10，防伪造轰炸）
 *   ALLOWED_ORIGIN       CORS 允许的 Origin；prod 设为 chrome-extension://<扩展ID>
 *
 * 合规落地（对应隐私政策第 5 节 / PRD 第十一节数据合规 8 项）：
 *   - 字段白名单：与 PRD 10.2 一致，越权字段一律 400
 *   - 最小化 / 去标识：仅存聚合字段，device_id 随机且与身份无关
 *   - 留存清理：原始日志按日文件存储，> RETENTION_DAYS 自动删除
 *   - 审计：audit.log 记录脱敏来源 IP + 时间 + 结果（不记录 PII）
 *   - 防滥用：每 device_id 速率限制（多实例部署时建议改用 Redis，见 README）
 *   - 访问控制：Bearer token + CORS Origin 白名单
 *
 * 注：这是「自托管参考实现」。生产环境建议前置 HTTPS 反向代理 + 托管数据库
 *     （如 Supabase / 云函数），并将 STATS_AUTH_TOKEN 通过构建/环境变量注入。
 */

const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");

const PORT = Number(process.env.PORT) || 8787;
const HOST = process.env.HOST || "127.0.0.1";
const AUTH_TOKEN = process.env.STATS_AUTH_TOKEN || "";
const DATA_DIR = process.env.STATS_DATA_DIR || path.join(process.cwd(), "stats-data");
const RETENTION_DAYS = Number(process.env.RETENTION_DAYS) || 30;
const RATE_LIMIT_PER_MIN = Number(process.env.RATE_LIMIT_PER_MIN) || 10;
const ALLOWED_ORIGIN = process.env.ALLOWED_ORIGIN || "*";

/** 与 PRD 第十节 10.2 一致的白名单字段 */
const ALLOWED_TOP_KEYS = new Set([
  "device_id", "date", "total_blocked", "top_domains",
  "rule_hit_distribution", "client_version"
]);
/** 明确禁止出现的关键词（大小写不敏感） */
const FORBIDDEN_HINTS = ["url", "path", "query", "email", "phone", "ip", "token", "cookie", "title", "text"];

fs.mkdirSync(DATA_DIR, { recursive: true });

/* ---------------- 速率限制（内存，单实例） ---------------- */
const rateBuckets = new Map(); // device_id -> [ts, ...]
function rateLimited(deviceId) {
  const now = Date.now();
  const arr = (rateBuckets.get(deviceId) || []).filter((t) => now - t < 60000);
  if (arr.length >= RATE_LIMIT_PER_MIN) { rateBuckets.set(deviceId, arr); return true; }
  arr.push(now);
  rateBuckets.set(deviceId, arr);
  return false;
}

/* ---------------- 工具 ---------------- */
function maskIp(ip) {
  if (!ip) return "unknown";
  if (ip.includes(":")) return ip.slice(0, ip.lastIndexOf(":") + 1) + "…"; // IPv6 截断
  const p = ip.split(".");
  if (p.length !== 4) return "masked";
  return `${p[0]}.${p[1]}.${p[2]}.0`; // IPv4 末位置 0
}

/** 校验 payload：顶层字段白名单 + 嵌套白名单 + 禁用词扫描 */
function validate(payload) {
  if (!payload || typeof payload !== "object" || Array.isArray(payload)) return "payload 非对象";
  for (const key of Object.keys(payload)) {
    if (!ALLOWED_TOP_KEYS.has(key)) return `未授权字段: ${key}`;
    if (FORBIDDEN_HINTS.some((h) => key.toLowerCase().includes(h))) return `疑似敏感字段: ${key}`;
  }
  if (typeof payload.device_id !== "string" || !/^[0-9a-f-]{36}$/i.test(payload.device_id)) {
    return "device_id 非随机 UUID 格式";
  }
  if (!/^\d{4}-\d{2}-\d{2}$/.test(payload.date || "")) return "date 格式非法";
  if (!Number.isInteger(payload.total_blocked) || payload.total_blocked < 0) return "total_blocked 非法";
  if (!Array.isArray(payload.top_domains)) return "top_domains 非数组";
  for (const item of payload.top_domains) {
    const keys = Object.keys(item || {});
    if (keys.some((k) => !["domain", "count"].includes(k))) return "top_domains 含多余字段";
    if (typeof item.domain !== "string" || item.domain.includes("/") || item.domain.includes("?")) {
      return "top_domains 疑似含完整 URL";
    }
    if (!Number.isInteger(item.count) || item.count < 0) return "top_domains.count 非法";
  }
  if (!payload.rule_hit_distribution || typeof payload.rule_hit_distribution !== "object") {
    return "rule_hit_distribution 非法";
  }
  return null;
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    let raw = "";
    req.on("data", (chunk) => {
      raw += chunk;
      if (raw.length > 64 * 1024) reject(new Error("payload 过大"));
    });
    req.on("end", () => {
      try { resolve(JSON.parse(raw)); } catch { reject(new Error("非法 JSON")); }
    });
    req.on("error", reject);
  });
}

/* ---------------- 持久化 / 留存 / 聚合 ---------------- */
function rawFileFor(date) { return path.join(DATA_DIR, `raw-${date}.log`); }

function appendRaw(entry) {
  fs.appendFileSync(rawFileFor(entry.date), JSON.stringify(entry) + "\n");
}

function audit(action, meta) {
  const line = JSON.stringify({ ts: new Date().toISOString(), action, ...meta }) + "\n";
  try { fs.appendFileSync(path.join(DATA_DIR, "audit.log"), line); } catch {}
}

/** 留存清理：删除 mtime 早于 RETENTION_DAYS 的原始日志文件（按日切分） */
function purgeOld(now = Date.now()) {
  const cutoff = now - RETENTION_DAYS * 86400000;
  let removed = 0;
  try {
    for (const f of fs.readdirSync(DATA_DIR)) {
      if (!/^raw-\d{4}-\d{2}-\d{2}\.log$/.test(f)) continue;
      const fp = path.join(DATA_DIR, f);
      if (fs.statSync(fp).mtimeMs < cutoff) { fs.unlinkSync(fp); removed++; }
    }
  } catch {}
  return removed;
}

/** 聚合：扫描现存原始日志（量级小时实时聚合；大流量建议改为预聚合任务） */
function loadAggregate() {
  const agg = { requests: 0, totalBlocked: 0, domains: new Map(), versions: new Map() };
  try {
    for (const f of fs.readdirSync(DATA_DIR)) {
      if (!/^raw-\d{4}-\d{2}-\d{2}\.log$/.test(f)) continue;
      const lines = fs.readFileSync(path.join(DATA_DIR, f), "utf8").split("\n").filter(Boolean);
      for (const ln of lines) {
        try {
          const e = JSON.parse(ln);
          agg.requests += 1;
          agg.totalBlocked += e.total_blocked || 0;
          for (const { domain, count } of (e.top_domains || [])) {
            agg.domains.set(domain, (agg.domains.get(domain) || 0) + count);
          }
          agg.versions.set(e.client_version, (agg.versions.get(e.client_version) || 0) + 1);
        } catch {}
      }
    }
  } catch {}
  return agg;
}

function serializeAggregate() {
  const a = loadAggregate();
  return {
    requests: a.requests,
    totalBlocked: a.totalBlocked,
    topDomains: [...a.domains.entries()].sort((x, y) => y[1] - x[1]).slice(0, 20),
    versions: [...a.versions.entries()]
  };
}

/* ---------------- HTTP 服务 ---------------- */
const server = http.createServer(async (req, res) => {
  res.setHeader("Access-Control-Allow-Origin", ALLOWED_ORIGIN);
  res.setHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");
  res.setHeader("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
  if (req.method === "OPTIONS") return res.writeHead(204).end();

  if (req.method === "GET" && req.url === "/health") {
    return res.writeHead(200, { "Content-Type": "application/json" })
      .end(JSON.stringify({ ok: true, retainedDays: RETENTION_DAYS, aggregate: serializeAggregate() }));
  }

  if (req.method !== "POST" || req.url !== "/v1/stats") return res.writeHead(404).end();

  // 1) 鉴权（prod 必须；dev 空 token 跳过）
  if (AUTH_TOKEN) {
    const auth = req.headers["authorization"] || "";
    if (auth !== `Bearer ${AUTH_TOKEN}`) {
      audit("reject", { reason: "auth", srcIp: maskIp(req.socket.remoteAddress) });
      return res.writeHead(401, { "Content-Type": "application/json" }).end(JSON.stringify({ error: "未授权" }));
    }
  }

  let payload;
  try {
    payload = await readBody(req);
  } catch (e) {
    return res.writeHead(400, { "Content-Type": "application/json" }).end(JSON.stringify({ error: e.message }));
  }

  // 2) 速率限制
  if (rateLimited(payload?.device_id || "anon")) {
    return res.writeHead(429, { "Content-Type": "application/json" }).end(JSON.stringify({ error: "速率超限" }));
  }

  // 3) 字段白名单校验
  const err = validate(payload);
  if (err) {
    audit("reject", { reason: err, srcIp: maskIp(req.socket.remoteAddress) });
    return res.writeHead(400, { "Content-Type": "application/json" }).end(JSON.stringify({ error: err }));
  }

  // 4) 落盘（仅聚合字段，device_id 仅随机 UUID，无 PII）
  const entry = {
    received_at: new Date().toISOString(),
    date: payload.date,
    device_id: payload.device_id,
    total_blocked: payload.total_blocked,
    top_domains: payload.top_domains,
    rule_hit_distribution: payload.rule_hit_distribution,
    client_version: payload.client_version,
    src_ip_masked: maskIp(req.socket.remoteAddress)
  };
  try {
    appendRaw(entry);
    audit("accept", { deviceId8: payload.device_id.slice(0, 8), srcIp: entry.src_ip_masked });
  } catch (e) {
    return res.writeHead(500, { "Content-Type": "application/json" }).end(JSON.stringify({ error: "写入失败" }));
  }

  res.writeHead(200, { "Content-Type": "application/json" }).end(JSON.stringify({ ok: true }));
});

/* ---------------- 启动 ---------------- */
if (require.main === module) {
  const removed = purgeOld();
  console.log(`[CleanView] 留存清理：已删除 ${removed} 个过期原始日志文件（> ${RETENTION_DAYS} 日）`);
  setInterval(purgeOld, 24 * 3600 * 1000).unref(); // 每日清理
  server.listen(PORT, HOST, () => {
    console.log(`[CleanView] 统计服务已启动: http://${HOST}:${PORT}`);
    console.log(`  鉴权: ${AUTH_TOKEN ? "开启(Bearer)" : "关闭(dev)"} | 留存: ${RETENTION_DAYS} 日 | 速率: ${RATE_LIMIT_PER_MIN}/min`);
  });
}

module.exports = { validate, purgeOld, loadAggregate, maskIp, ALLOWED_TOP_KEYS };
