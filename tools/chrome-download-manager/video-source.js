(() => {
  const host = document.createElement('div');
  host.setAttribute('data-fetch-desk-root', '');
  const shadow = host.attachShadow({ mode: 'closed' });
  shadow.innerHTML = `
    <style>
      :host { all: initial; }
      .button {
        position: fixed;
        z-index: 2147483647;
        display: none;
        padding: 9px 13px;
        border: 1px solid #ffffff44;
        border-radius: 9px;
        background: #3448c5;
        box-shadow: 0 3px 14px #0005;
        color: #fff;
        cursor: pointer;
        font: 600 13px/1.2 "Segoe UI", Arial, sans-serif;
        white-space: nowrap;
      }
      .button:hover { background: #293ba8; }
      .panel {
        position: fixed;
        z-index: 2147483647;
        display: none;
        width: min(330px, calc(100vw - 24px));
        padding: 14px;
        border: 1px solid #e4e8f1;
        border-radius: 12px;
        background: #fff;
        box-shadow: 0 8px 30px #0003;
        color: #172033;
        font: 13px/1.45 "Segoe UI", Arial, sans-serif;
      }
      .panel.open { display: block; }
      .heading {
        display: flex;
        align-items: center;
        justify-content: space-between;
        margin-bottom: 9px;
        font-size: 14px;
        font-weight: 700;
      }
      .close {
        border: 0;
        background: transparent;
        color: #778198;
        cursor: pointer;
        font: 20px/1 Arial, sans-serif;
      }
      .source {
        display: block;
        width: 100%;
        min-height: 54px;
        padding: 8px;
        border: 1px solid #e1e5ee;
        border-radius: 7px;
        color: #3448c5;
        font: 11px/1.4 Consolas, monospace;
        overflow-wrap: anywhere;
        resize: vertical;
      }
      .details { margin: 8px 0 11px; color: #6f7990; font-size: 11px; }
      .copy {
        width: 100%;
        padding: 9px;
        border: 0;
        border-radius: 8px;
        background: #3448c5;
        color: white;
        cursor: pointer;
        font: 600 12px "Segoe UI", Arial, sans-serif;
      }
      .copy:hover { background: #293ba8; }
      .notice { margin-top: 8px; color: #8a5b18; font-size: 11px; }
      .notice:empty { display: none; }
    </style>
    <button class="button" type="button">获取视频源</button>
    <section class="panel" aria-label="视频源信息">
      <div class="heading">
        <span>视频源信息</span>
        <button class="close" type="button" aria-label="关闭">×</button>
      </div>
      <textarea class="source" readonly aria-label="视频源地址"></textarea>
      <p class="details"></p>
      <button class="copy" type="button">复制来源地址</button>
      <p class="notice" role="status" aria-live="polite"></p>
    </section>
  `;
  (document.documentElement || document).append(host);

  const button = shadow.querySelector('.button');
  const panel = shadow.querySelector('.panel');
  const sourceField = shadow.querySelector('.source');
  const details = shadow.querySelector('.details');
  const notice = shadow.querySelector('.notice');
  let activeVideo = null;
  let updateFrame = 0;
  let mutationTimer = 0;

  function getSource(video) {
    return video.currentSrc || video.src || video.querySelector('source[src]')?.src || '';
  }

  function visibleArea(video) {
    const rect = video.getBoundingClientRect();
    if (rect.width < 50 || rect.height < 50) return 0;
    const style = getComputedStyle(video);
    if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity) === 0) {
      return 0;
    }
    const width = Math.max(0, Math.min(rect.right, innerWidth) - Math.max(rect.left, 0));
    const height = Math.max(0, Math.min(rect.bottom, innerHeight) - Math.max(rect.top, 0));
    return width * height;
  }

  function updateButtonPosition() {
    updateFrame = 0;
    let bestVideo = null;
    let bestArea = 0;

    for (const video of document.querySelectorAll('video')) {
      const area = visibleArea(video);
      if (area > bestArea) {
        bestArea = area;
        bestVideo = video;
      }
    }

    activeVideo = bestVideo;
    if (!activeVideo) {
      button.style.display = 'none';
      panel.classList.remove('open');
      return;
    }

    const rect = activeVideo.getBoundingClientRect();
    button.style.display = 'block';
    const buttonWidth = button.offsetWidth;
    const buttonHeight = button.offsetHeight;
    const left = Math.max(8, Math.min(innerWidth - buttonWidth - 8, rect.right - buttonWidth - 10));
    const top = Math.max(8, Math.min(innerHeight - buttonHeight - 8, rect.top + 10));
    button.style.left = `${left}px`;
    button.style.top = `${top}px`;

    if (panel.classList.contains('open')) {
      const panelWidth = panel.offsetWidth || 330;
      const panelLeft = Math.max(8, Math.min(innerWidth - panelWidth - 8, left + buttonWidth - panelWidth));
      panel.style.left = `${panelLeft}px`;
      panel.style.top = `${Math.max(8, Math.min(innerHeight - 210, top + buttonHeight + 8))}px`;
    }
  }

  function schedulePositionUpdate() {
    if (updateFrame) return;
    updateFrame = requestAnimationFrame(updateButtonPosition);
  }

  function openPanel() {
    if (!activeVideo) return;
    const source = getSource(activeVideo);
    sourceField.value = source || '未检测到可读取的视频地址';
    const duration = Number.isFinite(activeVideo.duration)
      ? `${Math.floor(activeVideo.duration)} 秒`
      : '未知';
    details.textContent = `${activeVideo.videoWidth || '未知'} × ${activeVideo.videoHeight || '未知'} · 时长 ${duration}`;
    notice.textContent = source.startsWith('blob:')
      ? '这是浏览器内部媒体地址，不是可直接下载的文件链接。此工具不会提取或还原分段/加密流。'
      : source
        ? '仅显示页面公开给浏览器的视频元素地址；下载仍受网站权限和访问规则限制。'
        : '此页面未向视频元素公开直接地址，或视频使用了无法读取的播放方式。';
    panel.classList.add('open');
    schedulePositionUpdate();
  }

  button.addEventListener('click', openPanel);
  shadow.querySelector('.close').addEventListener('click', () => panel.classList.remove('open'));
  shadow.querySelector('.copy').addEventListener('click', async () => {
    const source = sourceField.value;
    if (!source || source === '未检测到可读取的视频地址' || source.startsWith('blob:')) {
      notice.textContent = '没有可复制的直接下载地址。';
      return;
    }
    try {
      await navigator.clipboard.writeText(source);
      notice.textContent = '来源地址已复制。';
    } catch (error) {
      sourceField.focus();
      sourceField.select();
      notice.textContent = '浏览器未允许自动复制，请手动复制选中的地址。';
      console.warn('Fetch Desk could not write to clipboard:', error);
    }
  });

  window.addEventListener('scroll', schedulePositionUpdate, true);
  window.addEventListener('resize', schedulePositionUpdate);
  document.addEventListener('play', schedulePositionUpdate, true);
  document.addEventListener('loadedmetadata', schedulePositionUpdate, true);

  const observer = new MutationObserver(() => {
    clearTimeout(mutationTimer);
    mutationTimer = setTimeout(schedulePositionUpdate, 150);
  });
  observer.observe(document.documentElement, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: ['src', 'style', 'class'],
  });

  schedulePositionUpdate();
})();
