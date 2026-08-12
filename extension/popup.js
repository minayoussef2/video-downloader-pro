document.addEventListener('DOMContentLoaded', async () => {
  const statusEl = document.getElementById('appStatus');
  const listEl = document.getElementById('videoList');
  const offlineError = document.getElementById('app-offline-error');
  
  // Navigation elements
  const tabStreamsBtn = document.getElementById('tab-streams');
  const tabSettingsBtn = document.getElementById('tab-settings');
  const paneStreams = document.getElementById('pane-streams');
  const paneSettings = document.getElementById('pane-settings');
  
  // Preference elements
  const toggleIntercept = document.getElementById('toggle-intercept');
  const toggleSiteActive = document.getElementById('toggle-site-active');
  const siteDomainLabel = document.getElementById('site-domain-label');
  const exclusionContainer = document.getElementById('exclusion-container');
  const selectPreferredQuality = document.getElementById('select-preferred-quality');
  
  // Batch action elements
  const batchRow = document.getElementById('batch-row');
  const btnBatchQueue = document.getElementById('btn-batch-queue');
  const btnBatchDl = document.getElementById('btn-batch-dl');

  let isAppRunning = false;
  let currentDomain = '';
  let activeTabId = null;
  let activeTabUrl = '';
  let discoveredVideos = [];
  let defaultQuality = 'Best'; // Will be synced from desktop app or local preference

  // All quality options matching the desktop app
  const QUALITY_OPTIONS = [
    { value: 'Best',         label: 'Best Quality' },
    { value: '8K (4320p)',   label: '8K (4320p)'  },
    { value: '4K (2160p)',   label: '4K (2160p)'  },
    { value: '2K (1440p)',   label: '2K (1440p)'  },
    { value: '1080p',        label: '1080p Full HD' },
    { value: '720p',         label: '720p HD'     },
    { value: '480p',         label: '480p SD'     },
    { value: '360p',         label: '360p'        },
    { value: '240p',         label: '240p'        },
    { value: '144p',         label: '144p'        },
    { value: 'Audio Only',   label: 'Audio Only (MP3)' },
  ];

  // Build quality <select> option HTML using QUALITY_OPTIONS
  function buildQualityOptions(selectedValue) {
    return QUALITY_OPTIONS.map(opt =>
      `<option value="${opt.value}"${opt.value === selectedValue ? ' selected' : ''}>${opt.label}</option>`
    ).join('');
  }

  // 1. Direct App Connection Status Monitoring
  async function checkAppDirectly() {
    const result = await chrome.storage.local.get('appPort');
    let port = result.appPort || 18888;
    let running = false;

    // Test currently cached port
    try {
      const res = await fetch(`http://127.0.0.1:${port}/status`);
      const data = await res.json();
      running = data && data.status === 'running';
    } catch (err) {}

    // Probe fallback ports if needed
    if (!running) {
      for (let p = 18888; p <= 18892; p++) {
        try {
          const res = await fetch(`http://127.0.0.1:${p}/status`);
          const data = await res.json();
          if (data && data.status === 'running') {
            port = p;
            running = true;
            await chrome.storage.local.set({ appPort: p });
            break;
          }
        } catch (err) {}
      }
    }

    isAppRunning = running;

    if (isAppRunning) {
      statusEl.textContent = `App Connected (:${port})`;
      statusEl.className = 'status-badge online';
      offlineError.style.display = 'none';
      document.querySelectorAll('.btn-action.primary, .btn-action.secondary, .btn-batch').forEach(btn => {
        if (!btn.hasAttribute('data-disabled')) {
          btn.removeAttribute('disabled');
          btn.removeAttribute('title');
        }
      });
    } else {
      statusEl.textContent = 'App Offline';
      statusEl.className = 'status-badge';
      offlineError.style.display = 'block';
      document.querySelectorAll('.btn-action.primary, .btn-action.secondary, .btn-batch').forEach(btn => {
        btn.setAttribute('disabled', 'true');
        btn.setAttribute('title', 'Start the desktop app to download');
      });
    }
  }

  checkAppDirectly();
  setInterval(checkAppDirectly, 5000);

  // Sync default quality preference from local storage or desktop app settings
  async function initQualityPreferences() {
    // 1. Check local preferred quality first
    const prefData = await chrome.storage.local.get('preferredQuality');
    if (prefData && prefData.preferredQuality) {
      defaultQuality = prefData.preferredQuality;
    } else {
      // 2. Fallback to desktop app setting
      const result = await chrome.storage.local.get('appPort');
      const port = result.appPort || 18888;
      try {
        const res = await fetch(`http://127.0.0.1:${port}/settings`);
        const data = await res.json();
        if (data && data.defaultQuality) {
          defaultQuality = data.defaultQuality;
        }
      } catch {}
    }

    if (selectPreferredQuality) {
      selectPreferredQuality.innerHTML = buildQualityOptions(defaultQuality);
      selectPreferredQuality.value = defaultQuality;
      selectPreferredQuality.addEventListener('change', () => {
        defaultQuality = selectPreferredQuality.value;
        chrome.storage.local.set({ preferredQuality: defaultQuality });
      });
    }
  }
  initQualityPreferences();

  // 2. Navigation Tab Handlers
  tabStreamsBtn.addEventListener('click', () => {
    tabStreamsBtn.classList.add('active');
    tabSettingsBtn.classList.remove('active');
    paneStreams.classList.add('active');
    paneSettings.classList.remove('active');
  });

  tabSettingsBtn.addEventListener('click', () => {
    tabSettingsBtn.classList.add('active');
    tabStreamsBtn.classList.remove('active');
    paneSettings.classList.add('active');
    paneStreams.classList.remove('active');
    renderExclusionList();
  });

  // 3. Query Active Browser Tab Details
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (tab) {
    activeTabId = tab.id;
    activeTabUrl = tab.url;
    try {
      currentDomain = new URL(tab.url).hostname.toLowerCase();
      siteDomainLabel.textContent = `Disable capture for ${currentDomain}`;
    } catch {}
  }

  if (!tab || activeTabUrl.startsWith('chrome://') || activeTabUrl.startsWith('edge://')) {
    listEl.innerHTML = '<div class="empty-state"><div class="empty-state-icon">🔒</div>Scanning system pages is blocked by the browser.</div>';
    return;
  }

  // 4. Preferences & Exclusion Site List Management
  const storage = await chrome.storage.local.get(['autoIntercept', 'excludedSites']);
  
  // Set initial checkbox states
  toggleIntercept.checked = storage.autoIntercept !== false;
  
  const excludedSites = storage.excludedSites || [];
  const isCurrentSiteExcluded = excludedSites.includes(currentDomain);
  toggleSiteActive.checked = !isCurrentSiteExcluded;

  toggleIntercept.addEventListener('change', () => {
    chrome.storage.local.set({ autoIntercept: toggleIntercept.checked });
  });

  toggleSiteActive.addEventListener('change', async () => {
    const data = await chrome.storage.local.get('excludedSites');
    let list = data.excludedSites || [];
    
    if (toggleSiteActive.checked) {
      // Remove site from exclusions (enable capture)
      list = list.filter(site => site !== currentDomain);
    } else {
      // Add site to exclusions (disable capture)
      if (!list.includes(currentDomain) && currentDomain) {
        list.push(currentDomain);
      }
    }
    await chrome.storage.local.set({ excludedSites: list });
    renderExclusionList();
  });

  async function renderExclusionList() {
    const data = await chrome.storage.local.get('excludedSites');
    const list = data.excludedSites || [];
    exclusionContainer.innerHTML = '';

    if (list.length === 0) {
      exclusionContainer.innerHTML = '<div style="padding: 16px; text-align: center; color: var(--text-muted); font-size: 10px;">No excluded websites.</div>';
      return;
    }

    list.forEach(site => {
      const item = document.createElement('div');
      item.className = 'excluded-item';
      item.innerHTML = `
        <span>${site}</span>
        <button class="btn-remove-site" data-site="${site}">✕</button>
      `;
      exclusionContainer.appendChild(item);

      item.querySelector('.btn-remove-site').addEventListener('click', async (e) => {
        const siteToRemove = e.target.getAttribute('data-site');
        let updatedList = list.filter(s => s !== siteToRemove);
        await chrome.storage.local.set({ excludedSites: updatedList });
        
        if (siteToRemove === currentDomain) {
          toggleSiteActive.checked = true;
        }
        renderExclusionList();
      });
    });
  }

  // 5. Gather Media Streams and Scan Page DOM
  let currentOrigin = '';
  try { currentOrigin = new URL(activeTabUrl).origin; } catch {}

  chrome.runtime.sendMessage({ action: 'getSniffedStreams' }, (bgRes) => {
    chrome.tabs.sendMessage(activeTabId, { action: 'detectVideos' }, (res) => {
      let allVideos = [];

      // Add network sniffed streams
      if (bgRes && bgRes.streams) {
        const filtered = bgRes.streams.filter(s => {
          if (!currentOrigin) return true;
          if (s.tabId === activeTabId) return true;
          return s.referer.includes(currentOrigin) || currentOrigin.includes(s.referer);
        });
        allVideos = allVideos.concat(filtered);
      }

      // Add DOM scanned streams
      if (!chrome.runtime.lastError && res && res.videos) {
        res.videos.forEach(v => {
          if (!v.type) {
            const url = v.url.toLowerCase();
            if (url.includes('.m3u8')) v.type = 'HLS';
            else if (url.includes('.mpd')) v.type = 'DASH';
            else v.type = 'Media';
          }
          if (!v.referer) v.referer = activeTabUrl;
        });
        allVideos = allVideos.concat(res.videos);
      }

      // Robust Deduplication
      const uniqueVideos = [];
      const seenUrls = new Set();
      for (const v of allVideos) {
        const normUrl = v.url.split('#')[0];
        if (!seenUrls.has(normUrl)) {
          seenUrls.add(normUrl);
          uniqueVideos.push(v);
        }
      }

      discoveredVideos = uniqueVideos;

      if (discoveredVideos.length === 0) {
        listEl.innerHTML = `
          <div class="empty-state">
            <div class="empty-state-icon">🔍</div>
            No video streams detected on this page.<br><br>
            <span style="font-size: 10px; color: var(--text-muted);">Try playing the video to trigger network detection.</span>
          </div>`;
        batchRow.style.display = 'none';
        return;
      }

      batchRow.style.display = 'flex';
      renderVideos(discoveredVideos);
    });
  });

  // 6. Render Dynamic Video Cards
  function renderVideos(videos) {
    listEl.innerHTML = '';
    
    videos.forEach((vid, idx) => {
      const item = document.createElement('div');
      item.className = 'video-item';
      
      const badgeClass = vid.type.toLowerCase();
      const typeBadge = `<span class="badge ${badgeClass}">${vid.type}</span>`;
      
      // Determine file details icon
      let typeIcon = '🎬';
      if (vid.type === 'HLS' || vid.type === 'DASH' || vid.type === 'MSS') typeIcon = '📡';
      else if (vid.type === 'Page' || vid.type === 'YouTube' || vid.type === 'Instagram' || vid.type === 'Facebook') typeIcon = '📄';
      
      // Show file size indicator next to domain if present
      const sizeIndicator = vid.size ? ` • 📦 ${vid.size}` : '';

      // Determine file details icon or thumbnail image
      let thumbnailHtml = `<div class="video-thumb">${typeIcon}</div>`;
      if (vid.thumbnail) {
        thumbnailHtml = `<img class="video-thumb" src="${vid.thumbnail}" style="object-fit: cover;" onerror="this.outerHTML='<div class=&quot;video-thumb&quot;>${typeIcon}</div>'">`;
      }

      item.innerHTML = `
        <div class="video-header-row" id="header-${idx}">
          ${thumbnailHtml}
          <div class="video-meta-block">
            <div class="video-title" title="${vid.title}">${vid.title}</div>
            <div class="video-sub-meta">
              ${typeBadge} • ${new URL(vid.url).hostname.replace('www.', '')}${sizeIndicator}
            </div>
          </div>
        </div>
        
        <div class="video-details" id="details-${idx}">
          <div class="details-row"><strong>Resource URL:</strong> ${vid.url}</div>
          <div class="details-row"><strong>Origin Referer:</strong> ${vid.referer || 'Current Tab'}</div>
          
          <div class="action-controls">
            <select id="quality-${idx}">
              ${buildQualityOptions(defaultQuality)}
            </select>
          </div>
          
          <div class="action-controls" style="margin-top: 8px;">
              <button class="btn-action secondary" id="btn-q-${idx}">+ Queue</button>
              <button class="btn-action primary" id="btn-dl-${idx}">Download</button>
              <button class="btn-action copy" id="btn-copy-${idx}" title="Copy URL">📋</button>
          </div>
          <div id="msg-${idx}" class="msg-feedback"></div>
        </div>
      `;
      
      listEl.appendChild(item);

      // Accordion Collapse/Expand toggles
      document.getElementById(`header-${idx}`).addEventListener('click', () => {
        const detailsPanel = document.getElementById(`details-${idx}`);
        const isActive = detailsPanel.classList.contains('active');
        
        // Collapse all others
        document.querySelectorAll('.video-details').forEach(el => el.classList.remove('active'));
        
        if (!isActive) {
          detailsPanel.classList.add('active');
        }
      });

      document.getElementById(`btn-q-${idx}`).addEventListener('click', () => sendAction(vid.url, idx, 'queue', vid.type, vid.referer, vid.title, vid.thumbnail));
      document.getElementById(`btn-dl-${idx}`).addEventListener('click', () => sendAction(vid.url, idx, 'download', vid.type, vid.referer, vid.title, vid.thumbnail));
      document.getElementById(`btn-copy-${idx}`).addEventListener('click', () => {
        navigator.clipboard.writeText(vid.url);
        const msg = document.getElementById(`msg-${idx}`);
        msg.textContent = 'URL Copied to clipboard!';
        msg.className = 'msg-feedback success';
        setTimeout(() => msg.textContent = '', 2000);
      });
    });
  }

  // 7. Single Download Request Dispatcher
  function sendAction(url, idx, action, type, referer, title, thumbnail) {
    const msgEl = document.getElementById(`msg-${idx}`);
    const quality = document.getElementById(`quality-${idx}`).value;

    if (!isAppRunning) {
      msgEl.textContent = 'App offline';
      msgEl.className = 'msg-feedback error';
      return;
    }

    msgEl.textContent = 'Sending to app...';
    msgEl.className = 'msg-feedback';

    chrome.runtime.sendMessage({ action: 'sendToApp', url, quality, downloadAction: action, type, referer, title, thumbnail }, (res) => {
      if (res && res.success) {
        msgEl.textContent = 'Success! Added to Queue.';
        msgEl.className = 'msg-feedback success';
        setTimeout(() => window.close(), 1000);
      } else {
        msgEl.textContent = res ? res.error : 'Connection error';
        msgEl.className = 'msg-feedback error';
      }
    });
  }

  // 8. Batch Download Operations (Queue All & Download All)
  btnBatchQueue.addEventListener('click', () => runBatch('queue'));
  btnBatchDl.addEventListener('click', () => runBatch('download'));

  function runBatch(action) {
    if (!isAppRunning) return;
    
    // All stream types are now downloadable (including DASH/MPD)
    const downloadable = [...discoveredVideos];
    if (downloadable.length === 0) return;

    let successCount = 0;
    let failedCount = 0;

    downloadable.forEach((vid, i) => {
      chrome.runtime.sendMessage({ 
        action: 'sendToApp', 
        url: vid.url, 
        quality: 'Best', 
        downloadAction: action, 
        type: vid.type, 
        referer: vid.referer || activeTabUrl, 
        title: vid.title,
        thumbnail: vid.thumbnail
      }, (res) => {
        if (res && res.success) {
          successCount++;
        } else {
          failedCount++;
        }

        // Once all processed, show status
        if (successCount + failedCount === downloadable.length) {
          alert(`Batch Action Complete!\n\nSuccessful: ${successCount}\nFailed: ${failedCount}`);
          window.close();
        }
      });
    });
  }
});
