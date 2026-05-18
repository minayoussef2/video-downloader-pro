// Background service worker
const APP_URL = 'http://127.0.0.1:18888';

// Check if desktop app is running
async function checkAppStatus() {
  const urls = [
    'http://localhost:18888/status',
    'http://127.0.0.1:18888/status'
  ];

  for (const url of urls) {
    try {
      const res = await fetch(url, { method: 'GET', mode: 'cors' });
      const data = await res.json();
      if (data.status === 'running') return true;
    } catch (err) {}
  }
  return false;
}

// Send download request to desktop app
async function sendToApp(url, quality, action, type, referer, title) {
  try {
    const endpoint = action === 'download' ? '/download' : '/queue';
    const res = await fetch(`${APP_URL}${endpoint}`, {
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
        title: title || ''
      })
    });
    return await res.json();
  } catch (err) {
    return { error: 'Desktop app connection failed.' };
  }
}

// Global list of recent streams
const recentStreams = [];

// Helper to get tab info (title) for better stream names
async function getTabTitle(tabId) {
  try {
    if (!tabId || tabId < 0) return null;
    const tab = await chrome.tabs.get(tabId);
    return tab?.title;
  } catch {
    return null;
  }
}

async function addSniffedStream(details, type) {
  const url = details.url;
  
  // Prevent duplicates
  if (recentStreams.some(s => s.url === url)) {
    return;
  }

  // Get page title for better naming
  const pageTitle = await getTabTitle(details.tabId);
  const fileName = url.split('/').pop().split('?')[0] || 'Media Stream';
  const displayTitle = pageTitle ? `${pageTitle} (${fileName})` : fileName;

  // Try to find the page origin
  let referer = details.initiator || '';
  if (!referer || referer === 'null') {
    try {
      const urlObj = new URL(url);
      referer = urlObj.origin;
    } catch {}
  }

  const stream = {
    url: url,
    title: `[${type}] ${displayTitle}`,
    type: type,
    thumbnail: '',
    platform: type,
    referer: referer,
    tabId: details.tabId,
    timestamp: Date.now()
  };

  console.log(`[Sniffer] Detected ${type}: ${url}`);
  recentStreams.unshift(stream);
  
  if (recentStreams.length > 50) {
    recentStreams.pop();
  }
}

chrome.webRequest.onBeforeRequest.addListener(
  (details) => {
    let type = null;
    const url = details.url.toLowerCase();
    
    if (url.includes('.m3u8')) type = 'HLS';
    else if (url.includes('.mpd')) type = 'DASH';
    else if (url.includes('.ism/manifest')) type = 'MSS';
    
    if (type) addSniffedStream(details, type);
  },
  { urls: ["<all_urls>"] }
);

chrome.webRequest.onResponseStarted.addListener(
  (details) => {
    let type = null;
    const url = details.url.toLowerCase();
    if (url.includes('.m3u8')) type = 'HLS';
    else if (url.includes('.mpd')) type = 'DASH';
    
    if (type) addSniffedStream(details, type);
  },
  { urls: ["<all_urls>"] }
);

chrome.webRequest.onHeadersReceived.addListener(
  (details) => {
    let type = null;
    const url = details.url.toLowerCase();
    
    const ctHeader = details.responseHeaders?.find(h => h.name.toLowerCase() === 'content-type');
    if (ctHeader) {
      const ct = ctHeader.value.toLowerCase();
      // Expanded detection from fetchv patterns
      if (ct.includes('application/vnd.apple.mpegurl') || ct.includes('application/x-mpegurl')) type = 'HLS';
      else if (ct.includes('application/dash+xml')) type = 'DASH';
      else if (ct.includes('application/vnd.ms-sstr+xml')) type = 'MSS';
      else if (ct.includes('video/') || ct.includes('audio/mpeg') || ct.includes('audio/ogg')) {
          // Avoid small chunks or images that might be mislabeled
          const clHeader = details.responseHeaders?.find(h => h.name.toLowerCase() === 'content-length');
          const size = clHeader ? parseInt(clHeader.value) : 0;
          if (!clHeader || size > 1024 * 100) { // 100KB minimum for media
              type = 'Media';
          }
      }
    }

    if (type) addSniffedStream(details, type);
  },
  { urls: ["<all_urls>"] },
  ["responseHeaders"]
);

// Listen for messages from popup and content scripts
chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg.action === 'checkStatus') {
    checkAppStatus().then(running => sendResponse({ running }));
    return true;
  }
  if (msg.action === 'sendToApp') {
    sendToApp(msg.url, msg.quality, msg.downloadAction, msg.type, msg.referer, msg.title).then(sendResponse);
    return true;
  }
  if (msg.action === 'getSniffedStreams') {
    sendResponse({ streams: recentStreams });
    return true;
  }
  if (msg.action === 'appendStreams') {
    // Merge videos found by content script into the persistent list
    if (msg.streams) {
      msg.streams.forEach(s => {
        // Simple deduplication
        if (!recentStreams.some(existing => existing.url === s.url)) {
          recentStreams.unshift({
            ...s,
            timestamp: Date.now()
          });
        }
      });
      // Enforce limit of 100
      while (recentStreams.length > 100) {
        recentStreams.pop();
      }
    }
    sendResponse({ success: true });
    return true;
  }
});
