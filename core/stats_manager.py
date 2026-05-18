import json
import os
from datetime import datetime

class StatsManager:
    def __init__(self):
        # Use absolute path for data storage to avoid issues with working directory
        self.base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        self.data_dir = os.path.join(self.base_dir, "data")
        
        if not os.path.exists(self.data_dir):
            os.makedirs(self.data_dir)
        
        self.history_file = os.path.join(self.data_dir, "history.json")
        self.stats_file = os.path.join(self.data_dir, "stats.json")
        
        self.history = self._load_json(self.history_file, [])
        self.stats = self._load_json(self.stats_file, {
            "total_videos": 0,
            "total_data_mb": 0,
            "session_videos": 0,
            "session_data_mb": 0,
            "daily_stats": {}
        })

    def _load_json(self, path, default):
        if os.path.exists(path):
            try:
                with open(path, 'r', encoding='utf-8') as f:
                    return json.load(f)
            except Exception as e:
                print(f"Error loading {path}: {e}")
                return default
        return default

    def _save_json(self, path, data):
        try:
            with open(path, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=4)
        except Exception as e:
            print(f"Error saving {path}: {e}")

    def add_history(self, video_info):
        video_info['timestamp'] = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        self.history.insert(0, video_info)
        self._save_json(self.history_file, self.history)
        
        # Update stats
        self.stats["total_videos"] += 1
        self.stats["session_videos"] += 1
        size = video_info.get('size_mb', 0)
        self.stats["total_data_mb"] += size
        self.stats["session_data_mb"] += size
        
        today = datetime.now().strftime("%Y-%m-%d")
        self.stats["daily_stats"][today] = self.stats["daily_stats"].get(today, 0) + 1
        
        self._save_json(self.stats_file, self.stats)

    def get_history(self):
        # Reload to ensure we have latest
        self.history = self._load_json(self.history_file, [])
        return self.history

    def get_stats(self):
        # Reload to ensure we have latest
        self.stats = self._load_json(self.stats_file, self.stats)
        return self.stats

    def clear_history(self):
        self.history = []
        self._save_json(self.history_file, self.history)
