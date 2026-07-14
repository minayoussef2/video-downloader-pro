// ============================================================================
// Background Service Worker — Video Downloader Pro v3.2.0
// ============================================================================
// This service worker handles:
//   1. Companion app port discovery & communication (async, race-condition-free)
//   2. Network request sniffing for HLS / MPD / Direct video streams
//   3. Browser download interception (NeatDownloadManager style)
//   4. Message routing between content scripts, popup, and the WPF app
// ============================================================================

// ---------------------------------------------------------------------------
// Port Management — Fully async to prevent Manifest V3 service worker races
// ---------------------------------------------------------------------------

/**
 * Retrieve the cached app port from chrome.storage.local.
 * Defaults to 18888 if nothing is stored yet.
 * Using storage instead of a global variable avoids stale reads when the
 * service worker wakes up after being killed by Chrome.
 */
async function getAppPort() {
  try {
    const data = await chrome.storage.local.get('appPort');
    return (data && data.appPort) ? data.appPort : 18888;
  } catch {
    return 18888;
  }
}

/**
 * Persist a newly discovered port so future service worker wake-ups use it.
 */
async function setAppPort(port) {
  try {
    await chrome.storage.local.set({ appPort: port });
  } catch {}
}

// ---------------------------------------------------------------------------
// oEmbed — Resolve real titles & thumbnails for YouTube / Vimeo embeds
// ---------------------------------------------------------------------------

async function fetchOEmbedInfo(watchUrl, platform) {
  try {
    let oEmbedUrl = null;
    if (platform === 'YouTube') {
      oEmbedUrl = `https://www.youtube.com/oembed?url=${encodeURIComponent(watchUrl)}&format=json`;
    } else if (platform === 'Vimeo') {
      oEmbedUrl = `https://vimeo.com/api/oembed.json?url=${encodeURIComponent(watchUrl)}`;
    }
    if (!oEmbedUrl) return null;

    const res = await fetch(oEmbedUrl);
    if (!res.ok) return null;
    const data = await res.json();
    return {
      title: data.title || null,
      thumbnail: data.thumbnail_url || null
    };
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// Nested URL Extraction — Detect video URLs embedded inside query parameters
// ---------------------------------------------------------------------------

/**
 * Some sites load videos through an intermediate API iframe whose src contains
 * a query parameter with the actual watch URL, e.g.:
 *   https://api.example.com/player?url=https://www.youtube.com/watch?v=xyz
 *
 * This helper extracts the inner video URL when it matches a known platform.
 */
function extractNestedVideoUrl(rawUrl) {
  if (!rawUrl) return null;
  try {
    const urlObj = new URL(rawUrl);
    for (const [, value] of urlObj.searchParams.entries()) {
      if (value.startsWith('http://') || value.startsWith('https://')) {
        const valLower = value.toLowerCase();
        if (valLower.includes('youtube.com/watch') || valLower.includes('youtube.com/embed') ||
            valLower.includes('youtu.be/') || valLower.includes('vimeo.com') ||
            valLower.includes('dailymotion.com') || valLower.includes('rumble.com')) {
          return value;
        }
      }
    }
  } catch {}
  return null;
}

// ---------------------------------------------------------------------------
// Extension Install — Set sensible defaults in chrome.storage
// ---------------------------------------------------------------------------

chrome.runtime.onInstalled.addListener(() => {
  chrome.storage.local.set({
    autoIntercept: true,
    excludedSites: [],
    ctrlPressed: false,
    ctrlPressedTime: 0
  });
});

// ---------------------------------------------------------------------------
// App Status Check — Probes ports 18888-18892 to find the WPF companion app
// ---------------------------------------------------------------------------

/**
 * Try the cached port first; if that fails, scan the full range.
 * Persists the discovered port for future use.
 */
async function checkAppStatus() {
  const cachedPort = await getAppPort();

  // 1. Try the cached port
  try {
    const res = await fetch(`http://127.0.0.1:${cachedPort}/status`, { method: 'GET', mode: 'cors' });
    const data = await res.json();
    if (data && data.status === 'running') {
      return true;
    }
  } catch {}

  // 2. Probe ports 18888–18892
  for (let port = 18888; port <= 18892; port++) {
    if (port === cachedPort) continue; // already tried above
    try {
      const res = await fetch(`http://127.0.0.1:${port}/status`, { method: 'GET', mode: 'cors' });
      const data = await res.json();
      if (data && data.status === 'running') {
        await setAppPort(port);
        console.log(`[Background] Found app running on port ${port}`);
        return true;
      }
    } catch {}
  }
  return false;
}

// ---------------------------------------------------------------------------
// Send to App — Forward a download/queue request to the WPF companion
// ---------------------------------------------------------------------------

async function sendToApp(url, quality, action, type, referer, title, thumbnail) {
  try {
    // Confirm app is alive and refresh port if needed
    await checkAppStatus();
    const port = await getAppPort();
    const endpoint = action === 'download' ? '/download' : '/queue';
    const res = await fetch(`http://127.0.0.1:${port}${endpoint}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Access-Control-Allow-Private-Network': 'true'
      },
      body: JSON.stringify({
        url,
        quality,
        type: type || 'Page',
        referer: referer || '',
        title: title || '',
        thumbnail: thumbnail || ''
      })
    });
    return await res.json();
  } catch (err) {
    return { error: 'Desktop app connection failed.' };
  }
}

// ---------------------------------------------------------------------------
// Tab Helpers
// ---------------------------------------------------------------------------

async function getTabTitle(tabId) {
  try {
    if (!tabId || tabId < 0) return null;
    const tab = await chrome.tabs.get(tabId);
    return tab?.title;
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// Recent Streams — In-memory list of sniffed video streams
// ---------------------------------------------------------------------------
const recentStreams = [];

/**
 * Register a newly sniffed stream with rich metadata (title, size, referer).
 * Deduplicates by URL and caps the list at 100 entries.
 */
async function addSniffedStream(details, type, sizeBytes = 0, customFilename = '') {
  const url = details.url;

  // Deduplicate
  if (recentStreams.some(s => s.url === url)) return;

  // Resolve a human-readable title
  const pageTitle = await getTabTitle(details.tabId);

  let fileName = customFilename || 'Media Stream';
  if (!customFilename) {
    try {
      const parsedUrl = new URL(url);
      fileName = parsedUrl.pathname.split('/').pop().split('?')[0] || 'Media Stream';
    } catch {}
  }

  // Friendly names for well-known CDNs
  if (url.includes('googlevideo.com')) {
    fileName = 'YouTube Media Block';
  } else if (url.includes('instagram.com') || url.includes('cdninstagram.com')) {
    fileName = 'Instagram Video Block';
  }

  const displayTitle = pageTitle ? `${pageTitle} (${fileName})` : fileName;

  // Resolve referer / origin
  let referer = details.initiator || '';
  if (!referer || referer === 'null') {
    try {
      const urlObj = new URL(url);
      referer = urlObj.origin;
    } catch {}
  }

  // Format file size
  let formattedSize = '';
  if (sizeBytes > 0) {
    if (sizeBytes >= 1024 * 1024 * 1024) {
      formattedSize = `${(sizeBytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
    } else if (sizeBytes >= 1024 * 1024) {
      formattedSize = `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`;
    } else {
      formattedSize = `${(sizeBytes / 1024).toFixed(0)} KB`;
    }
  }

  const stream = {
    url: url,
    title: `[${type}] ${displayTitle}`,
    type: type,
    thumbnail: '',
    platform: type,
    referer: referer,
    tabId: details.tabId,
    size: formattedSize,
    timestamp: Date.now()
  };

  console.log(`[Sniffer] Detected ${type} (${formattedSize}): ${url}`);
  recentStreams.unshift(stream);
  if (recentStreams.length > 100) recentStreams.pop();
}

// ===========================================================================
// 1. NETWORK LISTENER — Detect HLS / MPD / Direct Video / Embedded Platforms
// ===========================================================================

chrome.webRequest.onHeadersReceived.addListener(
  (details) => {
    let type = null;
    const url = details.url.toLowerCase();

    // Skip HLS/DASH fragment requests (segments, range chunks, .m4s)
    if (url.includes('/segment') || url.includes('/fragment') ||
        url.includes('/chunk') || url.includes('range/') || url.includes('.m4s')) {
      return;
    }

    // --- Nested Video URL detection in network requests ---
    const nestedUrl = extractNestedVideoUrl(details.url);
    if (nestedUrl) {
      const nestedLower = nestedUrl.toLowerCase();
      if (nestedLower.includes('youtube.com/watch') || nestedLower.includes('youtu.be/')) {
        const match = nestedUrl.match(/(?:v=|youtu\.be\/)([a-zA-Z0-9_-]{11})/);
        if (match && !recentStreams.some(s => s.url.includes(match[1]))) {
          const videoId = match[1];
          const watchUrl = `https://www.youtube.com/watch?v=${videoId}`;
          const stream = {
            url: watchUrl,
            title: `YouTube Video (${videoId})`,
            type: 'YouTube',
            thumbnail: `https://img.youtube.com/vi/${videoId}/hqdefault.jpg`,
            platform: 'YouTube',
            referer: details.initiator || '',
            tabId: details.tabId,
            size: '',
            timestamp: Date.now()
          };
          recentStreams.unshift(stream);
          if (recentStreams.length > 100) recentStreams.pop();

          fetchOEmbedInfo(watchUrl, 'YouTube').then(info => {
            if (info && info.title) {
              stream.title = info.title;
              if (info.thumbnail) stream.thumbnail = info.thumbnail;
            }
          });
        }
        return; // Already handled
      }
    }

    // --- Embedded YouTube iframes ---
    if (url.includes('youtube.com/embed/') || url.includes('youtube-nocookie.com/embed/')) {
      const match = details.url.match(/\/embed\/([a-zA-Z0-9_-]{11})/);
      if (match) {
        const videoId = match[1];
        const watchUrl = `https://www.youtube.com/watch?v=${videoId}`;

        if (!recentStreams.some(existing => existing.url === watchUrl)) {
          const stream = {
            url: watchUrl,
            title: `YouTube Video (${videoId})`,
            type: 'YouTube',
            thumbnail: `https://img.youtube.com/vi/${videoId}/hqdefault.jpg`,
            platform: 'YouTube',
            referer: details.initiator || '',
            tabId: details.tabId,
            size: '',
            timestamp: Date.now()
          };
          console.log(`[Sniffer] Detected Embedded YouTube: ${watchUrl}`);
          recentStreams.unshift(stream);
          if (recentStreams.length > 100) recentStreams.pop();

          fetchOEmbedInfo(watchUrl, 'YouTube').then(info => {
            if (info && info.title) {
              stream.title = info.title;
              if (info.thumbnail) stream.thumbnail = info.thumbnail;
            }
          });
        }
      }
    }

    // --- Embedded Vimeo iframes ---
    if (url.includes('player.vimeo.com/video/')) {
      const match = details.url.match(/\/video\/([0-9]+)/);
      if (match) {
        const videoId = match[1];
        const watchUrl = `https://vimeo.com/${videoId}`;

        if (!recentStreams.some(existing => existing.url === watchUrl)) {
          const stream = {
            url: watchUrl,
            title: `Vimeo Video (${videoId})`,
            type: 'Vimeo',
            thumbnail: '',
            platform: 'Vimeo',
            referer: details.initiator || '',
            tabId: details.tabId,
            size: '',
            timestamp: Date.now()
          };
          console.log(`[Sniffer] Detected Embedded Vimeo: ${watchUrl}`);
          recentStreams.unshift(stream);
          if (recentStreams.length > 100) recentStreams.pop();

          fetchOEmbedInfo(watchUrl, 'Vimeo').then(info => {
            if (info && info.title) {
              stream.title = info.title;
              if (info.thumbnail) stream.thumbnail = info.thumbnail;
            }
          });
        }
      }
    }

    // --- Skip YouTube / Instagram CDN noise ---
    const initiator = details.initiator ? details.initiator.toLowerCase() : '';
    if (url.includes('youtube.com') || url.includes('googlevideo.com') ||
        url.includes('instagram.com') || url.includes('cdninstagram.com') ||
        initiator.includes('youtube.com') || initiator.includes('instagram.com')) {
      return;
    }

    // --- Content-Disposition filename extraction ---
    const cdHeader = details.responseHeaders?.find(h => h.name.toLowerCase() === 'content-disposition');
    let cdFilename = '';
    if (cdHeader && cdHeader.value) {
      const fnMatch = cdHeader.value.match(/filename\*?=(?:UTF-8'')?["']?([^;"'\n]+)["']?/i);
      if (fnMatch) {
        try { cdFilename = decodeURIComponent(fnMatch[1]).trim(); }
        catch { cdFilename = fnMatch[1].trim(); }
      }
    }

    // --- Content-Type & Content-Length ---
    const ctHeader = details.responseHeaders?.find(h => h.name.toLowerCase() === 'content-type');
    const clHeader = details.responseHeaders?.find(h => h.name.toLowerCase() === 'content-length');
    const size = clHeader ? parseInt(clHeader.value) : 0;

    const mediaExtensions = [
      '.mp4', '.mkv', '.webm', '.ts', '.flv', '.avi', '.mov', '.wmv', '.m4v', '.3gp',
      '.mp3', '.m4a', '.wav', '.ogg', '.aac', '.flac', '.opus',
      '.srt', '.vtt', '.ass', '.ssa', '.sub'
    ];

    if (ctHeader) {
      const ct = ctHeader.value.toLowerCase();

      // Skip small HLS .ts transport stream fragments
      if (ct.includes('video/mp2t') || ct.includes('video/mp2s')) {
        const tsSize = clHeader ? parseInt(clHeader.value) : NaN;
        if (isNaN(tsSize) || tsSize < 1 * 1024 * 1024) return;
      }

      if (url.includes('.ts') || url.includes('.ts?')) {
        const tsSize = clHeader ? parseInt(clHeader.value) : NaN;
        if (isNaN(tsSize) || tsSize < 1 * 1024 * 1024) return;
      }

      // Stream format detection by MIME type
      if (ct.includes('application/vnd.apple.mpegurl') || ct.includes('application/x-mpegurl')) {
        type = 'HLS';
      } else if (ct.includes('application/dash+xml')) {
        type = 'DASH';
      } else if (ct.includes('application/vnd.ms-sstr+xml')) {
        type = 'MSS';
      } else if (ct.includes('video/') || ct.includes('audio/mpeg') || ct.includes('audio/ogg') ||
                 ct.includes('audio/mp4') || ct.includes('audio/aac')) {
        if (!clHeader || size > 100 * 1024) {
          type = 'Media';
        }
      } else if (ct.includes('application/octet-stream') || ct.includes('binary/octet-stream') ||
                 ct.includes('application/download') || ct.includes('application/force-download')) {
        if (cdFilename && mediaExtensions.some(ext => cdFilename.toLowerCase().endsWith(ext))) {
          type = 'Media';
        }
      }
    }

    // Fallback: URL/filename extension sniffing
    if (!type) {
      if (url.includes('.m3u8')) type = 'HLS';
      else if (url.includes('.mpd')) type = 'DASH';
      else if (url.includes('.ism/manifest')) type = 'MSS';
      else {
        // Ignore bare .ts extensions (likely HLS segments)
        if (url.split('?')[0].endsWith('.ts')) return;

        const checkName = cdFilename || url.split('?')[0];
        if (mediaExtensions.some(ext => checkName.toLowerCase().endsWith(ext))) {
          type = 'Media';
        }
      }
    }

    if (type) {
      addSniffedStream(details, type, size, cdFilename);
    }
  },
  { urls: ["<all_urls>"] },
  ["responseHeaders"]
);

// ===========================================================================
// 2. BROWSER DOWNLOAD INTERCEPTOR — NeatDownloadManager style
// ===========================================================================

chrome.downloads.onCreated.addListener(async (downloadItem) => {
  // Read user preferences
  const prefs = await chrome.storage.local.get(['autoIntercept', 'excludedSites', 'ctrlPressed', 'ctrlPressedTime']);

  const autoIntercept = prefs.autoIntercept !== false;
  if (!autoIntercept) return;

  // Bypass if Ctrl was held down (within 1.5s)
  const isCtrlHeld = prefs.ctrlPressed || (Date.now() - (prefs.ctrlPressedTime || 0) < 1500);
  if (isCtrlHeld) {
    console.log('[Interceptor] Bypass triggered by Ctrl key.');
    return;
  }

  // Check site exclusions
  let originHost = '';
  try {
    const urlObj = new URL(downloadItem.referrer || downloadItem.url);
    originHost = urlObj.hostname.toLowerCase();
  } catch {}

  const isExcluded = prefs.excludedSites?.some(site => originHost.includes(site) || site.includes(originHost));
  if (isExcluded) {
    console.log(`[Interceptor] Bypass triggered: ${originHost} is on the exclusion list.`);
    return;
  }

  // Check if file extension or MIME matches media types
  const urlLower = downloadItem.url.toLowerCase().split('?')[0];
  const fileExts = [
    '.mp4', '.mkv', '.webm', '.ts', '.flv', '.avi', '.mov', '.wmv', '.m4v', '.3gp',
    '.mp3', '.m4a', '.wav', '.ogg', '.aac', '.flac', '.opus',
    '.srt', '.vtt', '.ass', '.ssa', '.sub'
  ];

  const filename = (downloadItem.filename || '').toLowerCase();
  const mimeLower = (downloadItem.mime || '').toLowerCase();
  const matchesMime = mimeLower.startsWith('video/') || mimeLower.startsWith('audio/') ||
                      mimeLower.includes('subrip') || mimeLower.includes('vtt') || mimeLower.includes('subtitle');
  const matchesExt = fileExts.some(ext => urlLower.endsWith(ext) || filename.endsWith(ext));

  if (!matchesExt && !matchesMime) return;

  // Confirm the desktop app is online
  const appRunning = await checkAppStatus();
  if (!appRunning) {
    console.log('[Interceptor] Desktop app is offline. Allowing browser to download.');
    return;
  }

  console.log(`[Interceptor] Intercepting download: ${downloadItem.url}`);

  // Cancel Chrome's default download and forward to the WPF app
  chrome.downloads.cancel(downloadItem.id, () => {
    chrome.downloads.erase({ id: downloadItem.id });
  });

  const title = downloadItem.filename || 'Downloaded File';
  sendToApp(downloadItem.url, 'Best', 'download', 'Direct File', downloadItem.referrer, title, '');
});

// ===========================================================================
// 3. MESSAGE ROUTER — Popup, Content Scripts, and App communication
// ===========================================================================

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  // Status check from popup
  if (msg.action === 'checkStatus') {
    checkAppStatus().then(running => sendResponse({ running }));
    return true;
  }

  // Forward download/queue to desktop app
  if (msg.action === 'sendToApp') {
    sendToApp(msg.url, msg.quality, msg.downloadAction, msg.type, msg.referer, msg.title, msg.thumbnail)
      .then(sendResponse);
    return true;
  }

  // Return all sniffed streams to popup
  if (msg.action === 'getSniffedStreams') {
    sendResponse({ streams: recentStreams });
    return true;
  }

  // Append streams detected by content scripts
  if (msg.action === 'appendStreams') {
    if (msg.streams) {
      msg.streams.forEach(s => {
        if (!recentStreams.some(existing => existing.url === s.url)) {
          recentStreams.unshift({
            ...s,
            timestamp: Date.now()
          });
        }
      });
      while (recentStreams.length > 100) recentStreams.pop();
    }
    sendResponse({ success: true });
    return true;
  }
});
