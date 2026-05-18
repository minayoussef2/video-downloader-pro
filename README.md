# 📥 Video Downloader Pro v3.0.0

A powerful, modern, and high-speed video downloader for Windows, built with WPF and powered by `yt-dlp`. Features a companion browser extension for advanced video sniffing.

## ✨ Features
- **Modern UI:** Premium dark-mode design with real-time feedback.
- **Smart Sniffer:** Browser extension detects HLS, DASH, and direct video streams on any site (TikTok, Instagram, etc.).
- **Progress Badges:** specialized tracking for HLS fragments and download speeds.
- **Queue Management:** Multi-download support with estimated size aggregation.
- **System Tray:** Minimize to tray with a custom icon and quick-access menu.

## 🔄 The Transformation: Legacy vs. v3.0.0

| Feature | Original (Legacy) | New v3.0.0 Overhaul |
| :--- | :--- | :--- |
| **User Interface** | Basic, functional design. | **Premium Dark-Mode** with glassmorphism and smooth animations. |
| **Video Detection** | Simple URL-based scanning. | **Persistent Smart Sniffer** for TikTok, Instagram, and more. |
| **Download Engine** | Basic yt-dlp calls. | **Advanced Engine** with fragment-level tracking and auto-merging. |
| **Progress Feedback** | Simple percentage. | **Rich Feedback:** HLS fragment badges, real-time speed, and ETA. |
| **Browser Sync** | Unstable connection. | **Secure Port 18888 Sync** with Private Network Access support. |
| **Metadata** | Often labeled as "Unknown". | **Deep Metadata Pipeline:** Pulls video titles from the browser. |
| **Stability** | Crashes without logs. | **Global Error Handling** with automatic `crash_log.txt` logs. |

## 🚀 Installation

### 1. Download the App
Download the latest release from the [Releases](https://github.com/minayoussef2/video-downloader-pro/releases) page.

### 2. External Dependencies (Required)
For the app to work correctly, you must place the following files in the application directory:
- **[yt-dlp.exe](https://github.com/yt-dlp/yt-dlp/releases):** Core download engine.
- **[ffmpeg.exe](https://www.gyan.dev/ffmpeg/builds/):** Used for merging high-quality video and audio.

### 3. Browser Extension (Manual Installation)
If the app doesn't automatically install the extension for you, follow these steps:
1. Open your browser (Chrome, Edge, or Brave).
2. Type **`chrome://extensions`** in the address bar and press Enter.
3. Enable **"Developer mode"** (toggle in the top-right corner).
4. Click the **"Load unpacked"** button.
5. Select the **`extension`** folder from the files you downloaded.
6. **Note:** After installing, refresh your website tabs to enable video detection.

<<<<<<< HEAD
## 🛠️ Build from Source
Requirements: `.NET 8.0 SDK`.

```powershell
# Clone the repository
git clone https://github.com/minayoussef2/video-downloader-pro.git

# Build and Publish
cd src
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```
=======
>>>>>>> b097195a9d5961ffd37d446c3d7d8f848ac82714

## 📝 License
This project is licensed under the MIT License.
