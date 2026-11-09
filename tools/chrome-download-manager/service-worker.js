const DOWNLOAD_LINK_MENU_ID = 'fetch-desk-download-link';

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({
      id: DOWNLOAD_LINK_MENU_ID,
      title: 'Download link with Fetch Desk',
      contexts: ['link'],
    });
  });
});

chrome.contextMenus.onClicked.addListener(async (info) => {
  if (info.menuItemId !== DOWNLOAD_LINK_MENU_ID || !info.linkUrl) return;

  try {
    await chrome.downloads.download({
      url: info.linkUrl,
      saveAs: true,
    });
  } catch (error) {
    console.error('Could not start link download:', error);
  }
});
