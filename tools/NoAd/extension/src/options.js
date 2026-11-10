/** options.js — 设置页：通用开关、白名单、规则订阅、隐私（FR-04/06/07） */
import { getSettings, setSettings, hostnameOf } from "./shared.js";

const $ = (id) => document.getElementById(id);
const POLICY_URL = "https://【待填】/privacy"; // 上线前替换为公开可访问的隐私政策 URL

function notifySettingsChanged() {
  chrome.runtime.sendMessage({ type: "SETTINGS_CHANGED" }).catch(() => {});
}

function flash(message) {
  $("status").textContent = message;
  setTimeout(() => { $("status").textContent = ""; }, 3000);
}

/** 输入归一化：允许用户粘贴完整 URL，只保留 hostname */
function normalizeDomain(raw) {
  const trimmed = (raw || "").trim().toLowerCase();
  if (!trimmed) return "";
  const withScheme = /^https?:\/\//.test(trimmed) ? trimmed : `https://${trimmed}`;
  try {
    return hostnameOf(withScheme);
  } catch {
    return "";
  }
}

async function render() {
  const settings = await getSettings();
  $("globalToggle").checked = settings.enabled;
  $("statsToggle").checked = settings.statsEnabled;
  $("rulesUrl").value = settings.rulesUrl || "";

  const list = $("domainList");
  list.innerHTML = "";
  if (settings.whitelist.length === 0) {
    const li = document.createElement("li");
    li.textContent = "暂无白名单站点";
    list.appendChild(li);
    return;
  }
  for (const domain of settings.whitelist) {
    const li = document.createElement("li");
    const name = document.createElement("span");
    name.textContent = domain;
    const btn = document.createElement("button");
    btn.textContent = "移除";
    btn.addEventListener("click", async () => {
      const s = await getSettings();
      await setSettings({ whitelist: s.whitelist.filter((d) => d !== domain) });
      notifySettingsChanged();
      await render();
      flash(`已移除 ${domain}`);
    });
    li.append(name, btn);
    list.appendChild(li);
  }
}

/* ---------- 通用开关 ---------- */
$("globalToggle").addEventListener("change", async (e) => {
  await setSettings({ enabled: e.target.checked });
  notifySettingsChanged();
  flash(e.target.checked ? "拦截已启用" : "拦截已停用");
});

/* ---------- 白名单 ---------- */
$("addDomain").addEventListener("click", async () => {
  const domain = normalizeDomain($("domainInput").value);
  if (!domain) return flash("请输入合法域名");
  const s = await getSettings();
  if (s.whitelist.includes(domain)) return flash("该域名已在白名单");
  await setSettings({ whitelist: [...s.whitelist, domain] });
  $("domainInput").value = "";
  notifySettingsChanged();
  await render();
  flash(`已添加 ${domain}`);
});

$("domainInput").addEventListener("keydown", (e) => {
  if (e.key === "Enter") $("addDomain").click();
});

/* ---------- 规则订阅 ---------- */
$("saveRulesUrl").addEventListener("click", async () => {
  const url = $("rulesUrl").value.trim();
  if (url && !/^https:\/\//.test(url)) return flash("规则地址须为 HTTPS");
  await setSettings({ rulesUrl: url });
  flash(url ? "已保存，将按每日周期更新" : "已清空，不再拉取远程规则");
});

$("updateRules").addEventListener("click", () => {
  chrome.runtime.sendMessage({ type: "MANUAL_RULE_UPDATE" }).catch(() => {});
  flash("已触发更新，结果见 Service Worker 控制台");
});

/* ---------- 隐私 ---------- */
$("statsToggle").addEventListener("change", async (e) => {
  await setSettings({ statsEnabled: e.target.checked });
  flash(e.target.checked ? "匿名统计已开启" : "匿名统计已关闭，不再上报任何数据");
});

$("resetId").addEventListener("click", async () => {
  const resp = await chrome.runtime.sendMessage({ type: "RESET_DEVICE_ID" });
  flash(resp?.deviceId ? "匿名标识已重置，新旧标识不可关联" : "重置失败");
});

$("openPolicy").addEventListener("click", () => {
  chrome.tabs.create({ url: POLICY_URL });
});

/* ---------- FR-06 首启告知 ---------- */
if (new URLSearchParams(location.search).get("welcome") === "1") {
  $("welcomeBanner").style.display = "block";
}
$("welcomeKeep").addEventListener("click", () => {
  $("welcomeBanner").style.display = "none";
  flash("感谢，统计保持开启（可随时在此关闭）");
});
$("welcomeDisable").addEventListener("click", async () => {
  await setSettings({ statsEnabled: false });
  $("statsToggle").checked = false;
  $("welcomeBanner").style.display = "none";
  flash("已关闭匿名统计，本扩展不会发送任何网络请求");
});

render();
