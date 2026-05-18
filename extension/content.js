// Content script — detects video elements and media URLs on any page
(function() {
  'use strict';

  const DETECTED_VIDEOS = new Map();

  function detectVideos() {
    const videos = [];
    const pageUrl = window.location.href;

    // 1. Always add the current page URL. 
    // yt-dlp is extremely smart and supports over 1000 sites.
    // Usually passing the page URL is the most reliable method.
    if (!DETECTED_VIDEOS.has(pageUrl)) {
      DETECTED_VIDEOS.set(pageUrl, true);
      videos.push({
        url: pageUrl,
        title: document.title || 'Current Page (yt-dlp auto-detect)',
        thumbnail: getPageThumbnail(),
        type: 'page_url',
        platform: getPlatformName(pageUrl)
      });
    }

    // 2. Detect <video> elements (only if they are direct MP4/WEBM URLs, NO BLOBs)
    document.querySelectorAll('video').forEach((vid, i) => {
      const src = vid.src || vid.querySelector('source')?.src;
      // Exclude blob URLs because they are local memory pointers that yt-dlp cannot access
      if (src && !src.startsWith('blob:') && !DETECTED_VIDEOS.has(src)) {
        DETECTED_VIDEOS.set(src, true);
        videos.push({
          url: src,
          title: `Direct Media File (${src.split('/').pop().split('?')[0] || 'video'})`,
          thumbnail: vid.poster || '',
          type: 'video_element',
          duration: vid.duration ? formatDuration(vid.duration) : ''
        });
      }
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
    // Try Open Graph image
    const ogImage = document.querySelector('meta[property="og:image"]');
    if (ogImage) return ogImage.content;

    // Try Twitter card image
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

  // Report detected videos to background script
  function reportToBackground(videos) {
    if (videos.length > 0) {
      chrome.runtime.sendMessage({ 
        action: 'appendStreams', 
        streams: videos.map(v => ({
          ...v,
          referer: window.location.href,
          tabId: null // Content script doesn't know its own tabId easily
        }))
      });
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

  // Watch for new video elements being added (TikTok, Instagram scrolling)
  const observer = new MutationObserver(() => {
    const newVideos = detectVideos();
    if (newVideos.length > 0) {
      reportToBackground(newVideos);
    }
  });

  observer.observe(document.body, { childList: true, subtree: true });

  // Initial detection
  setTimeout(() => {
    const videos = detectVideos();
    reportToBackground(videos);
  }, 1000);
})();
