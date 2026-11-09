const form = document.querySelector('#download-form');
const urlInput = document.querySelector('#download-url');
const list = document.querySelector('#download-list');
const emptyState = document.querySelector('#empty-state');
const count = document.querySelector('#download-count');
const message = document.querySelector('#message');
let refreshTimer;

const stateLabels = {
  in_progress: 'Downloading',
  interrupted: 'Interrupted',
  complete: 'Complete',
};

function formatBytes(bytes) {
  if (!Number.isFinite(bytes) || bytes < 0) return '—';
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = bytes / 1024;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }
  return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unitIndex]}`;
}

function displayName(download) {
  const path = download.filename || download.url || 'Download';
  return path.split(/[\\/]/).filter(Boolean).pop() || 'Download';
}

function createButton(label, action, downloadId) {
  const button = document.createElement('button');
  button.className = 'action-button';
  button.type = 'button';
  button.textContent = label;
  button.addEventListener('click', async () => {
    button.disabled = true;
    try {
      await action(downloadId);
      message.textContent = '';
      await refreshDownloads();
    } catch (error) {
      message.textContent = error.message || 'That action could not be completed.';
      button.disabled = false;
    }
  });
  return button;
}

function createDownloadItem(download) {
  const item = document.createElement('article');
  item.className = 'download-item';

  const topline = document.createElement('div');
  topline.className = 'download-topline';

  const name = document.createElement('div');
  name.className = 'download-name';
  name.textContent = displayName(download);
  name.title = download.filename || download.url || '';

  const status = document.createElement('span');
  status.className = 'status';
  if (download.state === 'in_progress' || download.paused) status.classList.add('active');
  if (download.state === 'interrupted') status.classList.add('failed');
  status.textContent = download.paused ? 'Paused' : (stateLabels[download.state] || download.state);
  topline.append(name, status);
  item.append(topline);

  const meta = document.createElement('div');
  meta.className = 'download-meta';
  const received = formatBytes(download.bytesReceived);
  const total = download.totalBytes > 0 ? formatBytes(download.totalBytes) : '?';
  const progress = download.totalBytes > 0
    ? Math.min(100, Math.round(download.bytesReceived / download.totalBytes * 100))
    : 0;
  meta.append(
    document.createTextNode(`${received} / ${total}`),
    document.createTextNode(download.error || `${progress}%`),
  );
  item.append(meta);

  if (download.state === 'in_progress') {
    const track = document.createElement('div');
    track.className = 'progress-track';
    track.setAttribute('aria-label', `Download progress: ${progress}%`);
    const value = document.createElement('div');
    value.className = 'progress-value';
    value.style.width = `${progress}%`;
    track.append(value);
    item.append(track);
  }

  const actions = document.createElement('div');
  actions.className = 'download-actions';
  if (download.state === 'in_progress' && !download.paused) {
    actions.append(createButton('Pause', (id) => chrome.downloads.pause(id), download.id));
  } else if (download.paused || (download.state === 'interrupted' && download.canResume)) {
    actions.append(createButton('Resume', (id) => chrome.downloads.resume(id), download.id));
  }
  if (download.state === 'in_progress' || download.paused) {
    actions.append(createButton('Cancel', (id) => chrome.downloads.cancel(id), download.id));
  }
  if (download.state === 'complete') {
    actions.append(createButton('Open', (id) => chrome.downloads.open(id), download.id));
    actions.append(createButton('Show in folder', (id) => chrome.downloads.show(id), download.id));
  }
  if (actions.childElementCount > 0) item.append(actions);
  return item;
}

async function refreshDownloads() {
  try {
    const downloads = await chrome.downloads.search({
      orderBy: ['-startTime'],
      limit: 50,
    });
    list.replaceChildren(...downloads.map(createDownloadItem));
    count.textContent = String(downloads.length);
    emptyState.hidden = downloads.length > 0;
  } catch (error) {
    message.textContent = error.message || 'Could not load downloads.';
  }
}

form.addEventListener('submit', async (event) => {
  event.preventDefault();
  message.textContent = '';
  let url;
  try {
    url = new URL(urlInput.value.trim());
  } catch {
    message.textContent = 'Enter a valid http or https link.';
    return;
  }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    message.textContent = 'Only http and https links are supported.';
    return;
  }

  const submitButton = form.querySelector('button[type="submit"]');
  submitButton.disabled = true;
  try {
    await chrome.downloads.download({ url: url.href, saveAs: true });
    urlInput.value = '';
    await refreshDownloads();
  } catch (error) {
    message.textContent = error.message || 'Could not start this download.';
  } finally {
    submitButton.disabled = false;
  }
});

document.querySelector('#refresh').addEventListener('click', refreshDownloads);
chrome.downloads.onChanged.addListener(() => {
  clearTimeout(refreshTimer);
  refreshTimer = setTimeout(refreshDownloads, 250);
});
refreshDownloads();
