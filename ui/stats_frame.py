import customtkinter as ctk
from core.stats_manager import StatsManager
import matplotlib.pyplot as plt
from matplotlib.backends.backend_tkagg import FigureCanvasTkAgg

class StatsFrame(ctk.CTkFrame):
    def __init__(self, master):
        super().__init__(master, corner_radius=0, fg_color="transparent")
        self.grid_columnconfigure((0, 1), weight=1)
        
        self.stats_manager = StatsManager()
        
        self.label = ctk.CTkLabel(self, text="Download Statistics", font=ctk.CTkFont(size=20, weight="bold"))
        self.label.grid(row=0, column=0, columnspan=2, padx=20, pady=20, sticky="w")
        
        # Stats Cards
        self.total_card = self.create_stat_card("Total Downloads", "0", 1, 0)
        self.data_card = self.create_stat_card("Total Data", "0 MB", 1, 1)
        self.session_card = self.create_stat_card("Session Downloads", "0", 2, 0)
        self.speed_card = self.create_stat_card("Avg Speed", "0 MB/s", 2, 1)
        
        # Chart Area
        self.chart_frame = ctk.CTkFrame(self)
        self.chart_frame.grid(row=3, column=0, columnspan=2, padx=20, pady=20, sticky="nsew")
        self.grid_rowconfigure(3, weight=1)
        
        self.refresh()

    def create_stat_card(self, title, value, row, col):
        card = ctk.CTkFrame(self)
        card.grid(row=row, column=col, padx=10, pady=10, sticky="nsew")
        
        t_label = ctk.CTkLabel(card, text=title, font=ctk.CTkFont(size=12))
        t_label.pack(pady=(10, 0))
        
        v_label = ctk.CTkLabel(card, text=value, font=ctk.CTkFont(size=24, weight="bold"))
        v_label.pack(pady=(0, 10))
        
        return v_label

    def refresh(self):
        stats = self.stats_manager.get_stats()
        self.total_card.configure(text=str(stats["total_videos"]))
        self.data_card.configure(text=f"{stats['total_data_mb']:.1f} MB")
        self.session_card.configure(text=str(stats["session_videos"]))
        
        # Update Chart
        self.update_chart(stats["daily_stats"])

    def update_chart(self, daily_stats):
        # Clear previous chart
        for widget in self.chart_frame.winfo_children():
            widget.destroy()
            
        if not daily_stats:
            ctk.CTkLabel(self.chart_frame, text="No data for charts yet").pack(expand=True)
            return
            
        dates = list(daily_stats.keys())[-7:] # Last 7 days
        counts = [daily_stats[d] for d in dates]
        
        fig, ax = plt.subplots(figsize=(5, 3), dpi=100)
        fig.patch.set_facecolor('#2b2b2b') # Match dark theme
        ax.set_facecolor('#2b2b2b')
        
        ax.plot(dates, counts, marker='o', color='#1f538d', linewidth=2)
        ax.set_title("Download Trends (Daily)", color='white')
        ax.tick_params(axis='x', colors='white', labelsize=8)
        ax.tick_params(axis='y', colors='white')
        for spine in ax.spines.values():
            spine.set_color('white')
            
        canvas = FigureCanvasTkAgg(fig, master=self.chart_frame)
        canvas.draw()
        canvas.get_tk_widget().pack(fill="both", expand=True, padx=10, pady=10)
        plt.close(fig)
