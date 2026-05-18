// Background service worker - Video Downloader Pro v1.0.0
const APP_URL = 'http://127.0.0.1:18888';
const recentStreams = [];

// Default configuration stored in chrome.storage.local
chrome.runtime.onInstalled.addListener(() => {
  chrome.storage.local.set({
    autoIntercept: true,
    excludedSites: [],
    ctrlPressed: false,
    ctrlPressedTime: 0
  });
});

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
async function sendToApp(url, quality, action, type, referer, title, thumbnail) {
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
        title: title || '',
        thumbnail: thumbnail || ''
      })
    });
    return await res.json();
  } catch (err) {
    return { error: 'Desktop app connection failed.' };
  }
}

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

// Add a stream to our recent list with rich metadata
async function addSniffedStream(details, type, sizeBytes = 0) {
  const url = details.url;
  
  // Exclude duplicate URLs
  if (recentStreams.some(s => s.url === url)) {
    return;
  }

  // Get page title for better naming
  const pageTitle = await getTabTitle(details.tabId);
  
  let fileName = 'Media Stream';
  try {
    const parsedUrl = new URL(url);
    fileName = parsedUrl.pathname.split('/').pop().split('?')[0] || 'Media Stream';
  } catch {}
  
  // If it's a YouTube googlevideo URL, name it nicely
  if (url.includes('googlevideo.com')) {
    fileName = 'YouTube Media Block';
  } else if (url.includes('instagram.com') || url.includes('cdninstagram.com')) {
    fileName = 'Instagram Video Block';
  }

  const displayTitle = pageTitle ? `${pageTitle} (${fileName})` : fileName;

  // Try to find the page origin / referer
  let referer = details.initiator || '';
  if (!referer || referer === 'null') {
    try {
      const urlObj = new URL(url);
      referer = urlObj.origin;
    } catch {}
  }

  // Calculate formatted size
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
  
  if (recentStreams.length > 100) {
    recentStreams.pop();
  }
}

// 1. LISTEN TO NETWORKS & DETECT COMPLEX STREAMS (YouTube, Instagram, HLS, DASH, Direct Video)
chrome.webRequest.onHeadersReceived.addListener(
  (details) => {
    let type = null;
    const url = details.url.toLowerCase();
    
    // Sniff embedded YouTube videos via iframe requests
    if (url.includes('youtube.com/embed/') || url.includes('youtube-nocookie.com/embed/')) {
      const match = details.url.match(/\/embed\/([a-zA-Z0-9_-]{11})/);
      if (match) {
        const videoId = match[1];
        const watchUrl = `https://www.youtube.com/watch?v=${videoId}`;
        
        const stream = {
          url: watchUrl,
          title: `Embedded YouTube Video (${videoId})`,
          type: 'YouTube',
          thumbnail: `https://img.youtube.com/vi/${videoId}/hqdefault.jpg`,
          platform: 'YouTube',
          referer: details.initiator || '',
          tabId: details.tabId,
          size: '',
          timestamp: Date.now()
        };

        if (!recentStreams.some(existing => existing.url === watchUrl)) {
          console.log(`[Sniffer] Detected Embedded YouTube: ${watchUrl}`);
          recentStreams.unshift(stream);
          if (recentStreams.length > 100) recentStreams.pop();
        }
      }
    }

    // Sniff embedded Vimeo videos via iframe requests
    if (url.includes('player.vimeo.com/video/')) {
      const match = details.url.match(/\/video\/([0-9]+)/);
      if (match) {
        const videoId = match[1];
        const watchUrl = `https://vimeo.com/${videoId}`;
        
        const stream = {
          url: watchUrl,
          title: `Embedded Vimeo Video (${videoId})`,
          type: 'Vimeo',
          thumbnail: '',
          platform: 'Vimeo',
          referer: details.initiator || '',
          tabId: details.tabId,
          size: '',
          timestamp: Date.now()
        };

        if (!recentStreams.some(existing => existing.url === watchUrl)) {
          console.log(`[Sniffer] Detected Embedded Vimeo: ${watchUrl}`);
          recentStreams.unshift(stream);
          if (recentStreams.length > 100) recentStreams.pop();
        }
      }
    }

    const initiator = details.initiator ? details.initiator.toLowerCase() : '';
    // Ignore all background network sniffing when on YouTube or Instagram.
    // They are officially supported by yt-dlp, so showing CDN chunks or media fragments is useless and clutters the UI.
    if (url.includes('youtube.com') || url.includes('googlevideo.com') || 
        url.includes('instagram.com') || url.includes('cdninstagram.com') ||
        initiator.includes('youtube.com') || initiator.includes('instagram.com')) {
      return;
    }
    
    // Check Content-Type header
    const ctHeader = details.responseHeaders?.find(h => h.name.toLowerCase() === 'content-type');
    const clHeader = details.responseHeaders?.find(h => h.name.toLowerCase() === 'content-length');
    const size = clHeader ? parseInt(clHeader.value) : 0;

    if (ctHeader) {
      const ct = ctHeader.value.toLowerCase();
      
      // HLS detection
      if (ct.includes('application/vnd.apple.mpegurl') || ct.includes('application/x-mpegurl')) {
        type = 'HLS';
      }
      // DASH detection
      else if (ct.includes('application/dash+xml')) {
        type = 'DASH';
      }
      // MSS detection
      else if (ct.includes('application/vnd.ms-sstr+xml')) {
        type = 'MSS';
      }
      // Direct Video / Audio streams
      else if (ct.includes('video/') || ct.includes('audio/mpeg') || ct.includes('audio/ogg') || ct.includes('audio/mp4') || ct.includes('audio/aac')) {
        // Exclude small chunks (e.g. less than 100KB) to avoid false positives (images or short sounds)
        if (!clHeader || size > 100 * 1024) {
          type = 'Media';
        }
      }
    }

    // 2. Fallback to URL extension sniffing if Content-Type is missing
    if (!type) {
      if (url.includes('.m3u8')) type = 'HLS';
      else if (url.includes('.mpd')) type = 'DASH';
      else if (url.includes('.ism/manifest')) type = 'MSS';
      else {
        // Direct media extensions
        const mediaExtensions = ['.mp4', '.mkv', '.webm', '.ts', '.flv', '.avi', '.mov', '.mp3', '.m4a', '.wav', '.ogg'];
        if (mediaExtensions.some(ext => url.split('?')[0].endsWith(ext))) {
          type = 'Media';
        }
      }
    }

    if (type) {
      addSniffedStream(details, type, size);
    }
  },
  { urls: ["<all_urls>"] },
  ["responseHeaders"]
);

// 3. LISTEN TO BROWSER DOWNLOADS & HACK THEM (NeatDownloadManager style)
chrome.downloads.onCreated.addListener(async (downloadItem) => {
  // Read current user preferences
  const prefs = await chrome.storage.local.get(['autoIntercept', 'excludedSites', 'ctrlPressed', 'ctrlPressedTime']);
  
  if (!prefs.autoIntercept) return;

  // Check if Ctrl key was held down (pressed within the last 1.5 seconds)
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

  // Check if file extension matches our media/download list
  const urlLower = downloadItem.url.toLowerCase().split('?')[0];
  const fileExts = [
    '.mp4', '.mkv', '.webm', '.ts', '.flv', '.avi', '.mov', '.wmv', '.m4v', '.3gp', // Video
    '.mp3', '.m4a', '.wav', '.ogg', '.aac', '.flac', '.opus',                      // Audio
    '.srt', '.vtt', '.ass', '.ssa', '.sub'                                         // Subtitles
  ];

  const matchesExt = fileExts.some(ext => urlLower.endsWith(ext) || downloadItem.filename.toLowerCase().endsWith(ext));
  if (!matchesExt) return;

  // Check if the Desktop companion app is running
  const appRunning = await checkAppStatus();
  if (!appRunning) {
    console.log('[Interceptor] Desktop app is offline. Allowing browser to download.');
    return;
  }

  console.log(`[Interceptor] Intercepting download: ${downloadItem.url}`);

  // Cancel Chrome's default download
  chrome.downloads.cancel(downloadItem.id, () => {
    // Erase from download history to keep browser clean
    chrome.downloads.erase({ id: downloadItem.id });
  });

  // Forward details to desktop application to download instantly
  let title = downloadItem.filename || 'Downloaded File';
  sendToApp(downloadItem.url, 'Best', 'download', 'Direct File', downloadItem.referrer, title, '');
});

// 4. PORT & GENERAL COMMUNICATIONS INTERFACE
chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg.action === 'checkStatus') {
    checkAppStatus().then(running => sendResponse({ running }));
    return true;
  }
  if (msg.action === 'sendToApp') {
    sendToApp(msg.url, msg.quality, msg.downloadAction, msg.type, msg.referer, msg.title, msg.thumbnail).then(sendResponse);
    return true;
  }
  if (msg.action === 'getSniffedStreams') {
    sendResponse({ streams: recentStreams });
    return true;
  }
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
      while (recentStreams.length > 100) {
        recentStreams.pop();
      }
    }
    sendResponse({ success: true });
    return true;
  }
});
