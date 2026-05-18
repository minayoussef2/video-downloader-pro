import json
import os

class Settings:
    def __init__(self, config_file="config.json"):
        self.config_file = config_file
        self.defaults = {
            "theme":                "Dark",
            "language":             "English",
            "download_path":        os.path.join(os.path.expanduser("~"), "Downloads", "YTDownloaderPro"),
            "default_quality":      "Best",
            "default_format":       "MP4",
            "auto_subtitles":       False,
            # Multi-connection (IDM-style)
            "concurrent_fragments": 4,     # parallel fragment downloads
        }
        self.config = self.load()

        if not os.path.exists(self.config["download_path"]):
            try:
                os.makedirs(self.config["download_path"])
            except:
                pass

    def load(self):
        if os.path.exists(self.config_file):
            try:
                with open(self.config_file, 'r') as f:
                    data = json.load(f)
                    return {**self.defaults, **data}
            except:
                return self.defaults.copy()
        return self.defaults.copy()

    def save(self):
        with open(self.config_file, 'w') as f:
            json.dump(self.config, f, indent=4)

    def get(self, key):
        return self.config.get(key, self.defaults.get(key))

    def set(self, key, value):
        self.config[key] = value
        self.save()
