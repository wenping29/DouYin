const MENU_ACTIONS = [
  { id: "summarize", title: "SP：总结本页", contexts: ["page", "selection"] },
  { id: "outline", title: "SP：列大纲", contexts: ["page", "selection"] },
  { id: "translate", title: "SP：译成中文", contexts: ["selection"] },
  { id: "explain", title: "SP：解释划线", contexts: ["selection"] },
  { id: "polish", title: "SP：润色改写", contexts: ["selection"] },
];

chrome.runtime.onInstalled.addListener(() => {
  enablePanelBehavior();
  registerMenus();
});

chrome.runtime.onStartup.addListener(enablePanelBehavior);

function enablePanelBehavior() {
  chrome.sidePanel.setPanelBehavior({ openPanelOnActionClick: true }).catch(() => {});
}

function registerMenus() {
  chrome.contextMenus.removeAll(() => {
    for (const item of MENU_ACTIONS) {
      chrome.contextMenus.create({ id: item.id, title: item.title, contexts: item.contexts });
    }
  });
}

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (!tab?.id) return;
  launchAction({ actionId: info.menuItemId, selection: info.selectionText || "", tab });
});

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg?.type === "sp-action" || msg?.type === "sp-launch") {
    launchAction({
      actionId: msg.actionId,
      view: msg.view,
      selection: msg.selection,
      tab: sender.tab,
    })
      .then(() => sendResponse({ ok: true }))
      .catch((err) => sendResponse({ ok: false, error: String(err) }));
    return true;
  }
  if (msg?.type === "sp-toggle-chat") {
    toggleChat(sender.tab)
      .then(sendResponse)
      .catch((err) => sendResponse({ ok: false, error: String(err) }));
    return true;
  }
  if (msg?.type === "sp-panel-state") {
    probePanel(sender.tab?.windowId)
      .then((panel) => sendResponse({ open: !!panel, view: panel?.view || "" }))
      .catch(() => sendResponse({ open: false }));
    return true;
  }
  return false;
});

// 侧边栏没有“是否已打开”的查询接口：向扩展页面发探针，活着的那份会带着自己的窗口号回应。
// 没有面板页面时 sendMessage 会直接拒绝，正好当作“已关闭”。
const PANEL_PROBE_TIMEOUT = 400;

async function probePanelOnce(windowId) {
  try {
    const reply = await Promise.race([
      chrome.runtime.sendMessage({ type: "sp-panel-probe", windowId }),
      new Promise((_, reject) => setTimeout(() => reject(new Error("probe timeout")), PANEL_PROBE_TIMEOUT)),
    ]);
    return reply?.open ? reply : null;
  } catch {
    return null;
  }
}

// 面板刚打开时 sidepanel.js 可能还没执行到监听器，第一次探针会落空。
// 注意不能在落空后重试：open() 已经同步发出，重试会看到刚打开的面板，把第一次点击误判成“关闭”。
async function probePanel(windowId) {
  if (typeof windowId !== "number") return null;
  return probePanelOnce(windowId);
}

// 关闭优先让面板页面自己 window.close()：Chrome / Edge 都支持，也不依赖 sidePanel.close。
// 同时再调一次官方的 sidePanel.close() 兜底，两条路任意一条成功即可。
async function closePanel(windowId) {
  let selfClosed = false;
  try {
    const reply = await Promise.race([
      chrome.runtime.sendMessage({ type: "sp-panel-close", windowId }),
      new Promise((_, reject) => setTimeout(() => reject(new Error("close timeout")), 600)),
    ]);
    selfClosed = !!reply?.closed;
  } catch {
    // 面板可能已经不在，继续走 sidePanel.close 兜底
  }
  if (typeof chrome.sidePanel.close === "function") {
    try {
      await chrome.sidePanel.close({ windowId });
      return true;
    } catch (err) {
      if (!selfClosed) console.warn("关闭侧边栏失败", err?.message || err);
    }
  }
  return selfClosed;
}

async function toggleChat(tab) {
  const windowId = tab?.windowId;
  if (typeof windowId !== "number") return { ok: false, error: "缺少窗口信息" };

  // Service Worker 里 await 过任何东西，用户手势就没了，之后的 sidePanel.open() 会被拒绝。
  // 所以先把探针和 pending 启动起来，再同步调 open()（本来就在聊天视图时它只是空操作），
  // 等探针回来再决定是否 close() 把它收回去。
  const pending = {
    actionId: null,
    view: "chat",
    selection: "",
    title: tab?.title || "",
    url: tab?.url || "",
    windowId,
    at: Date.now(),
  };
  const written = chrome.storage.local.set({ pending });
  const panel = probePanel(windowId);
  chrome.sidePanel.open({ windowId }).catch((err) => {
    console.warn("打开侧边栏失败", err?.message || err);
  });

  const open = await panel;
  if (open) {
    // 面板已经开着，这次点击就是“关上”：撤掉刚写的 pending，别留给下次。
    // 不管当前停在哪个视图都直接关闭，保证悬浮球上的聊天按钮是真正的开关。
    await written;
    await chrome.storage.local.remove("pending");
    const closed = await closePanel(windowId);
    if (!closed) return { ok: false, error: "关闭侧边栏失败", open: true };
    return { ok: true, open: false };
  }

  // 没开就打开
  await written;
  try {
    await chrome.runtime.sendMessage({ type: "pending-ready" });
  } catch (err) {
    // 面板还没起来，boot() 会自己读 pending
    console.warn("通知面板失败", err?.message || err);
  }
  return { ok: true, open: true };
}

// 划词/悬浮按钮：打开停靠侧边栏必须在丢手势之前调用，所以 open() 是第一个 await。
async function launchAction({ actionId, view, selection, tab }) {
  const windowId = tab?.windowId;
  if (typeof windowId !== 'number') return;

  const pending = {
    actionId: actionId || null,
    view: view || null,
    selection: (selection || "").trim(),
    title: tab?.title || "",
    url: tab?.url || "",
    windowId,
    at: Date.now(),
  };
  const written = chrome.storage.local.set({ pending });
  try {
    await chrome.sidePanel.open({ windowId });
  } catch (err) {
    console.warn("打开侧边栏失败", err?.message || err);
  }
  await written;
  try {
    await chrome.runtime.sendMessage({ type: "pending-ready" });
  } catch (err) {
    // 面板还没起来，下一步它会自己读 pending
    console.warn("通知面板失败", err?.message || err);
  }
}
