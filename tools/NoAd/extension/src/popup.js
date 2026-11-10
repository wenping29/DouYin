/** popup.js — 弹窗：全局开关、当日统计、当前站点暂停（FR-04 / FR-05） */
import {
  getSettings, setSettings, getStats, hostnameOf, isWhitelisted
} from "./shared.js";

const $ = (id) => document.getElementById(id);

/** 通知 background 重建动态规则 */
function notifySettingsChanged() {
  chrome.runtime.sendMessage({ type: "SETTINGS_CHANGED" }).catch(() => {});
}

/** 获取用户当前正在看的标签页域名（activeTab 权限，popup 打开属于用户手势） */
async function currentHost() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  return tab?.url ? hostnameOf(tab.url) : "";
}

async function render() {
  const settings = await getSettings();
  const stats = await getStats();
  const host = await currentHost();

  $("globalToggle").checked = settings.enabled;
  $("todayBlocked").textContent = String(stats.totalBlocked);
  $("todayHidden").textContent = String(stats.totalHidden);
  $("statsHint").textContent = `匿名统计：${settings.statsEnabled ? "开启" : "已关闭"}`;

  if (!host) {
    $("siteName").textContent = "无法获取（非网页标签页）";
    $("sitePause").disabled = true;
    $("siteState").textContent = "";
    return;
  }

  $("siteName").textContent = host;
  const paused = isWhitelisted(host, settings.whitelist);
  $("sitePause").textContent = paused ? "恢复本域拦截" : "暂停本域";
  $("siteState").textContent = paused ? "该域已加入白名单" : "该域正在拦截";
  $("sitePause").dataset.host = host;
  $("sitePause").dataset.paused = paused ? "1" : "0";
}

/* 全局开关 */
$("globalToggle").addEventListener("change", async (e) => {
  await setSettings({ enabled: e.target.checked });
  notifySettingsChanged();
  await render();
});

/* 当前站点暂停 / 恢复 */
$("sitePause").addEventListener("click", async (e) => {
  const host = e.target.dataset.host;
  if (!host) return;
  const settings = await getSettings();
  const paused = e.target.dataset.paused === "1";
  const next = paused
    ? settings.whitelist.filter((d) => d !== host)
    : [...new Set([...settings.whitelist, host])];
  await setSettings({ whitelist: next });
  notifySettingsChanged();
  await render();
});

/* 打开设置页 */
$("openOptions").addEventListener("click", (e) => {
  e.preventDefault();
  chrome.runtime.openOptionsPage();
});

render();
