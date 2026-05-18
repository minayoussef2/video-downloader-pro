document.addEventListener('DOMContentLoaded', async () => {
  const statusEl = document.getElementById('appStatus');
  const listEl = document.getElementById('videoList');
  const offlineError = document.getElementById('app-offline-error');
  let isAppRunning = false;

  // Check app status directly from popup for better reliability
  async function checkAppDirectly() {
    try {
      // Try localhost first (Chrome prefers it over 127.0.0.1)
      const res = await fetch('http://localhost:18888/status');
      const data = await res.json();
      isAppRunning = data && data.status === 'running';
    } catch (err) {
      try {
        const res = await fetch('http://127.0.0.1:18888/status');
        const data = await res.json();
        isAppRunning = data && data.status === 'running';
      } catch (err2) {
        console.warn('[Popup] Connection failed to both localhost and 127.0.0.1');
        isAppRunning = false;
      }
    }

    if (isAppRunning) {
      if (statusEl) {
        statusEl.textContent = 'App Connected';
        statusEl.classList.add('online');
      }
      if (offlineError) offlineError.style.display = 'none';
      Array.from(document.getElementsByClassName('download-btn')).forEach(btn => btn.disabled = false);
    } else {
      if (statusEl) {
        statusEl.textContent = 'App Offline (Re-check Port 18888)';
        statusEl.classList.remove('online');
      }
      if (offlineError) offlineError.style.display = 'block';
      Array.from(document.getElementsByClassName('download-btn')).forEach(btn => btn.disabled = true);
    }
  }

  checkAppDirectly();
  setInterval(checkAppDirectly, 5000); // Auto-refresh status every 5 seconds

  // Query active tab
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  
  if (!tab || tab.url.startsWith('chrome://') || tab.url.startsWith('edge://')) {
    listEl.innerHTML = '<div class="empty-state">Cannot scan system pages</div>';
    return;
  }

  // Current tab origin for filtering
  let currentOrigin = '';
  try { currentOrigin = new URL(tab.url).origin; } catch {}

  // Ask background script for network-sniffed streams
  chrome.runtime.sendMessage({ action: 'getSniffedStreams' }, (bgRes) => {
    // Ask content script for page videos
    chrome.tabs.sendMessage(tab.id, { action: 'detectVideos' }, (res) => {
      let allVideos = [];
      
      // 1. Add sniffed streams
      if (bgRes && bgRes.streams) {
        const filtered = bgRes.streams.filter(s => {
          if (!currentOrigin) return true;
          // Filter by current tabId if available, or origin matching
          if (s.tabId === tab.id) return true;
          return s.referer.includes(currentOrigin) || currentOrigin.includes(s.referer);
        });
        allVideos = allVideos.concat(filtered);
      }
      
      // 2. Add page detected streams (DOM scan)
      if (!chrome.runtime.lastError && res && res.videos) {
        res.videos.forEach(v => {
          if (!v.type) {
            const url = v.url.toLowerCase();
            if (url.includes('.m3u8')) v.type = 'HLS';
            else if (url.includes('.mpd')) v.type = 'DASH';
            else v.type = 'Media';
          }
          if (!v.referer) v.referer = tab.url;
        });
        allVideos = allVideos.concat(res.videos);
      }

      // 3. Robust Deduplication
      const uniqueVideos = [];
      const seenUrls = new Set();
      for (const v of allVideos) {
        // Normalize URL for comparison (remove fragments)
        const normUrl = v.url.split('#')[0];
        if (!seenUrls.has(normUrl)) {
          seenUrls.add(normUrl);
          uniqueVideos.push(v);
        }
      }

      if (uniqueVideos.length === 0) {
        listEl.innerHTML = `
          <div class="empty-state">
            No videos detected on this page.<br><br>
            <span style="font-size: 11px;">Try playing the video to trigger detection.</span>
          </div>`;
        return;
      }

      renderVideos(uniqueVideos);
    });
  });

  function renderVideos(videos) {
    listEl.innerHTML = '';
    
    videos.forEach((vid, idx) => {
      const item = document.createElement('div');
      item.className = 'video-item';
      
      const isDash = vid.type === 'DASH';
      const typeBadge = `<span class="type-badge ${vid.type.toLowerCase()}">${vid.type}</span>`;
      
      item.innerHTML = `
        <div class="video-thumb">
          <div class="type-icon">${vid.type === 'Media' ? '🎬' : '📡'}</div>
        </div>
        <div class="video-info">
          <div class="video-title" title="${vid.title}">${vid.title}</div>
          <div class="video-meta">${typeBadge} • ${new URL(vid.url).hostname}</div>
          
          <div class="controls">
            <select id="quality-${idx}" ${isDash ? 'disabled' : ''}>
              <option value="Best">Best Quality</option>
              <option value="1080p">1080p</option>
              <option value="720p">720p</option>
              <option value="Audio Only">MP3 Audio</option>
            </select>
          </div>
          
          <div class="actions">
            ${isDash ? 
              `<button class="btn-queue" disabled style="width: 100%; opacity: 0.5;">Unsupported (DRM)</button>` 
            : 
              `<button class="btn-queue" id="btn-q-${idx}">+ Queue</button>
               <button class="btn-download" id="btn-dl-${idx}">Download</button>`
            }
          </div>
          <div id="msg-${idx}" class="msg"></div>
        </div>
      `;
      
      listEl.appendChild(item);

      if (!isDash) {
        document.getElementById(`btn-q-${idx}`).addEventListener('click', () => sendAction(vid.url, idx, 'queue', vid.type, vid.referer, vid.title));
        document.getElementById(`btn-dl-${idx}`).addEventListener('click', () => sendAction(vid.url, idx, 'download', vid.type, vid.referer, vid.title));
      }
    });
  }

  function sendAction(url, idx, action, type, referer, title) {
    const msgEl = document.getElementById(`msg-${idx}`);
    const quality = document.getElementById(`quality-${idx}`).value;

    if (!isAppRunning) {
      msgEl.textContent = 'App offline';
      msgEl.className = 'msg error';
      return;
    }

    msgEl.textContent = 'Sending...';
    msgEl.className = 'msg';

    chrome.runtime.sendMessage({ action: 'sendToApp', url, quality, downloadAction: action, type, referer, title }, (res) => {
      if (res && res.success) {
        msgEl.textContent = 'Success!';
        msgEl.className = 'msg success';
        setTimeout(() => window.close(), 1000);
      } else {
        msgEl.textContent = res ? res.error : 'Connection error';
        msgEl.className = 'msg error';
      }
    });
  }
});
