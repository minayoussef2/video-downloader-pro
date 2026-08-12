# 📥 Video Downloader Pro v1.0.0

> A high-performance, modern video downloader & stream extractor for Windows — powered by `.NET 8 WPF` and `yt-dlp`, paired with a Manifest V3 browser extension for deep network stream sniffing and single-click browser interception.

---

## 🌟 Key Features

- **⚡ NeatDownloadManager-Style Browser Interception:** Automatically intercepts browser file downloads for videos, audio, and subtitle files (`.mp4`, `.mkv`, `.mp3`, `.m3u8`, `.mpd`, etc.) and routes them instantly to the desktop companion app.
- **📡 Universal HLS & DASH (MPD) Stream Extraction:** Full support for live streams, segment-based playlists (`.m3u8`), and DASH manifests (`.mpd`). Unencrypted DASH streams can be queued, analyzed, and downloaded seamlessly.
- **🔍 Multi-Level Media Sniffing:**
  - **Network Request Sniffer:** Sniffs video MIME types, Content-Disposition headers, and media fragments across all web traffic.
  - **DOM & Element Analyzer:** Scans `<video>` tags and nested `<iframe>` parameters (e.g. `?url=https://youtube.com/watch...`).
  - **Embed Recognition:** Native metadata resolution for YouTube, Vimeo, Dailymotion, Rumble, Bilibili, and more.
- **🎯 Flexible Quality Controls:**
  - Choose between 11 quality presets (`Best`, `8K 4320p`, `4K 2160p`, `2K 1440p`, `1080p`, `720p`, `480p`, `360p`, `240p`, `144p`, `Audio Only MP3`).
  - Configurable **Preferred Default Quality** in the browser extension that automatically pre-selects your choice for every detected stream.
- **🛡️ Bypass & Exclusion Rules:**
  - Hold the **Control (Ctrl)** key while clicking a download link to force the browser's native downloader.
  - Add domain exclusions (`chrome.storage` synced) to disable interception on specific websites.
- **🚀 Single-Instance Activation & Tray Integration:**
  - Double-launching `VideoDownloaderPro.exe` automatically restores and focuses the running instance window via cross-process named `EventWaitHandle` signaling (no duplicate processes or error dialogs).
  - Single left-click on the system tray icon brings up the main window instantly.
- **⚠️ Interactive Error Details Inspector:**
  - Failed downloads display a small `⚠️ Error Details` button that opens a dedicated log viewer with a `📋 Copy Error Details` button for troubleshooting.
- **🌐 Browser Installation Wizard:**
  - Built-in step-by-step setup guide for **Chrome**, **Edge**, **Brave**, and **Opera**, with an "Open Extension Folder" shortcut button.

---

## 💻 System & Hardware Requirements

### 🖥️ Software & Operating System Requirements

| Requirement | Minimum | Recommended |
| :--- | :--- | :--- |
| **Operating System** | Windows 10 (64-bit, Build 1809 or higher) | Windows 11 (64-bit, latest release) |
| **Framework Runtime** | None *(Self-contained build includes .NET 8)* | [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) *(Only if running non-self-contained)* |
| **Supported Browsers** | Google Chrome (v100+), MS Edge (v100+), Brave, Opera | Latest Chromium-based browser |
| **Core Dependencies** | `yt-dlp.exe` *(Bundled or on PATH)* | `yt-dlp.exe` + `ffmpeg.exe` *(For high-res audio/video merging)* |

### ⚙️ Hardware Requirements

| Resource | Minimum Requirement | Recommended |
| :--- | :--- | :--- |
| **Processor (CPU)** | Intel Core i3 / AMD Ryzen 3 (Dual-Core 2.0 GHz) | Intel Core i5 / AMD Ryzen 5 or higher (Quad-Core+) |
| **Memory (RAM)** | 4 GB RAM | 8 GB RAM or higher |
| **Storage (Disk Space)** | 250 MB free space for app & dependencies | SSD with 10 GB+ free space for video buffer & storage |
| **Network** | 5 Mbps Internet Connection | Broadband / Fiber (100 Mbps+) |

---

## 🛠️ Architecture & How It Works

```
┌────────────────────────────────────────────────────────┐
│               Chromium Web Browser                     │
│  (Chrome / Edge / Brave / Opera with Extension v1.0)   │
└──────────────────────────┬─────────────────────────────┘
                           │
            Local HTTP API │ (Port Probe 18888 - 18892)
            JSON Requests  │ CORS & Private Network Access
                           ▼
┌────────────────────────────────────────────────────────┐
│            Video Downloader Pro Desktop App            │
│         WPF (.NET 8) · Single-Instance Process         │
└──────────────────────────┬─────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│                 Download Engine                        │
│          yt-dlp.exe  +  ffmpeg.exe pipeline            │
└────────────────────────────────────────────────────────┘
```

1. **Local Server Bind:** On boot, the desktop app spins up an internal `HttpListener` on port `18888` (with fallback retries across `18889–18892`).
2. **Browser Extension Probe:** The Manifest V3 extension probes `http://127.0.0.1:18888/status` to discover the active app port and caches it asynchronously in `chrome.storage.local`.
3. **Stream & Download Interception:**
   - Network requests matching video/audio MIME types or `.m3u8` / `.mpd` extensions are cataloged in the extension popup.
   - Standard browser downloads for media files are intercepted, canceled in Chrome, and posted to `/download` or `/queue` endpoints in the WPF app.
4. **Execution:** The desktop app invokes `yt-dlp.exe` with real-time stdout/stderr progress parsing (percentages, speed, ETA, HLS fragment counts).

---

## 🚀 Installation & Setup

### 1. Download & Extract Desktop Companion App
1. Download the latest release package from the Releases tab or compile from source.
2. Extract `VideoDownloaderPro_v3.2.0.zip` to a folder of your choice (e.g. `C:\Program Files\VideoDownloaderPro`).

### 2. External Binaries (yt-dlp & FFmpeg)
Ensure the following files are present in the app directory (or available in your system `PATH`):
- **[`yt-dlp.exe`](https://github.com/yt-dlp/yt-dlp/releases):** Main extraction and download engine.
- **[`ffmpeg.exe`](https://www.gyan.dev/ffmpeg/builds/):** Required for merging separate video + audio streams into high-definition `.mp4` / `.mkv` files.

### 3. Install Browser Extension (Manifest V3)

The extension works on **Google Chrome**, **Microsoft Edge**, **Brave Browser**, and **Opera**.

#### Step-by-Step Instructions:

1. **Open Extensions Page in your browser:**
   - **Chrome:** `chrome://extensions`
   - **Edge:** `edge://extensions`
   - **Brave:** `brave://extensions`
   - **Opera:** `opera://extensions`
2. **Enable Developer Mode:** Turn on the **Developer mode** toggle in the top-right corner.
3. **Load Unpacked Extension:**
   - Click the **Load unpacked** button.
   - Browse to the app directory and select the **`extension`** folder.
4. **Pin Extension:** Click the puzzle icon 🧩 in your browser toolbar and pin **Video Downloader Pro**.

---

## 📖 How to Use

### 📥 Downloading Videos from Web Pages
1. Open any web page containing a video (YouTube, TikTok, Instagram, Twitter/X, lectures, embedded players).
2. Click the **Video Downloader Pro extension icon** in your browser.
3. The popup automatically displays detected streams, thumbnails, formats, and quality options.
4. Click **Download** to start downloading immediately, or **+ Queue** to add it to the app queue.

### 📡 Downloading HLS / MPD Streaming Links
1. Open the desktop app and navigate to the **📡 HLS / MPD Streams** tab.
2. Paste any `.m3u8` or `.mpd` stream URL into the input field.
3. Optionally specify a **Referer URL** if required by the CDN server.
4. Click **➕ Add to Queue** or **⬇ Download All**.

### ⌨️ Interception Shortcuts & Rules
- **Bypass Interception (Ctrl Key):** Hold down `Ctrl` while clicking any download link on a webpage to force the browser to handle the file natively instead of sending it to Video Downloader Pro.
- **Site Exclusions:** Click **Preferences** in the extension popup and uncheck "Capture on current website" to add the domain to your exclusions list.

---

## 📂 Project Directory Structure

```
video-downloader-pro/
├── extension/                       # Chrome / Chromium Extension (Manifest V3)
│   ├── manifest.json                # Extension Manifest V3 configuration
│   ├── background.js                # Service Worker: Network sniffer, storage, app IPC
│   ├── content.js                   # Content script: DOM video scanner & iframe detector
│   ├── popup.html                   # Extension Popup UI
│   ├── popup.js                     # Extension Popup logic & quality preference sync
│   └── icons/                       # Extension icon assets (16x16, 48x48, 128x128)

├── src/                             # Desktop Application Source Code (.NET 8 WPF)
    ├── VideoDownloaderPro.csproj    # WPF C# Project File
    ├── App.xaml / App.xaml.cs       # Entry point, Single-instance Mutex & ShowEvent listener
    ├── MainWindow.xaml / .cs        # Main Window UI, System Tray icon, Tab views
    │
    ├── Core/                        # Core Downloader Architecture
    │   ├── QueueManager.cs          # Concurrency semaphore, download lifecycle management
    │   └── YtDlpWrapper.cs          # Async Process wrapper around yt-dlp.exe & progress regex
    │
    ├── Services/                    # Services Layer
    │   ├── ExtensionServer.cs       # Local HttpListener server (Port 18888-18892 retry loop)
    │   ├── ExtensionInstaller.cs    # Registry auto-install helper
    │   ├── SettingsService.cs       # JSON settings persistence
    │   └── StatsService.cs          # Download history & bandwidth statistics manager
    │
    ├── ViewModels/                  # MVVM ViewModels
    │   ├── MainViewModel.cs         # Master ViewModel, Commands, Navigation, State
    │   └── ViewModelBase.cs       # INotifyPropertyChanged base implementation
    │
    ├── Models/                      # Data Models
    │   ├── DownloadItem.cs          # Item model (Url, Progress, Quality, ErrorDetails, Status)
    │   ├── AppSettings.cs           # User configuration model
    │   └── AppStats.cs              # History and download metrics model
    │
    └── Views/                       # Modal Dialogs & Windows
        ├── ExtensionGuideWindow.xaml# Step-by-step browser setup guide window
        └── ErrorDetailsWindow.xaml  # Full error log inspector & copy dialog

```

---

## 🛠️ Building from Source

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed on your system.

### Build Commands

```powershell
# 1. Clone the repository
git clone https://github.com/minayoussef2/video-downloader-pro.git
cd video-downloader-pro

# 2. Compile WPF application
cd src
dotnet build --configuration Release

# 3. Publish Self-Contained Executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

The published build will be generated in `src/bin/Release/net8.0-windows/win-x64/publish/`.

---

## ❓ Troubleshooting & FAQ

### 1. The Extension shows "App Offline"
- Ensure `VideoDownloaderPro.exe` is running on your desktop.
- Verify that Windows Firewall is not blocking local port `18888`.
- Open your browser and test `http://127.0.0.1:18888/status`. It should return `{"status":"running"}`.

### 2. Video downloads fail with an error
- Click the **`⚠️ Error Details`** button beside the failed item in the app.
- Check the log for details. Common causes include:
  - Missing `ffmpeg.exe` (required for high-resolution video streams).
  - Outdated `yt-dlp.exe` (run `yt-dlp -U` in command prompt to update).
  - Encrypted DRM stream (e.g. Netflix, Widevine-protected content cannot be downloaded).

### 3. Multiple app windows open on startup
- Video Downloader Pro uses single-instance enforcement. Launching a second shortcut signals the existing process to bring its window forward.

---

## 📝 License

This project is licensed under the **MIT License**. Feel free to use, modify, and distribute it.
