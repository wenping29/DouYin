# Fetch Desk

Fetch Desk is a small Chrome Manifest V3 extension for managing ordinary browser downloads. It uses Chrome's `downloads` API and provides a compact, original interface rather than copying another product's branding or screen designs.

## Install for local testing

1. Open `chrome://extensions` in Google Chrome.
2. Turn on **Developer mode**.
3. Choose **Load unpacked** and select this directory.
4. Pin **Fetch Desk** from the Extensions menu if you want quick access.

## Features

- Start an HTTP/HTTPS download by pasting a direct link.
- Right-click a link on a page and choose **Download link with Fetch Desk**.
- Show a **获取视频源** button over the largest visible video; inspect and copy the direct URL exposed by its video element.
- View recent download progress and status.
- Pause, resume, or cancel active downloads.
- Open a completed file or reveal it in the file manager.

## Scope and permissions

The extension uses the `downloads` permission to start and manage browser downloads, `contextMenus` to add the link context-menu action, and HTTP/HTTPS site access to place a source button on pages with video elements (including embedded frames). Chrome will ask you to grant access to sites. It only reads the video element's `currentSrc`/`src` and metadata when you use the button. A `blob:` address is an internal browser media reference, not a direct downloadable file; this extension does not inspect network traffic, extract segmented streams, or bypass DRM, authentication, paywalls, rate limits, or site access controls. Use it only for resources you are authorized to download; the extension remains subject to Chrome and the source website's restrictions.

After updating the extension at `chrome://extensions`, reload the extension and refresh the video page. The button appears at the upper-right of the largest visible video. Some players do not expose a direct video URL; in that case, the extension explains that no directly readable address is available.
