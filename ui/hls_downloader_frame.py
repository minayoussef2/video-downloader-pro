import customtkinter as ctk
import threading
import os
import re
import time
import yt_dlp
from ui.downloader_frame import ItemDownloader
from utils.settings import Settings
from core.stats_manager import StatsManager


def format_size(bytes_val):
    if bytes_val <= 0:
        return "—"
    if bytes_val < 1024 * 1024:
        return f"{bytes_val / 1024:.1f} KB"
    elif bytes_val < 1024 * 1024 * 1024:
        return f"{bytes_val / (1024 * 1024):.1f} MB"
    else:
        return f"{bytes_val / (1024 * 1024 * 1024):.2f} GB"

def format_duration(seconds):
    if not seconds: return "—"
    try:
        s = int(seconds)
        h, r = divmod(s, 3600)
        m, s = divmod(r, 60)
        return f"{h}:{m:02d}:{s:02d}" if h else f"{m}:{s:02d}"
    except: return "—"

class HLSDownloadItem(ctk.CTkFrame):
    """A single HLS download card with pause/resume/cancel support."""

    QUALITY_OPTIONS = ["Best Available", "1080p", "720p", "480p", "360p", "240p", "Audio Only"]

    def __init__(self, master, url, referer, item_id, on_remove, settings):
        super().__init__(master, corner_radius=10, border_width=1,
                         border_color=("gray70", "gray30"))
        self.url          = url
        self.referer      = referer
        self.item_id      = item_id
        self.on_remove    = on_remove
        self.settings     = settings
        self.is_downloading = False
        self._dl          = ItemDownloader()
        self.video_info   = None
        self.format_sizes = {}
        self.analyze_ok   = False

        self.grid_columnconfigure(1, weight=1)
        self._build_ui()
        threading.Thread(target=self._analyze, daemon=True).start()

    def _build_ui(self):
        # Row 0: status + title + remove
        self.status_dot = ctk.CTkLabel(self, text="⏳", width=30,
                                        font=ctk.CTkFont(size=16))
        self.status_dot.grid(row=0, column=0, padx=(10,5), pady=(10,2), sticky="nw")

        self.title_label = ctk.CTkLabel(self, text="Analyzing…",
                                         font=ctk.CTkFont(size=13, weight="bold"),
                                         anchor="w", wraplength=500)
        self.title_label.grid(row=0, column=1, padx=5, pady=(10,2), sticky="ew")

        self.remove_btn = ctk.CTkButton(self, text="✕", width=28, height=26,
                                         fg_color="transparent",
                                         text_color=("gray40","gray60"),
                                         hover_color=("gray80","gray25"),
                                         command=self._remove)
        self.remove_btn.grid(row=0, column=2, padx=(5,10), pady=(8,2), sticky="ne")

        # Row 1: meta
        self.meta_label = ctk.CTkLabel(self, text="Duration: —",
                                        font=ctk.CTkFont(size=11),
                                        text_color=("gray40","gray60"), anchor="w")
        self.meta_label.grid(row=1, column=1, padx=5, pady=2, sticky="ew")

        # Row 2: truncated url
        short = self.url if len(self.url) <= 72 else self.url[:69]+"…"
        ctk.CTkLabel(self, text=short, font=ctk.CTkFont(size=10),
                     text_color=("gray50","gray50"), anchor="w").grid(
            row=2, column=1, padx=5, pady=(0,5), sticky="ew")

        # Row 3: options
        opts = ctk.CTkFrame(self, fg_color="transparent")
        opts.grid(row=3, column=0, columnspan=3, padx=10, pady=5, sticky="ew")
        opts.grid_columnconfigure(2, weight=1)

        ctk.CTkLabel(opts, text="Quality:", font=ctk.CTkFont(size=12)).grid(
            row=0, column=0, padx=(5,2))

        self.quality_combo = ctk.CTkComboBox(opts, values=self.QUALITY_OPTIONS,
                                              width=158,
                                              command=self._on_quality_change)
        self.quality_combo.set("Best Available")
        self.quality_combo.grid(row=0, column=1, padx=(2,10))

        self.size_estimate_label = ctk.CTkLabel(opts, text="Est. size: —",
                                                  font=ctk.CTkFont(size=11),
                                                  text_color=("gray40","gray60"))
        self.size_estimate_label.grid(row=0, column=2, padx=(0,10), sticky="w")

        self.download_btn = ctk.CTkButton(opts, text="Download",
                                           width=100, height=30, state="disabled",
                                           font=ctk.CTkFont(weight="bold"),
                                           command=self.start_download)
        self.download_btn.grid(row=0, column=3, padx=(5,4))

        self.pause_btn = ctk.CTkButton(opts, text="⏸ Pause",
                                        width=90, height=30, state="disabled",
                                        fg_color=("#d4850a","#a0620a"),
                                        hover_color=("#e0960c","#b8720c"),
                                        command=self._toggle_pause)
        self.pause_btn.grid(row=0, column=4, padx=(0,4))

        self.cancel_btn = ctk.CTkButton(opts, text="Cancel",
                                         width=80, height=30, state="disabled",
                                         fg_color=("#c0392b","#922b21"),
                                         hover_color=("#e74c3c","#c0392b"),
                                         command=self.cancel_download)
        self.cancel_btn.grid(row=0, column=5, padx=(0,5))

        # Row 4: progress
        self.progress_bar = ctk.CTkProgressBar(self)
        self.progress_bar.grid(row=4, column=0, columnspan=3,
                                padx=10, pady=(5,3), sticky="ew")
        self.progress_bar.set(0)

        # Row 5: status
        self.status_label = ctk.CTkLabel(self, text="Analyzing stream…",
                                          font=ctk.CTkFont(size=11),
                                          text_color=("gray40","gray60"), anchor="w")
        self.status_label.grid(row=5, column=0, columnspan=3,
                                padx=12, pady=(0,10), sticky="ew")

    # ── Analysis ──────────────────────────────────────────────────────────

    def _analyze(self):
        try:
            http_headers = {
                "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
            }
            if self.referer:
                http_headers["Referer"] = self.referer

            ydl_opts = {
                "quiet": True, "no_warnings": True,
                "retries": 10,
                "fragment_retries": 15,
                "file_access_retries": 5,
                "socket_timeout": 30,
                "http_chunk_size": 10485760,
                "retry_sleep_functions": {"fragment": lambda n: min(4 ** (n - 1), 60)},
                "allowed_extensions": "ALL",
                "http_headers": http_headers,
            }
            with yt_dlp.YoutubeDL(ydl_opts) as ydl:
                info = ydl.extract_info(self.url, download=False)

            title    = (info.get("title") or os.path.basename(
                         self.url.split("?")[0]) or "HLS Stream")
            duration = info.get("duration")
            formats  = info.get("formats", [])

            self.format_sizes = {}
            for fmt in formats:
                h   = fmt.get("height")
                fs  = fmt.get("filesize") or fmt.get("filesize_approx")
                tbr = fmt.get("tbr")
                if h and duration and not fs and tbr:
                    fs = int(tbr * 1000 / 8 * duration)
                if h and fs:
                    key = self._height_to_key(h)
                    if key not in self.format_sizes or fs > self.format_sizes[key]:
                        self.format_sizes[key] = fs

            if self.format_sizes:
                self.format_sizes["Best Available"] = max(self.format_sizes.values())

            self.video_info = {"title": title, "duration": duration}
            self.analyze_ok = True
            self.after(0, lambda: self._update_after_analyze(title, duration))

        except Exception as e:
            self.after(0, lambda: self._analyze_failed(str(e)))

    def _height_to_key(self, h):
        if h >= 1080: return "1080p"
        if h >= 720:  return "720p"
        if h >= 480:  return "480p"
        if h >= 360:  return "360p"
        return "240p"

    def _update_after_analyze(self, title, duration):
        self.title_label.configure(text=title)
        dur = format_duration(duration)
        self.meta_label.configure(text=f"Duration: {dur}")
        self.status_dot.configure(text="🟢")
        self.status_label.configure(text="Ready to download")
        self.download_btn.configure(state="normal")
        self._update_size_estimate("Best Available")

    def _analyze_failed(self, error):
        title = os.path.basename(self.url.split("?")[0]) or "HLS Stream"
        self.title_label.configure(text=title)
        self.status_dot.configure(text="🟡")
        self.status_label.configure(
            text="Metadata unavailable — will download with best quality")
        self.quality_combo.set("Best Available")
        self.download_btn.configure(state="normal")

    def _on_quality_change(self, value):
        self._update_size_estimate(value)

    def _update_size_estimate(self, quality):
        sz = self.format_sizes.get(quality, 0)
        self.size_estimate_label.configure(
            text=f"Est. size: {format_size(sz)}" if sz else "Est. size: —")

    # ── Download ──────────────────────────────────────────────────────────

    def start_download(self):
        self.is_downloading = True
        self.download_btn.configure(state="disabled")
        self.pause_btn.configure(state="normal", text="⏸ Pause")
        self.cancel_btn.configure(state="normal")
        self.status_dot.configure(text="🔵")
        self.progress_bar.set(0)

        quality  = self.quality_combo.get()
        cf       = int(self.settings.get("concurrent_fragments") or 4)
        out_path = self.settings.get("download_path")
        os.makedirs(out_path, exist_ok=True)

        title = "hls_video"
        if self.video_info and self.video_info.get("title"):
            title = re.sub(r'[<>:"/\\|?*]', "_", self.video_info["title"])

        out_file = os.path.join(out_path, f"{title}.mp4")
        threading.Thread(target=self._dl_thread,
                          args=(quality, cf, out_file), daemon=True).start()

    def _build_fmt(self, quality):
        if not self.analyze_ok or quality == "Best Available":
            return "best"
        q_map = {
            "1080p": "bestvideo[height<=1080]+bestaudio/best[height<=1080]/best",
            "720p":  "bestvideo[height<=720]+bestaudio/best[height<=720]/best",
            "480p":  "bestvideo[height<=480]+bestaudio/best[height<=480]/best",
            "360p":  "bestvideo[height<=360]+bestaudio/best[height<=360]/best",
            "240p":  "bestvideo[height<=240]+bestaudio/best[height<=240]/best",
            "Audio Only": "bestaudio/best",
        }
        return q_map.get(quality, "best")

    def _dl_thread(self, quality, cf, out_file):
        try:
            http_headers = {
                "User-Agent": ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                               "AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36")
            }
            if self.referer:
                http_headers["Referer"] = self.referer

            def progress_hook(d):
                if d["status"] == "downloading":
                    p   = float(d.get("_percent_str","0%").replace("%","").strip() or 0)
                    spd = d.get("_speed_str","—")
                    eta = d.get("_eta_str","—")
                    fi  = d.get("fragment_index", 0)
                    fn  = d.get("fragment_count", 0)
                    dl  = d.get("downloaded_bytes", 0)
                    tot = d.get("total_bytes") or d.get("total_bytes_estimate", 0)
                    frag_txt = f" | Frag {fi}/{fn}" if fn else ""
                    size_txt = (f" | {format_size(dl)}/{format_size(tot)}" if tot
                                else (f" | {format_size(dl)}" if dl else ""))
                    self.after(0, lambda p=p,s=spd,e=eta,ft=frag_txt,st=size_txt:
                               self._progress_ui(p, s, e, ft, st))

            ydl_opts = {
                "format": self._build_fmt(quality),
                "outtmpl": out_file.replace(".mp4", ".%(ext)s"),
                "merge_output_format": "mp4",
                "allowed_extensions": "ALL",
                "http_headers": http_headers,
                "progress_hooks": [progress_hook],
                "concurrent_fragment_downloads": max(1, cf),
                "quiet": True, "no_warnings": True,
            }

            result = self._dl.download(self.url, ydl_opts)
            if result is True:
                self.after(0, self._on_success)
            elif result == "cancelled":
                self.after(0, self._on_cancelled)
            else:
                self.after(0, lambda r=result: self._on_error(r))

        except Exception as e:
            self.after(0, lambda err=str(e): self._on_error(err))

    def _progress_ui(self, p, speed, eta, frag_txt, size_txt):
        self.progress_bar.set(p / 100)
        self.status_label.configure(
            text=f"Downloading: {p:.1f}%  |  Speed: {speed}  |  ETA: {eta}{frag_txt}{size_txt}")

    def _on_success(self):
        self.is_downloading = False
        self.progress_bar.set(1.0)
        self.status_dot.configure(text="✅")
        self.status_label.configure(text="Download completed successfully!")
        self.download_btn.configure(state="normal", text="Re-download")
        self.pause_btn.configure(state="disabled", text="⏸ Pause")
        self.cancel_btn.configure(state="disabled")

    def _on_cancelled(self):
        self.is_downloading = False
        self.status_dot.configure(text="🟡")
        self.status_label.configure(text="Download cancelled")
        self.progress_bar.set(0)
        self.download_btn.configure(state="normal")
        self.pause_btn.configure(state="disabled", text="⏸ Pause")
        self.cancel_btn.configure(state="disabled")

    def _on_error(self, error):
        self.is_downloading = False
        self.status_dot.configure(text="🔴")
        self.status_label.configure(text=f"Error: {str(error)[:120]}")
        self.download_btn.configure(state="normal")
        self.pause_btn.configure(state="disabled", text="⏸ Pause")
        self.cancel_btn.configure(state="disabled")

    def _toggle_pause(self):
        if self._dl.is_paused:
            self._dl.resume()
            self.pause_btn.configure(text="⏸ Pause")
            self.status_label.configure(text="▶  Resuming…")
        else:
            self._dl.pause()
            self.pause_btn.configure(text="▶ Resume")
            self.status_label.configure(text="⏸  Paused — click Resume to continue")

    def cancel_download(self):
        self._dl.cancel()
        self.cancel_btn.configure(state="disabled")

    def _remove(self):
        if self.is_downloading:
            self._dl.cancel()
        self.on_remove(self.item_id)



# ═══════════════════════════════════════════════════════════════════════════════

class HLSDownloaderFrame(ctk.CTkFrame):
    def __init__(self, master):
        super().__init__(master, corner_radius=0, fg_color="transparent")
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(2, weight=1)

        self.settings = Settings()
        self.items    = {}
        self._item_counter = 0

        self._build_header()
        self._build_input_area()
        self._build_queue_area()
        self._build_bottom_bar()

    def _build_header(self):
        h = ctk.CTkFrame(self, fg_color="transparent")
        h.grid(row=0, column=0, padx=20, pady=(20, 5), sticky="ew")
        ctk.CTkLabel(h, text="🔗  HLS / M3U8 Stream Downloader",
                     font=ctk.CTkFont(size=18, weight="bold")).grid(row=0, column=0, sticky="w")
        ctk.CTkLabel(h, text="Supports BunnyCDN, CDN streams, and direct .m3u8 links",
                     font=ctk.CTkFont(size=12),
                     text_color=("gray50", "gray50")).grid(row=1, column=0, sticky="w")

    def _build_input_area(self):
        f = ctk.CTkFrame(self)
        f.grid(row=1, column=0, padx=20, pady=10, sticky="ew")
        f.grid_columnconfigure(0, weight=1)

        ctk.CTkLabel(f, text="Paste one or more .m3u8 URLs (one per line):",
                     font=ctk.CTkFont(size=13)).grid(
            row=0, column=0, columnspan=2, padx=15, pady=(12, 4), sticky="w")

        # Referer row (before adding to queue)
        ref_row = ctk.CTkFrame(f, fg_color="transparent")
        ref_row.grid(row=1, column=0, columnspan=2, padx=15, pady=(0, 6), sticky="ew")
        ref_row.grid_columnconfigure(1, weight=1)

        ctk.CTkLabel(ref_row, text="Referer URL:",
                     font=ctk.CTkFont(size=12)).grid(row=0, column=0, padx=(0, 8), sticky="w")
        self.referer_entry = ctk.CTkEntry(
            ref_row,
            placeholder_text="https://LINK-OF-THE-SITE-THAT-YOU-DOWNLOAD-FROM.com  (required for CDN-protected streams)",
            height=30)
        self.referer_entry.grid(row=0, column=1, sticky="ew")

        # URL text area + buttons
        txt_row = ctk.CTkFrame(f, fg_color="transparent")
        txt_row.grid(row=2, column=0, columnspan=2, padx=15, pady=(0, 12), sticky="ew")
        txt_row.grid_columnconfigure(0, weight=1)

        self.url_textbox = ctk.CTkTextbox(txt_row, height=75, font=ctk.CTkFont(size=12))
        self.url_textbox.grid(row=0, column=0, sticky="ew")

        btn_col = ctk.CTkFrame(txt_row, fg_color="transparent")
        btn_col.grid(row=0, column=1, padx=(10, 0), sticky="ns")

        ctk.CTkButton(btn_col, text="➕  Add to Queue", width=145, height=36,
                      command=self._add_urls,
                      font=ctk.CTkFont(weight="bold")).pack(side="top", pady=(0, 8))
        ctk.CTkButton(btn_col, text="🗑  Clear All", width=145, height=30,
                      fg_color="transparent", border_width=1,
                      text_color=("gray20", "gray80"),
                      command=self._clear_all).pack(side="top")

    def _build_queue_area(self):
        wrap = ctk.CTkFrame(self, fg_color="transparent")
        wrap.grid(row=2, column=0, padx=20, pady=5, sticky="nsew")
        wrap.grid_rowconfigure(1, weight=1)
        wrap.grid_columnconfigure(0, weight=1)

        hdr = ctk.CTkFrame(wrap, fg_color="transparent")
        hdr.grid(row=0, column=0, sticky="ew", pady=(0, 5))
        hdr.grid_columnconfigure(0, weight=1)

        self.queue_count_label = ctk.CTkLabel(hdr, text="Queue: 0 items",
                                               font=ctk.CTkFont(size=12, weight="bold"))
        self.queue_count_label.grid(row=0, column=0, sticky="w")

        self.download_all_btn = ctk.CTkButton(hdr, text="⬇  Download All",
                                               width=145, height=32, state="disabled",
                                               command=self._download_all,
                                               font=ctk.CTkFont(weight="bold"))
        self.download_all_btn.grid(row=0, column=1, padx=5)

        self.pause_all_btn = ctk.CTkButton(hdr, text="⏸  Pause All",
                                            width=110, height=32, state="disabled",
                                            fg_color=("#d4850a", "#a0620a"),
                                            hover_color=("#e0960c", "#b8720c"),
                                            command=self._toggle_pause_all)
        self.pause_all_btn.grid(row=0, column=2, padx=5)
        self._all_paused = False

        self.cancel_all_btn = ctk.CTkButton(hdr, text="✖  Cancel All",
                                             width=110, height=32, state="disabled",
                                             fg_color=("#c0392b", "#922b21"),
                                             hover_color=("#e74c3c", "#c0392b"),
                                             command=self._cancel_all)
        self.cancel_all_btn.grid(row=0, column=3)

        self.scroll_frame = ctk.CTkScrollableFrame(wrap, label_text="")
        self.scroll_frame.grid(row=1, column=0, sticky="nsew")
        self.scroll_frame.grid_columnconfigure(0, weight=1)

        self.empty_label = ctk.CTkLabel(
            self.scroll_frame,
            text="No downloads in queue.\n"
                 "Set your Referer above, paste .m3u8 links, then click 'Add to Queue'.",
            font=ctk.CTkFont(size=13),
            text_color=("gray50", "gray50"))
        self.empty_label.grid(row=0, column=0, pady=60)

    def _build_bottom_bar(self):
        bar = ctk.CTkFrame(self, height=38, fg_color=("gray90", "gray15"), corner_radius=0)
        bar.grid(row=3, column=0, sticky="ew", pady=(10, 0))
        bar.grid_columnconfigure(1, weight=1)

        self.overall_status = ctk.CTkLabel(bar, text="Idle — Add URLs to start",
                                            font=ctk.CTkFont(size=11),
                                            text_color=("gray40", "gray60"))
        self.overall_status.grid(row=0, column=0, padx=15, pady=8, sticky="w")

        self.overall_progress = ctk.CTkProgressBar(bar, height=8)
        self.overall_progress.grid(row=0, column=1, padx=15, pady=8, sticky="ew")
        self.overall_progress.set(0)

    # ── Queue actions ─────────────────────────────────────────────────────────

    def _add_urls(self):
        raw = self.url_textbox.get("0.0", "end").strip()
        if not raw:
            return
        urls = [u.strip() for u in raw.splitlines()
                if u.strip() and u.strip().startswith("http")]
        if not urls:
            return

        referer = self.referer_entry.get().strip()

        if self.empty_label.winfo_ismapped():
            self.empty_label.grid_forget()

        for url in urls:
            self._item_counter += 1
            iid = self._item_counter
            item = HLSDownloadItem(self.scroll_frame, url, referer, iid,
                                    on_remove=self._remove_item,
                                    settings=self.settings)
            item.grid(row=iid, column=0, padx=5, pady=6, sticky="ew")
            self.items[iid] = item

        self.url_textbox.delete("0.0", "end")
        self._update_queue_ui()

    def _remove_item(self, iid):
        if iid in self.items:
            self.items[iid].destroy()
            del self.items[iid]
        self._update_queue_ui()
        if not self.items:
            self.empty_label.grid(row=0, column=0, pady=60)

    def _clear_all(self):
        for item in list(self.items.values()):
            item.destroy()
        self.items.clear()
        self._update_queue_ui()
        self.empty_label.grid(row=0, column=0, pady=60)

    def _download_all(self):
        for item in self.items.values():
            if not item.is_downloading:
                item.start_download()
                time.sleep(0.15)
        self.pause_all_btn.configure(state="normal")
        self.cancel_all_btn.configure(state="normal")
        self.overall_status.configure(text=f"Downloading {len(self.items)} item(s)...")

    def _toggle_pause_all(self):
        self._all_paused = not getattr(self, "_all_paused", False)
        for item in self.items.values():
            if item.is_downloading:
                if self._all_paused:
                    item._dl.pause()
                    item.pause_btn.configure(text="▶ Resume")
                else:
                    item._dl.resume()
                    item.pause_btn.configure(text="⏸ Pause")
        lbl = "▶  Resume All" if self._all_paused else "⏸  Pause All"
        self.pause_all_btn.configure(text=lbl)
        status = "All downloads paused" if self._all_paused else "All downloads resumed"
        self.overall_status.configure(text=status)

    def _cancel_all(self):
        for item in self.items.values():
            if item.is_downloading:
                item.cancel_download()
        self.overall_status.configure(text="All downloads cancelled")

    def _update_queue_ui(self):
        n = len(self.items)
        self.queue_count_label.configure(text=f"Queue: {n} item{'s' if n != 1 else ''}")
        s = "normal" if n > 0 else "disabled"
        self.download_all_btn.configure(state=s)
        self.pause_all_btn.configure(state=s)
        self.cancel_all_btn.configure(state=s)
        if n == 0:
            self.overall_status.configure(text="Idle — Add URLs to start")
            self.overall_progress.set(0)
            self._all_paused = False
