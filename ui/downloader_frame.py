import customtkinter as ctk
import threading
import requests
import os
import re
import time
from PIL import Image
from io import BytesIO
import yt_dlp
from utils.settings import Settings
from core.stats_manager import StatsManager


# ─────────────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────────────

def fmt_size(b):
    if not b or b <= 0: return None
    if b < 1 << 20:  return f"{b/1024:.1f} KB"
    if b < 1 << 30:  return f"{b/(1<<20):.1f} MB"
    return f"{b/(1<<30):.2f} GB"

def fmt_dur(s):
    if not s: return "—"
    s = int(s)
    h, r = divmod(s, 3600)
    m, s = divmod(r, 60)
    return f"{h}:{m:02d}:{s:02d}" if h else f"{m}:{s:02d}"

def sanitize(name):
    return re.sub(r'[<>:"/\\|?*]', '_', name or "video")


# ─────────────────────────────────────────────────────────────────────────────
# Per-item downloader (owns its own yt-dlp + pause/cancel state)
# ─────────────────────────────────────────────────────────────────────────────

class ItemDownloader:
    def __init__(self):
        self.is_cancelled = False
        self.is_paused    = False
        self._pause_event = threading.Event()
        self._pause_event.set()

    def pause(self):
        self.is_paused = True
        self._pause_event.clear()

    def resume(self):
        self.is_paused = False
        self._pause_event.set()

    def cancel(self):
        self.is_cancelled = True
        self._pause_event.set()

    def download(self, url, ydl_opts):
        """Run download; returns True | 'cancelled' | error-string."""
        self.is_cancelled = False
        self.is_paused    = False
        self._pause_event.set()

        original_hooks = [h for h in ydl_opts.get("progress_hooks", []) if h]

        def hook(d):
            self._pause_event.wait()
            if self.is_cancelled:
                raise Exception("cancelled by user")
            for h in original_hooks:
                h(d)
        
        merged = {**ydl_opts, "progress_hooks": [hook]}
        try:
            with yt_dlp.YoutubeDL(merged) as ydl:
                ydl.download([url])
            return True
        except Exception as e:
            err = str(e)
            if "cancelled by user" in err.lower():
                return "cancelled"
            for prefix in ("ERROR: ", "yt_dlp.utils.DownloadError: ERROR: "):
                if err.startswith(prefix):
                    err = err[len(prefix):]
                    break
            return err


# ─────────────────────────────────────────────────────────────────────────────
# Single queue-item card
# ─────────────────────────────────────────────────────────────────────────────

QUALITIES = [
    "Best", "8K (4320p)", "4K (2160p)", "2K (1440p)",
    "1080p", "720p", "480p", "360p", "240p", "144p", "Audio Only",
]
HEIGHT_MAP = {
    "8K (4320p)": 4320, "4K (2160p)": 2160, "2K (1440p)": 1440,
    "1080p": 1080, "720p": 720, "480p": 480,
    "360p": 360, "240p": 240, "144p": 144,
}
FORMAT_STRING = {
    **{f"bestvideo[height<={h}]+bestaudio/best[height<={h}]": h
       for h in HEIGHT_MAP.values()},
}


class YTQueueItem(ctk.CTkFrame):
    def __init__(self, master, url, item_id, on_remove, settings,
                 prefetch_info=None):
        super().__init__(master, corner_radius=10, border_width=1,
                         border_color=("gray70", "gray30"))
        self.url          = url
        self.item_id      = item_id
        self.on_remove    = on_remove
        self.settings     = settings
        self.video_info   = None
        self.format_sizes = {}
        self.is_downloading = False
        self._dl = ItemDownloader()
        self._ctk_thumbnail = None     # keep reference

        self.grid_columnconfigure(1, weight=1)
        self._build_ui()

        if prefetch_info:
            # info already fetched by playlist expansion – apply immediately
            self.after(0, lambda: self._apply_info(prefetch_info))
        else:
            threading.Thread(target=self._analyze, daemon=True).start()

    # ── UI layout ──────────────────────────────────────────────────────────

    def _build_ui(self):
        # Thumbnail
        self.thumb = ctk.CTkLabel(self, text="⏳", width=140, height=80,
                                   fg_color=("gray80", "gray25"),
                                   corner_radius=6, font=ctk.CTkFont(size=22))
        self.thumb.grid(row=0, column=0, rowspan=3, padx=(10, 6), pady=10, sticky="nw")

        # Title row
        title_row = ctk.CTkFrame(self, fg_color="transparent")
        title_row.grid(row=0, column=1, padx=4, pady=(10, 2), sticky="ew")
        title_row.grid_columnconfigure(0, weight=1)

        self.title_lbl = ctk.CTkLabel(title_row, text="Analyzing…",
                                       font=ctk.CTkFont(size=13, weight="bold"),
                                       anchor="w", wraplength=480)
        self.title_lbl.grid(row=0, column=0, sticky="ew")

        self.remove_btn = ctk.CTkButton(title_row, text="✕", width=28, height=26,
                                         fg_color="transparent",
                                         text_color=("gray45", "gray55"),
                                         hover_color=("gray80", "gray25"),
                                         command=self._remove)
        self.remove_btn.grid(row=0, column=1, padx=(8, 4))

        # Meta
        self.meta_lbl = ctk.CTkLabel(self, text="",
                                      font=ctk.CTkFont(size=11),
                                      text_color=("gray45", "gray55"), anchor="w")
        self.meta_lbl.grid(row=1, column=1, padx=6, pady=1, sticky="ew")

        # Options row
        opts = ctk.CTkFrame(self, fg_color="transparent")
        opts.grid(row=2, column=1, padx=4, pady=(2, 6), sticky="ew")

        ctk.CTkLabel(opts, text="Quality:", font=ctk.CTkFont(size=11)).pack(side="left", padx=(0,4))
        self.qual_combo = ctk.CTkComboBox(opts, values=QUALITIES, width=155,
                                           command=self._on_quality)
        # apply default quality from settings
        dq = self.settings.get("default_quality") or "Best"
        self.qual_combo.set(dq if dq in QUALITIES else "Best")
        self.qual_combo.pack(side="left", padx=(0, 8))

        self.size_lbl = ctk.CTkLabel(opts, text="",
                                      font=ctk.CTkFont(size=11),
                                      text_color=("gray40", "gray60"))
        self.size_lbl.pack(side="left", padx=(0, 12))

        ctk.CTkLabel(opts, text="Format:", font=ctk.CTkFont(size=11)).pack(side="left", padx=(0,4))
        self.fmt_combo = ctk.CTkComboBox(opts, values=["MP4","MKV","MP3","WAV","M4A"], width=90)
        self.fmt_combo.set("MP4")
        self.fmt_combo.pack(side="left")

        # Progress bar
        self.prog_bar = ctk.CTkProgressBar(self)
        self.prog_bar.grid(row=3, column=0, columnspan=2,
                            padx=10, pady=(4, 2), sticky="ew")
        self.prog_bar.set(0)

        # Status + buttons row
        bottom = ctk.CTkFrame(self, fg_color="transparent")
        bottom.grid(row=4, column=0, columnspan=2,
                    padx=10, pady=(0, 10), sticky="ew")
        bottom.grid_columnconfigure(0, weight=1)

        self.status_lbl = ctk.CTkLabel(bottom, text="Fetching info…",
                                        font=ctk.CTkFont(size=11),
                                        text_color=("gray40", "gray60"), anchor="w")
        self.status_lbl.grid(row=0, column=0, sticky="ew")

        # Action buttons
        btns = ctk.CTkFrame(bottom, fg_color="transparent")
        btns.grid(row=0, column=1, padx=(8, 0))

        self.dl_btn = ctk.CTkButton(btns, text="⬇ Download",
                                     width=108, height=28, state="disabled",
                                     font=ctk.CTkFont(weight="bold"),
                                     command=self.start_download)
        self.dl_btn.pack(side="left", padx=(0, 4))

        self.pause_btn = ctk.CTkButton(btns, text="⏸ Pause",
                                        width=90, height=28, state="disabled",
                                        fg_color=("#d4850a","#a0620a"),
                                        hover_color=("#e0960c","#b8720c"),
                                        command=self._toggle_pause)
        self.pause_btn.pack(side="left", padx=(0, 4))

        self.cancel_btn = ctk.CTkButton(btns, text="✕ Cancel",
                                         width=80, height=28, state="disabled",
                                         fg_color=("#c0392b","#922b21"),
                                         hover_color=("#e74c3c","#c0392b"),
                                         command=self._cancel)
        self.cancel_btn.pack(side="left")

    # ── Analysis ──────────────────────────────────────────────────────────

    def _analyze(self):
        try:
            with yt_dlp.YoutubeDL({"quiet":True,"no_warnings":True}) as ydl:
                info = ydl.extract_info(self.url, download=False)
            self.after(0, lambda: self._apply_info(info))
        except Exception as e:
            self.after(0, lambda: self._apply_info({"error": str(e)}))

    def _apply_info(self, info):
        if "error" in info:
            self.title_lbl.configure(text=os.path.basename(
                self.url.split("?")[0]) or "Unknown")
            self.status_lbl.configure(
                text=f"Could not fetch info — will try download anyway")
            self.dl_btn.configure(state="normal")
            return

        self.video_info = info
        title    = info.get("title","Unknown")
        duration = info.get("duration", 0)
        uploader = info.get("uploader","")
        views    = info.get("view_count", 0)
        thumb    = info.get("thumbnail")

        self.title_lbl.configure(text=title)
        parts = [fmt_dur(duration)]
        if uploader: parts.append(uploader)
        if views:    parts.append(f"{views:,} views")
        self.meta_lbl.configure(text="  ·  ".join(parts))

        # build size map
        self._build_size_map(info)
        self._on_quality(self.qual_combo.get())

        self.status_lbl.configure(text="Ready to download")
        self.dl_btn.configure(state="normal")

        if thumb:
            threading.Thread(target=self._load_thumb,
                              args=(thumb,), daemon=True).start()

    def _build_size_map(self, info):
        self.format_sizes = {}
        duration = info.get("duration", 0)
        for fmt in info.get("formats", []):
            h   = fmt.get("height")
            fs  = fmt.get("filesize") or fmt.get("filesize_approx")
            tbr = fmt.get("tbr")
            if not fs and tbr and duration:
                fs = int(tbr * 1000 / 8 * duration)
            if not fs: continue
            if not h:
                # audio-only track
                if fmt.get("vcodec","") in ("none",""):
                    self.format_sizes["Audio Only"] = max(
                        self.format_sizes.get("Audio Only",0), fs)
                continue
            key = next((q for q, hh in HEIGHT_MAP.items() if h >= hh), None)
            if key:
                self.format_sizes[key] = max(self.format_sizes.get(key,0), fs)
            self.format_sizes["Best"] = max(self.format_sizes.get("Best",0), fs)

    def _load_thumb(self, url):
        try:
            r   = requests.get(url, timeout=10)
            img = Image.open(BytesIO(r.content))
            cimg = ctk.CTkImage(img, size=(140, 80))
            self._ctk_thumbnail = cimg
            self.after(0, lambda: self.thumb.configure(image=cimg, text=""))
        except: pass

    def _on_quality(self, val):
        sz = self.format_sizes.get(val, 0)
        s  = fmt_size(sz)
        self.size_lbl.configure(text=f"≈ {s}" if s else "")

    # ── Download ──────────────────────────────────────────────────────────

    def start_download(self):
        self.is_downloading = True
        self.dl_btn.configure(state="disabled")
        self.pause_btn.configure(state="normal", text="⏸ Pause")
        self.cancel_btn.configure(state="normal")
        self.prog_bar.set(0)

        quality = self.qual_combo.get()
        fmt     = self.fmt_combo.get().lower()
        cf      = int(self.settings.get("concurrent_fragments") or 4)
        out_dir = self.settings.get("download_path")
        os.makedirs(out_dir, exist_ok=True)

        # Format string
        if quality == "Audio Only" or fmt in ("mp3","wav","m4a"):
            fmt_str = "bestaudio/best"
        elif quality in HEIGHT_MAP:
            h = HEIGHT_MAP[quality]
            fmt_str = (f"bestvideo[height<={h}]+bestaudio"
                       f"/best[height<={h}]/best")
        else:
            fmt_str = "bestvideo+bestaudio/best"

        def progress_hook(d):
            if d["status"] == "downloading":
                p = float(d.get("_percent_str","0%").replace("%","").strip() or 0)
                speed = d.get("_speed_str","—")
                eta   = d.get("_eta_str","—")
                dl    = d.get("downloaded_bytes",0)
                tot   = d.get("total_bytes") or d.get("total_bytes_estimate",0)
                size_txt = (f" | {fmt_size(dl)}/{fmt_size(tot)}" if tot
                            else (f" | {fmt_size(dl)}" if dl else ""))
                self.after(0, lambda p=p,s=speed,e=eta,st=size_txt:
                           self._progress_ui(p, s, e, st))

        ydl_opts = {
            "format": fmt_str,
            "outtmpl": os.path.join(out_dir, "%(title)s.%(ext)s"),
            "merge_output_format": fmt if fmt in ("mp4","mkv") else "mp4",
            "concurrent_fragment_downloads": max(1, cf),
            "progress_hooks": [progress_hook],
            "quiet": True,
            "no_warnings": True,
            "retries": 10,
            "fragment_retries": 15,
            "file_access_retries": 5,
            "socket_timeout": 30,
            "http_chunk_size": 10485760,
            "retry_sleep_functions": {"fragment": lambda n: min(4 ** (n - 1), 60)},
        }
        if fmt in ("mp3","wav","m4a") or quality == "Audio Only":
            ydl_opts["postprocessors"] = [{
                "key": "FFmpegExtractAudio",
                "preferredcodec": fmt if fmt in ("mp3","wav","m4a") else "mp3",
                "preferredquality": "192",
            }]

        def run():
            result = self._dl.download(self.url, ydl_opts)
            self.after(0, lambda: self._finished(result))

        threading.Thread(target=run, daemon=True).start()

    def _progress_ui(self, p, speed, eta, size_txt):
        self.prog_bar.set(p / 100)
        self.status_lbl.configure(
            text=f"⬇  {p:.1f}%  |  {speed}  |  ETA {eta}{size_txt}")

    def _finished(self, result):
        self.is_downloading = False
        self.pause_btn.configure(state="disabled", text="⏸ Pause")
        self.cancel_btn.configure(state="disabled")
        self.dl_btn.configure(state="normal", text="↺ Re-download")
        if result is True:
            self.prog_bar.set(1.0)
            self.status_lbl.configure(text="✅  Completed!")
            if self.video_info:
                from core.stats_manager import StatsManager
                StatsManager().add_history({
                    "title":   self.video_info.get("title","Unknown"),
                    "thumbnail": self.video_info.get("thumbnail",""),
                    "quality": self.qual_combo.get(),
                    "format":  self.fmt_combo.get(),
                    "size_mb": 0,
                })
        elif result == "cancelled":
            self.prog_bar.set(0)
            self.status_lbl.configure(text="🚫  Cancelled")
        else:
            self.status_lbl.configure(text=f"❌  {result[:100]}")

    def _toggle_pause(self):
        if self._dl.is_paused:
            self._dl.resume()
            self.pause_btn.configure(text="⏸ Pause")
            self.status_lbl.configure(text="▶  Resuming…")
        else:
            self._dl.pause()
            self.pause_btn.configure(text="▶ Resume")
            self.status_lbl.configure(text="⏸  Paused — click Resume to continue")

    def _cancel(self):
        self._dl.cancel()
        self.cancel_btn.configure(state="disabled")
        self.status_lbl.configure(text="Cancelling…")

    def _remove(self):
        if self.is_downloading:
            self._dl.cancel()
        self.on_remove(self.item_id)


# ─────────────────────────────────────────────────────────────────────────────
# Main frame
# ─────────────────────────────────────────────────────────────────────────────

class DownloaderFrame(ctk.CTkFrame):
    def __init__(self, master):
        super().__init__(master, corner_radius=0, fg_color="transparent")
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(2, weight=1)

        self.settings      = Settings()
        self.stats_manager = StatsManager()
        self.items         = {}
        self._counter      = 0

        self._build_header()
        self._build_input()
        self._build_queue()
        self._build_statusbar()

    # ── Header ────────────────────────────────────────────────────────────

    def _build_header(self):
        h = ctk.CTkFrame(self, fg_color="transparent")
        h.grid(row=0, column=0, padx=20, pady=(20, 5), sticky="ew")
        ctk.CTkLabel(h, text="▶  YouTube Downloader",
                     font=ctk.CTkFont(size=18, weight="bold")).grid(row=0,column=0,sticky="w")
        ctk.CTkLabel(h, text="Videos · Playlists · Multiple links at once",
                     font=ctk.CTkFont(size=12),
                     text_color=("gray50","gray50")).grid(row=1,column=0,sticky="w")

    # ── Input panel ───────────────────────────────────────────────────────

    def _build_input(self):
        panel = ctk.CTkFrame(self)
        panel.grid(row=1, column=0, padx=20, pady=(0,10), sticky="ew")
        panel.grid_columnconfigure(0, weight=1)

        ctk.CTkLabel(panel,
                     text="Paste YouTube video or playlist URLs (one per line):",
                     font=ctk.CTkFont(size=13)).grid(
            row=0, column=0, columnspan=2, padx=15, pady=(12,4), sticky="w")

        text_row = ctk.CTkFrame(panel, fg_color="transparent")
        text_row.grid(row=1, column=0, columnspan=2, padx=15, pady=(0,12), sticky="ew")
        text_row.grid_columnconfigure(0, weight=1)

        self.url_box = ctk.CTkTextbox(text_row, height=80,
                                       font=ctk.CTkFont(size=12))
        self.url_box.grid(row=0, column=0, sticky="ew")
        self.url_box.insert("0.0",
            "https://www.youtube.com/watch?v=...\n"
            "https://www.youtube.com/playlist?list=...")
        self._placeholder = True

        self.url_box.bind("<FocusIn>", self._clear_ph)

        btn_col = ctk.CTkFrame(text_row, fg_color="transparent")
        btn_col.grid(row=0, column=1, padx=(10,0), sticky="ns")

        self.add_btn = ctk.CTkButton(btn_col, text="➕  Add to Queue",
                                      width=150, height=36,
                                      font=ctk.CTkFont(weight="bold"),
                                      command=self._add_urls)
        self.add_btn.pack(side="top", pady=(0,8))

        self.clear_btn = ctk.CTkButton(btn_col, text="🗑  Clear All",
                                        width=150, height=30,
                                        fg_color="transparent", border_width=1,
                                        text_color=("gray20","gray80"),
                                        command=self._clear_all)
        self.clear_btn.pack(side="top")

    def _clear_ph(self, _=None):
        if self._placeholder:
            self.url_box.delete("0.0","end")
            self._placeholder = False

    # ── Queue area ────────────────────────────────────────────────────────

    def _build_queue(self):
        wrapper = ctk.CTkFrame(self, fg_color="transparent")
        wrapper.grid(row=2, column=0, padx=20, pady=5, sticky="nsew")
        wrapper.grid_rowconfigure(1, weight=1)
        wrapper.grid_columnconfigure(0, weight=1)

        ctrl = ctk.CTkFrame(wrapper, fg_color="transparent")
        ctrl.grid(row=0, column=0, sticky="ew", pady=(0,5))
        ctrl.grid_columnconfigure(0, weight=1)

        self.count_lbl = ctk.CTkLabel(ctrl, text="Queue: 0 items",
                                       font=ctk.CTkFont(size=12, weight="bold"))
        self.count_lbl.grid(row=0, column=0, sticky="w")

        # Sequential vs parallel toggle
        self.seq_var = ctk.BooleanVar(value=True)
        self.seq_chk = ctk.CTkCheckBox(ctrl, text="Sequential (one at a time)",
                                        variable=self.seq_var,
                                        font=ctk.CTkFont(size=12))
        self.seq_chk.grid(row=0, column=1, padx=10)

        self.dl_all_btn = ctk.CTkButton(ctrl, text="⬇  Download All",
                                         width=145, height=32, state="disabled",
                                         font=ctk.CTkFont(weight="bold"),
                                         command=self._download_all)
        self.dl_all_btn.grid(row=0, column=2, padx=5)

        self.pause_all_btn = ctk.CTkButton(ctrl, text="⏸  Pause All",
                                            width=110, height=32, state="disabled",
                                            fg_color=("#d4850a","#a0620a"),
                                            hover_color=("#e0960c","#b8720c"),
                                            command=self._pause_all)
        self.pause_all_btn.grid(row=0, column=3, padx=5)

        self.cancel_all_btn = ctk.CTkButton(ctrl, text="✕  Cancel All",
                                             width=110, height=32, state="disabled",
                                             fg_color=("#c0392b","#922b21"),
                                             hover_color=("#e74c3c","#c0392b"),
                                             command=self._cancel_all)
        self.cancel_all_btn.grid(row=0, column=4)

        self.scroll = ctk.CTkScrollableFrame(wrapper, label_text="")
        self.scroll.grid(row=1, column=0, sticky="nsew")
        self.scroll.grid_columnconfigure(0, weight=1)

        self.empty_lbl = ctk.CTkLabel(
            self.scroll,
            text="Queue is empty.\n"
                 "Paste YouTube video or playlist URLs above and click 'Add to Queue'.",
            font=ctk.CTkFont(size=13),
            text_color=("gray50","gray50"))
        self.empty_lbl.grid(row=0, column=0, pady=60)

    # ── Status bar ────────────────────────────────────────────────────────

    def _build_statusbar(self):
        bar = ctk.CTkFrame(self, height=36, fg_color=("gray90","gray15"),
                            corner_radius=0)
        bar.grid(row=3, column=0, sticky="ew", pady=(8,0))
        bar.grid_columnconfigure(1, weight=1)

        self.status_lbl = ctk.CTkLabel(bar, text="Idle — add URLs to start",
                                        font=ctk.CTkFont(size=11),
                                        text_color=("gray40","gray60"))
        self.status_lbl.grid(row=0, column=0, padx=15, pady=6, sticky="w")

        self.overall_bar = ctk.CTkProgressBar(bar, height=8)
        self.overall_bar.grid(row=0, column=1, padx=15, pady=6, sticky="ew")
        self.overall_bar.set(0)

    # ── Actions ───────────────────────────────────────────────────────────

    def _add_urls(self):
        raw = self.url_box.get("0.0","end").strip()
        if not raw: return
        urls = [u.strip() for u in raw.splitlines()
                if u.strip().startswith("http")]
        if not urls: return

        self.add_btn.configure(state="disabled", text="Analyzing…")
        self.status_lbl.configure(text=f"Expanding {len(urls)} URL(s)…")
        threading.Thread(target=self._expand_urls,
                          args=(urls,), daemon=True).start()

    def _expand_urls(self, urls):
        """Expand playlists into individual video URLs (flat & fast)."""
        expanded = []   # list of (url, prefetch_info_or_None)
        for url in urls:
            try:
                opts = {"quiet":True,"no_warnings":True,
                        "extract_flat":"in_playlist"}
                with yt_dlp.YoutubeDL(opts) as ydl:
                    info = ydl.extract_info(url, download=False)

                if info.get("_type") == "playlist" or "entries" in info:
                    entries = list(info.get("entries") or [])
                    for e in entries:
                        vid_url = (e.get("url") or
                                   e.get("webpage_url") or
                                   f"https://www.youtube.com/watch?v={e.get('id','')}")
                        # Pass partial info so item shows title immediately
                        partial = {
                            "title":    e.get("title",""),
                            "duration": e.get("duration"),
                            "uploader": info.get("uploader",""),
                            "thumbnail": e.get("thumbnail") or e.get("thumbnails",[{}])[-1].get("url"),
                        }
                        expanded.append((vid_url, partial))
                else:
                    expanded.append((url, info))
            except Exception as e:
                expanded.append((url, {"error": str(e)}))

        self.after(0, lambda: self._add_to_queue(expanded))

    def _add_to_queue(self, entries):
        self.add_btn.configure(state="normal", text="➕  Add to Queue")

        if self.empty_lbl.winfo_ismapped():
            self.empty_lbl.grid_forget()

        for (url, info) in entries:
            self._counter += 1
            iid  = self._counter
            item = YTQueueItem(
                self.scroll, url, iid,
                on_remove=self._remove_item,
                settings=self.settings,
                prefetch_info=info,
            )
            item.grid(row=iid, column=0, padx=5, pady=6, sticky="ew")
            self.items[iid] = item

        self.url_box.delete("0.0","end")
        self._placeholder = True
        self._refresh_controls()
        self.status_lbl.configure(text=f"Queue: {len(self.items)} item(s)")

    def _remove_item(self, iid):
        if iid in self.items:
            self.items[iid].destroy()
            del self.items[iid]
        self._refresh_controls()
        if not self.items:
            self.empty_lbl.grid(row=0, column=0, pady=60)

    def _clear_all(self):
        for item in list(self.items.values()):
            item._dl.cancel()
            item.destroy()
        self.items.clear()
        self._refresh_controls()
        self.empty_lbl.grid(row=0, column=0, pady=60)

    def _download_all(self):
        items = list(self.items.values())
        if self.seq_var.get():
            # Sequential: start first ready item, chain via callbacks
            threading.Thread(target=self._sequential_run,
                              args=(items,), daemon=True).start()
        else:
            # Parallel: start all at once
            for item in items:
                if not item.is_downloading:
                    item.start_download()
                    time.sleep(0.15)
        self.status_lbl.configure(text=f"Downloading {len(items)} item(s)…")

    def _sequential_run(self, items):
        for item in items:
            if item.is_downloading:
                continue
            # wait until its download_btn is enabled (info fetched)
            for _ in range(60):
                if item.dl_btn.cget("state") == "normal":
                    break
                time.sleep(0.5)
            self.after(0, item.start_download)
            # wait until done
            while item.is_downloading:
                time.sleep(0.4)

    def _pause_all(self):
        for item in self.items.values():
            if item.is_downloading and not item._dl.is_paused:
                item._dl.pause()
                item.pause_btn.configure(text="▶ Resume")
                item.status_lbl.configure(text="⏸  Paused")
        self.status_lbl.configure(text="All downloads paused")

    def _cancel_all(self):
        for item in self.items.values():
            if item.is_downloading:
                item._dl.cancel()
        self.status_lbl.configure(text="All downloads cancelled")

    def _refresh_controls(self):
        n = len(self.items)
        self.count_lbl.configure(text=f"Queue: {n} item{'s' if n!=1 else ''}")
        s = "normal" if n > 0 else "disabled"
        self.dl_all_btn.configure(state=s)
        self.pause_all_btn.configure(state=s)
        self.cancel_all_btn.configure(state=s)
        if n == 0:
            self.status_lbl.configure(text="Idle — add URLs to start")
            self.overall_bar.set(0)
