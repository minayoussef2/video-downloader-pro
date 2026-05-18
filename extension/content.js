// Content script - Video Downloader Pro v3.1.0
(function() {
  'use strict';

  const DETECTED_VIDEOS = new Map();

  // Track the Control (Ctrl) key press state and store it in chrome.storage.local
  function updateCtrlState(isPressed) {
    if (!chrome.runtime?.id) return;
    try {
      chrome.storage.local.set({
        ctrlPressed: isPressed,
        ctrlPressedTime: isPressed ? Date.now() : 0
      });
    } catch (e) {
      // Swallowed context invalidation error
    }
  }

  window.addEventListener('keydown', (e) => {
    if (e.key === 'Control') {
      updateCtrlState(true);
    }
  });

  window.addEventListener('keyup', (e) => {
    if (e.key === 'Control') {
      updateCtrlState(false);
    }
  });

  // Reset Ctrl state on window blur (to avoid stuck key states)
  window.addEventListener('blur', () => {
    updateCtrlState(false);
  });

  // Track clicking specifically with Ctrl held
  window.addEventListener('click', (e) => {
    if (e.ctrlKey) {
      updateCtrlState(true);
      // Automatically expire it shortly
      setTimeout(() => updateCtrlState(false), 1500);
    }
  }, { capture: true });

  function detectVideos() {
    const videos = [];
    const pageUrl = window.location.href;

    // 1. Core Page Scraper - yt-dlp native extraction is the ultimate fallback!
    // Since yt-dlp has custom extractors for 1000+ sites (YouTube, Instagram, Facebook),
    // presenting the current page URL is by far the most reliable detection method.
    // Wrap with top-level window check to avoid background iframes (like auth/ad frames) from cluttering the list
    if (window === window.top && !DETECTED_VIDEOS.has(pageUrl)) {
      DETECTED_VIDEOS.set(pageUrl, true);
      
      let cleanTitle = document.title;
      if (pageUrl.includes('youtube.com')) cleanTitle = cleanTitle.replace(/ - YouTube$/, '');
      if (pageUrl.includes('facebook.com')) cleanTitle = cleanTitle.replace(/ \| Facebook$/, '');
      if (pageUrl.includes('instagram.com')) cleanTitle = cleanTitle.replace(/ • Instagram photos and videos$/, '');

      const platform = getPlatformName(pageUrl);
      videos.push({
        url: pageUrl,
        title: cleanTitle || `Current Page (${platform} auto-detect)`,
        thumbnail: getPageThumbnail(),
        type: platform,
        platform: platform
      });
    }

    // 2. DOM <video> element scanning
    document.querySelectorAll('video').forEach((vid, i) => {
      const src = vid.src || vid.querySelector('source')?.src;
      // Exclude blob URLs since yt-dlp cannot resolve local memory references
      if (src && !src.startsWith('blob:') && !DETECTED_VIDEOS.has(src)) {
        DETECTED_VIDEOS.set(src, true);
        
        let title = document.title;
        if (title.length > 40) title = title.substring(0, 37) + '...';
        const fileName = src.split('/').pop().split('?')[0] || 'video';
        
        videos.push({
          url: src,
          title: `Direct Media Element (${title} - ${fileName})`,
          thumbnail: vid.poster || '',
          type: 'Media',
          duration: vid.duration ? formatDuration(vid.duration) : ''
        });
      }
    });

    // 3. Scan Embedded Iframes (YouTube, Vimeo, etc.)
    document.querySelectorAll('iframe').forEach(iframe => {
      try {
        const src = iframe.src;
        if (src && (src.includes('youtube.com/embed/') || src.includes('player.vimeo.com/video/'))) {
          if (!DETECTED_VIDEOS.has(src)) {
            DETECTED_VIDEOS.set(src, true);
            videos.push({
              url: src,
              title: `Embedded Frame Video (${getPlatformName(src)})`,
              thumbnail: '',
              type: 'HLS',
              platform: getPlatformName(src)
            });
          }
        }
      } catch (e) {}
    });

    return videos;
  }

  function getPlatformName(url) {
    if (url.includes('youtube.com') || url.includes('youtu.be')) return 'YouTube';
    if (url.includes('facebook.com') || url.includes('fb.watch')) return 'Facebook';
    if (url.includes('instagram.com')) return 'Instagram';
    if (url.includes('twitter.com') || url.includes('x.com')) return 'Twitter';
    if (url.includes('tiktok.com')) return 'TikTok';
    if (url.includes('vimeo.com')) return 'Vimeo';
    if (url.includes('twitch.tv')) return 'Twitch';
    if (url.includes('reddit.com')) return 'Reddit';
    return 'Website';
  }

  function getPageThumbnail() {
    const ogImage = document.querySelector('meta[property="og:image"]');
    if (ogImage) return ogImage.content;

    const twImage = document.querySelector('meta[name="twitter:image"]');
    if (twImage) return twImage.content;

    return '';
  }

  function formatDuration(seconds) {
    if (!seconds || isNaN(seconds)) return '';
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = Math.floor(seconds % 60);
    return h > 0 ? `${h}:${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`
                  : `${m}:${String(s).padStart(2,'0')}`;
  }

  function reportToBackground(videos) {
    if (videos.length > 0 && chrome.runtime?.id) {
      try {
        chrome.runtime.sendMessage({ 
          action: 'appendStreams', 
          streams: videos.map(v => ({
            ...v,
            referer: window.location.href,
            tabId: null 
          }))
        });
      } catch (e) {
        // Swallowed context invalidation error
      }
    }
  }

  // Listen for messages from popup
  chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (msg.action === 'detectVideos') {
      const videos = detectVideos();
      reportToBackground(videos);
      sendResponse({ videos });
    }
    return true;
  });

  // Monitor dynamic content DOM changes (lazy-loaded reels/TikToks/YouTube pages)
  const observer = new MutationObserver(() => {
    const newVideos = detectVideos();
    if (newVideos.length > 0) {
      reportToBackground(newVideos);
    }
  });

  observer.observe(document.body, { childList: true, subtree: true });

  // Scan immediately upon page load idle
  setTimeout(() => {
    const videos = detectVideos();
    reportToBackground(videos);
  }, 1000);
})();
