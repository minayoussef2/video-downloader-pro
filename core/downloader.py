import yt_dlp
import os
import threading
from datetime import datetime


class YTDownloader:
    def __init__(self, progress_callback=None):
        self.progress_callback = progress_callback
        self.is_cancelled = False
        self._pause_event = threading.Event()
        self._pause_event.set()   # set = running, clear = paused
        self.is_paused = False

    # ── Info ──────────────────────────────────────────────────────────────────

    def get_info(self, url):
        ydl_opts = {
            'quiet': True,
            'no_warnings': True,
            'extract_flat': 'in_playlist',
        }
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            try:
                info = ydl.extract_info(url, download=False)
                return info
            except Exception as e:
                return {"error": str(e)}

    # ── Download ──────────────────────────────────────────────────────────────

    def download(self, url, options, concurrent_fragments=4):
        self.is_cancelled = False
        self.is_paused = False
        self._pause_event.set()

        def progress_hook(d):
            # Block here while paused (will unblock on resume)
            self._pause_event.wait()

            if self.is_cancelled:
                raise Exception("Download cancelled by user")

            if d['status'] == 'downloading':
                p_str = d.get('_percent_str', '0%').replace('%', '').strip()
                try:
                    percent = float(p_str)
                except:
                    percent = 0.0

                speed = d.get('_speed_str', '0 MB/s')
                eta   = d.get('_eta_str',   '00:00')
                total_bytes  = d.get('total_bytes') or d.get('total_bytes_estimate', 0)
                size_mb = total_bytes / (1024 * 1024) if total_bytes else 0

                if self.progress_callback:
                    self.progress_callback({
                        'percent':  percent,
                        'speed':    speed,
                        'eta':      eta,
                        'status':   'downloading',
                        'filename': os.path.basename(d.get('filename', '')),
                        'size_mb':  size_mb,
                    })

            elif d['status'] == 'finished':
                if self.progress_callback:
                    self.progress_callback({'status': 'finished', 'percent': 100})

        ydl_opts = {
            'format':               options.get('format_id', 'best'),
            'outtmpl':              options.get('outtmpl', '%(title)s.%(ext)s'),
            'progress_hooks':       [progress_hook],
            'noplaylist':           options.get('noplaylist', True),
            'merge_output_format':  options.get('ext', 'mp4'),
            'concurrent_fragment_downloads': max(1, int(concurrent_fragments)),
        }

        if options.get('subtitles'):
            ydl_opts['writesubtitles']    = True
            ydl_opts['allsubtitles']      = True
            ydl_opts['writeautomaticsub'] = True

        if options.get('postprocessors'):
            ydl_opts['postprocessors'] = options['postprocessors']

        try:
            with yt_dlp.YoutubeDL(ydl_opts) as ydl:
                ydl.download([url])
                if self.progress_callback:
                    self.progress_callback({'status': 'finished', 'percent': 100})
                return True
        except Exception as e:
            err = str(e)
            if 'cancelled' in err.lower():
                return 'cancelled'
            if self.is_paused:
                return 'paused'
            return err

    # ── Controls ──────────────────────────────────────────────────────────────

    def pause(self):
        self.is_paused = True
        self._pause_event.clear()   # block progress hook → pauses between fragments

    def resume(self):
        self.is_paused = False
        self._pause_event.set()     # unblock progress hook → resumes

    def cancel(self):
        self.is_cancelled = True
        self._pause_event.set()     # unblock so the cancellation exception can be raised
