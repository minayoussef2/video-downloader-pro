import customtkinter as ctk
from utils.settings import Settings
import os
from tkinter import filedialog


class SettingsFrame(ctk.CTkFrame):
    def __init__(self, master):
        super().__init__(master, corner_radius=0, fg_color="transparent")
        self.grid_columnconfigure(0, weight=1)

        self.settings = Settings()

        # ── Title ─────────────────────────────────────────────────────────────
        self.label = ctk.CTkLabel(self, text="Settings",
                                   font=ctk.CTkFont(size=20, weight="bold"))
        self.label.grid(row=0, column=0, padx=20, pady=20, sticky="w")

        # ── Appearance ────────────────────────────────────────────────────────
        app_frame = self._section("Appearance", row=1)
        ctk.CTkLabel(app_frame, text="Theme:").grid(row=0, column=0, padx=20, pady=12, sticky="w")
        self.appearance_menu = ctk.CTkOptionMenu(
            app_frame, values=["Light", "Dark", "System"],
            command=self._change_theme)
        self.appearance_menu.set(self.settings.get("theme"))
        self.appearance_menu.grid(row=0, column=1, padx=10, pady=12)

        # ── Download Folder ───────────────────────────────────────────────────
        path_frame = self._section("Download Folder", row=2)
        ctk.CTkLabel(path_frame, text="Save to:").grid(row=0, column=0, padx=20, pady=12, sticky="w")
        self.path_entry = ctk.CTkEntry(path_frame, width=320)
        self.path_entry.insert(0, self.settings.get("download_path"))
        self.path_entry.grid(row=0, column=1, padx=10, pady=12)
        ctk.CTkButton(path_frame, text="Browse", width=80,
                      command=self._browse_path).grid(row=0, column=2, padx=10, pady=12)

        # ── Default Quality ───────────────────────────────────────────────────
        q_frame = self._section("Default Quality", row=3)
        ctk.CTkLabel(q_frame, text="Quality:").grid(row=0, column=0, padx=20, pady=12, sticky="w")
        self.quality_menu = ctk.CTkOptionMenu(
            q_frame, values=["Best", "1080p", "720p", "480p", "360p", "Audio Only"])
        self.quality_menu.set(self.settings.get("default_quality"))
        self.quality_menu.grid(row=0, column=1, padx=10, pady=12)

        # ── Multi-Connection Downloads (IDM-style) ────────────────────────────
        dl_frame = self._section("⚡  Multi-Connection Downloads", row=4)
        dl_frame.grid_columnconfigure(2, weight=1)

        ctk.CTkLabel(dl_frame,
                     text="Split each file into parallel connections to maximize download speed.\n"
                          "Recommended: 4-8 connections.",
                     font=ctk.CTkFont(size=11),
                     text_color=("gray40", "gray60"),
                     justify="left").grid(row=0, column=0, columnspan=4,
                                          padx=20, pady=(12, 4), sticky="w")

        ctk.CTkLabel(dl_frame, text="Connections:").grid(row=1, column=0, padx=20, pady=8, sticky="w")

        # Slider 1–16
        self.conn_slider = ctk.CTkSlider(dl_frame, from_=1, to=16, number_of_steps=15,
                                          width=260, command=self._on_conn_slider)
        saved_conn = int(self.settings.get("concurrent_fragments") or 4)
        self.conn_slider.set(saved_conn)
        self.conn_slider.grid(row=1, column=1, padx=(10, 6), pady=8)

        self.conn_value_label = ctk.CTkLabel(dl_frame, text=f"{saved_conn}",
                                              font=ctk.CTkFont(size=14, weight="bold"),
                                              width=30)
        self.conn_value_label.grid(row=1, column=2, padx=(0, 8), pady=8)

        # Speed indicator badge
        self.speed_badge = ctk.CTkLabel(dl_frame, text="",
                                         font=ctk.CTkFont(size=11),
                                         corner_radius=6,
                                         fg_color=("gray80", "gray25"),
                                         padx=10, pady=4)
        self.speed_badge.grid(row=1, column=3, padx=(0, 20), pady=8)
        self._update_speed_badge(saved_conn)

        # Preset buttons
        preset_row = ctk.CTkFrame(dl_frame, fg_color="transparent")
        preset_row.grid(row=2, column=0, columnspan=4, padx=20, pady=(0, 12), sticky="w")

        presets = [
            ("1  — Safe / slow servers",   1,  ("gray60", "gray40")),
            ("4  — Balanced (default)",     4,  None),
            ("8  — Fast",                   8,  ("#1a7a3e", "#1a6b35")),
            ("16 — Maximum speed",         16,  ("#b5500a", "#8a3c06")),
        ]
        for label, val, color in presets:
            kw = {"fg_color": color} if color else {}
            ctk.CTkButton(preset_row, text=label, width=160, height=28,
                          command=lambda v=val: self._set_conn(v), **kw).pack(
                side="left", padx=(0, 8))

        # Note
        ctk.CTkLabel(dl_frame,
                     text="⚠  Very high connection counts (16) may be rate-limited or banned by some servers.",
                     font=ctk.CTkFont(size=10),
                     text_color=("gray50", "gray50")).grid(
            row=3, column=0, columnspan=4, padx=20, pady=(0, 12), sticky="w")

        # ── Save ──────────────────────────────────────────────────────────────
        self.save_btn = ctk.CTkButton(self, text="💾  Save All Settings",
                                       height=40, width=220,
                                       font=ctk.CTkFont(size=14, weight="bold"),
                                       command=self._save)
        self.save_btn.grid(row=5, column=0, padx=20, pady=24)

    # ── Helpers ───────────────────────────────────────────────────────────────

    def _section(self, title, row):
        """Create a labelled section card and return it."""
        card = ctk.CTkFrame(self)
        card.grid(row=row, column=0, padx=20, pady=(0, 10), sticky="ew")
        card.grid_columnconfigure(3, weight=1)
        ctk.CTkLabel(card, text=f"  {title}",
                     font=ctk.CTkFont(size=12, weight="bold"),
                     fg_color=("gray80", "gray25"),
                     corner_radius=6,
                     padx=8, pady=4).grid(row=0, column=0, columnspan=4,
                                           padx=0, pady=0, sticky="ew")
        # inner frame shifted by 1 row
        inner = ctk.CTkFrame(card, fg_color="transparent")
        inner.grid(row=1, column=0, columnspan=4, sticky="ew", padx=0, pady=0)
        inner.grid_columnconfigure(3, weight=1)
        # Re-bind grid methods so callers can use inner directly
        inner._section_card = card
        return inner

    def _on_conn_slider(self, value):
        v = int(round(value))
        self.conn_value_label.configure(text=str(v))
        self._update_speed_badge(v)

    def _set_conn(self, value):
        self.conn_slider.set(value)
        self.conn_value_label.configure(text=str(value))
        self._update_speed_badge(value)

    def _update_speed_badge(self, v):
        v = int(v)
        if v == 1:
            txt, color = "🐢  1× speed", ("gray70", "gray35")
        elif v <= 3:
            txt, color = f"🚗  ~{v}× faster", ("gray70", "gray35")
        elif v <= 7:
            txt, color = f"🚀  ~{v}× faster", ("#1a7a3e", "#1a6b35")
        else:
            txt, color = f"⚡  ~{v}× faster", ("#b5500a", "#8a3c06")
        self.speed_badge.configure(text=txt, fg_color=color)

    def _change_theme(self, mode):
        ctk.set_appearance_mode(mode)
        self.settings.set("theme", mode)

    def _browse_path(self):
        directory = filedialog.askdirectory()
        if directory:
            self.path_entry.delete(0, ctk.END)
            self.path_entry.insert(0, directory)

    def _save(self):
        self.settings.set("download_path",        self.path_entry.get())
        self.settings.set("default_quality",       self.quality_menu.get())
        self.settings.set("concurrent_fragments",  int(round(self.conn_slider.get())))
        self.settings.save()
        self.save_btn.configure(text="✅  Settings Saved!", fg_color="green")
        self.after(2200, lambda: self.save_btn.configure(
            text="💾  Save All Settings",
            fg_color=ctk.ThemeManager.theme["CTkButton"]["fg_color"]))
