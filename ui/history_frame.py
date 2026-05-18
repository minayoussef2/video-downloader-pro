import customtkinter as ctk
from core.stats_manager import StatsManager
from utils.settings import Settings
import os
import subprocess
import sys

class HistoryFrame(ctk.CTkFrame):
    def __init__(self, master):
        super().__init__(master, corner_radius=0, fg_color="transparent")
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)
        
        self.stats_manager = StatsManager()
        self.settings = Settings()
        
        # Header row
        header = ctk.CTkFrame(self, fg_color="transparent")
        header.grid(row=0, column=0, padx=20, pady=20, sticky="ew")
        header.grid_columnconfigure(0, weight=1)

        self.label = ctk.CTkLabel(header, text="Download History",
                                   font=ctk.CTkFont(size=20, weight="bold"))
        self.label.grid(row=0, column=0, sticky="w")

        self.clear_btn = ctk.CTkButton(header, text="🗑  Clear History", width=130, height=30,
                                        fg_color="transparent", border_width=1,
                                        text_color=("gray20", "gray80"),
                                        command=self._clear_history)
        self.clear_btn.grid(row=0, column=1, sticky="e")

        self.open_folder_btn = ctk.CTkButton(header, text="📁  Open Download Folder",
                                              width=170, height=30,
                                              command=self._open_download_folder)
        self.open_folder_btn.grid(row=0, column=2, padx=(10, 0), sticky="e")

        self.scrollable_frame = ctk.CTkScrollableFrame(self, label_text="Recent Downloads")
        self.scrollable_frame.grid(row=1, column=0, padx=20, pady=10, sticky="nsew")
        self.scrollable_frame.grid_columnconfigure(0, weight=1)
        
        self.refresh()

    def _open_download_folder(self):
        """Open the downloads folder in the system file explorer."""
        path = self.settings.get("download_path")
        os.makedirs(path, exist_ok=True)
        self._reveal_in_explorer(path)

    def _reveal_in_explorer(self, path):
        """Cross-platform: open a folder or reveal a file in explorer."""
        try:
            if sys.platform == "win32":
                if os.path.isfile(path):
                    subprocess.Popen(f'explorer /select,"{path}"')
                else:
                    os.startfile(path)
            elif sys.platform == "darwin":
                subprocess.Popen(["open", path])
            else:
                subprocess.Popen(["xdg-open", path])
        except Exception as e:
            print(f"Could not open folder: {e}")

    def _clear_history(self):
        self.stats_manager.clear_history()
        self.refresh()

    def refresh(self):
        for widget in self.scrollable_frame.winfo_children():
            widget.destroy()
            
        history = self.stats_manager.get_history()
        if not history:
            no_history = ctk.CTkLabel(self.scrollable_frame,
                                       text="No download history yet.",
                                       font=ctk.CTkFont(size=13),
                                       text_color=("gray50", "gray50"))
            no_history.grid(row=0, column=0, pady=40)
            return
            
        for i, item in enumerate(reversed(history)):  # newest first
            item_frame = ctk.CTkFrame(self.scrollable_frame, corner_radius=8)
            item_frame.grid(row=i, column=0, padx=10, pady=5, sticky="ew")
            item_frame.grid_columnconfigure(1, weight=1)
            
            title     = item.get('title', 'Unknown')
            timestamp = item.get('timestamp', '')
            quality   = item.get('quality', '')
            fmt       = item.get('format', '')
            size_mb   = item.get('size_mb', 0)
            
            # Icon
            icon = ctk.CTkLabel(item_frame, text="🎬", font=ctk.CTkFont(size=20), width=40)
            icon.grid(row=0, column=0, rowspan=2, padx=(10, 5), pady=8)

            title_label = ctk.CTkLabel(item_frame, text=title,
                                        font=ctk.CTkFont(weight="bold"), anchor="w")
            title_label.grid(row=0, column=1, padx=5, pady=(8, 1), sticky="w")
            
            meta_parts = [p for p in [timestamp, quality, fmt,
                                       f"{size_mb:.1f} MB" if size_mb else ""] if p]
            info_label = ctk.CTkLabel(item_frame,
                                       text="  ·  ".join(meta_parts),
                                       font=ctk.CTkFont(size=10),
                                       text_color=("gray50", "gray50"))
            info_label.grid(row=1, column=1, padx=5, pady=(0, 8), sticky="w")

            # Open folder button — opens the download folder
            dl_path = self.settings.get("download_path")
            open_btn = ctk.CTkButton(item_frame, text="📁 Open Folder",
                                      width=110, height=28,
                                      command=lambda p=dl_path: self._reveal_in_explorer(p))
            open_btn.grid(row=0, column=2, rowspan=2, padx=10, sticky="e")
