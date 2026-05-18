import customtkinter as ctk
from PIL import Image
import os
from ui.downloader_frame import DownloaderFrame
from ui.hls_downloader_frame import HLSDownloaderFrame
from ui.history_frame import HistoryFrame
from ui.stats_frame import StatsFrame
from ui.settings_frame import SettingsFrame

class MainWindow(ctk.CTk):
    def __init__(self):
        super().__init__()

        self.title("YouTube Downloader Pro")
        self.geometry("1150x750")

        self.grid_rowconfigure(0, weight=1)
        self.grid_columnconfigure(1, weight=1)

        # Navigation frame
        self.navigation_frame = ctk.CTkFrame(self, corner_radius=0)
        self.navigation_frame.grid(row=0, column=0, sticky="nsew")
        self.navigation_frame.grid_rowconfigure(7, weight=1)

        self.navigation_frame_label = ctk.CTkLabel(
            self.navigation_frame, text="  YT Downloader Pro",
            compound="left", font=ctk.CTkFont(size=15, weight="bold"))
        self.navigation_frame_label.grid(row=0, column=0, padx=20, pady=20)

        nav_btn_cfg = dict(
            corner_radius=0, height=40, border_spacing=10,
            fg_color="transparent", text_color=("gray10", "gray90"),
            hover_color=("gray70", "gray30"), anchor="w"
        )

        self.home_button = ctk.CTkButton(
            self.navigation_frame, text="▶  YT Downloader",
            command=self.home_button_event, **nav_btn_cfg)
        self.home_button.grid(row=1, column=0, sticky="ew")

        self.hls_button = ctk.CTkButton(
            self.navigation_frame, text="🔗  HLS Downloader",
            command=self.hls_button_event, **nav_btn_cfg)
        self.hls_button.grid(row=2, column=0, sticky="ew")

        self.sep_label = ctk.CTkLabel(
            self.navigation_frame, text="─────────────",
            font=ctk.CTkFont(size=10), text_color=("gray60", "gray40"))
        self.sep_label.grid(row=3, column=0, padx=10, pady=(5, 0))

        self.history_button = ctk.CTkButton(
            self.navigation_frame, text="🕓  History",
            command=self.history_button_event, **nav_btn_cfg)
        self.history_button.grid(row=4, column=0, sticky="ew")

        self.stats_button = ctk.CTkButton(
            self.navigation_frame, text="📊  Statistics",
            command=self.stats_button_event, **nav_btn_cfg)
        self.stats_button.grid(row=5, column=0, sticky="ew")

        self.settings_button = ctk.CTkButton(
            self.navigation_frame, text="⚙  Settings",
            command=self.settings_button_event, **nav_btn_cfg)
        self.settings_button.grid(row=6, column=0, sticky="ew")

        # Frames
        self.home_frame = DownloaderFrame(self)
        self.hls_frame = HLSDownloaderFrame(self)
        self.history_frame = HistoryFrame(self)
        self.stats_frame = StatsFrame(self)
        self.settings_frame = SettingsFrame(self)

        self.select_frame_by_name("home")

    def select_frame_by_name(self, name):
        active_color = ("gray75", "gray25")
        self.home_button.configure(fg_color=active_color if name == "home" else "transparent")
        self.hls_button.configure(fg_color=active_color if name == "hls" else "transparent")
        self.history_button.configure(fg_color=active_color if name == "history" else "transparent")
        self.stats_button.configure(fg_color=active_color if name == "stats" else "transparent")
        self.settings_button.configure(fg_color=active_color if name == "settings" else "transparent")

        frames = {
            "home": self.home_frame,
            "hls": self.hls_frame,
            "history": self.history_frame,
            "stats": self.stats_frame,
            "settings": self.settings_frame,
        }
        for fname, frame in frames.items():
            if fname == name:
                frame.grid(row=0, column=1, sticky="nsew")
                if fname == "history":
                    frame.refresh()
                elif fname == "stats":
                    frame.refresh()
            else:
                frame.grid_forget()

    def home_button_event(self): self.select_frame_by_name("home")
    def hls_button_event(self): self.select_frame_by_name("hls")
    def history_button_event(self): self.select_frame_by_name("history")
    def stats_button_event(self): self.select_frame_by_name("stats")
    def settings_button_event(self): self.select_frame_by_name("settings")
