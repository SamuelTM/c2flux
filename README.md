![Windows](https://img.shields.io/badge/Windows-10%2B-0078D6?logo=windows&logoColor=white) ![macOS](https://img.shields.io/badge/macOS-12%2B-000000?logo=apple&logoColor=white) ![Linux](https://img.shields.io/badge/Linux-AppImage-FCC624?logo=linux&logoColor=black) ![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white) ![C%23](https://img.shields.io/badge/C%23-239120?logo=csharp&logoColor=white) ![Platform](https://img.shields.io/badge/Platform-x64%20%7C%20ARM64-lightgrey) ![License](https://img.shields.io/github/license/SamuelTM/c2flux) ![Release](https://img.shields.io/github/v/release/SamuelTM/c2flux?include_prereleases)

# c² flux – Tree Scanner

**First, a big thank you:**

A huge thank you to everyone behind these projects — you’re amazing! Thanks for making such great work available to the community. ❤️

- [itsWindows11.NtfsReader](https://github.com/itsWindows11/NtfsReader)
  (This project includes a modified version of itsWindows11/NtfsReader)
- [Avalonia](https://github.com/AvaloniaUI/Avalonia), the cross-platform UI
- [AntdUI](https://github.com/AntdUI/AntdUI), whose look the interface keeps

---

**Scan your drives, get an overview of used disk space, find everything. Fast.**

[![Download c2flux](https://img.shields.io/github/v/release/SamuelTM/c2flux?include_prereleases&label=%E2%AC%87%EF%B8%8F%20Download&style=for-the-badge&color=brightgreen)](https://github.com/SamuelTM/c2flux/releases)

[![Changelog](https://img.shields.io/badge/%F0%9F%93%9D%20Changelog-View-blue?style=for-the-badge)](CHANGELOG.md)

<br>
<br>
**Troubleshooting**

See the [Troubleshooting Guide](docs/Troubleshooting.md).

<br>
<br>

✨ Bar Chart

<img width="1267" height="751" alt="grafik" src="https://github.com/user-attachments/assets/08a7f8db-efa2-4adc-a022-bf3d24faa8f9" />

<br>
<br>
<br>

✨ Treemap

<img width="1267" height="751" alt="grafik" src="https://github.com/user-attachments/assets/02a4e251-8e44-45d7-afe7-78b588d54823" />

<br>
<br>
<br>

✨ Sunburst

<img width="1403" height="752" alt="grafik" src="https://github.com/user-attachments/assets/be2ad86f-e7df-48bc-aaf3-04a08d1533eb" />

<br>
<br>
<br>

✨ Analysis

<img width="1403" height="752" alt="grafik" src="https://github.com/user-attachments/assets/036d3c31-742a-4268-a011-c85d93d3da66" />

<br>
<br>
<br>

✨ Scan History — see why your free drive space gets smaller and smaller

<img width="1481" height="840" alt="grafik" src="https://github.com/user-attachments/assets/a896b664-70d4-40bc-bc54-a99b1382876a" />

<br>
<br>
<br>

🚀 Find files at lightspeed (example: more than 700k files)

Search from everywhere, directly!

<img width="500" height="193" alt="grafik" src="https://github.com/user-attachments/assets/1606aee2-6b9a-4965-be65-ef40e1551530" />

<br>
<br>

<img width="453" height="30" alt="grafik" src="https://github.com/user-attachments/assets/7a2fe76e-b99e-43f7-962d-78eb7d964108" />

<br>

<img width="648" height="519" alt="grafik" src="https://github.com/user-attachments/assets/4585438f-301b-4a32-90ac-1c1bbd744f96" />

<br>

## Highlights

- ⚡ **Fast drive and folder scanning with strong caching mechanisms**
- 🚀 **Regular and MFT-based scanning**
  - **MFT scan:** Fastest scan mode when running as Administrator
  - **Regular scan:** Fast first scan, super-fast subsequent scans through caching
- 🔎 **Super-fast global search**
  - 🔴 Find files and folders instantly
  - 🔴 Integrated context menu actions
- Multiple visualization and display styles
- Export functions for scan and analysis data
- Scan history and storage timeline
- Optional SQLite database for persistent scan storage
  - Store previous scans
  - Compare scans
  - Inspect detailed differences between scan states
- Windows Explorer integration
- Light and dark themes
- Multilingual interface with 30 languages

<br>

## Scan Modes

### 🚀 MFT Scan

Fastest scan mode.

> **Requires administrator privileges.**

### ⚡ Regular Scan

- Fast initial scan
- Super-fast subsequent scans through caching

<br>

## Search

### 🔎 Super-fast global search

- 🔴 Find files and folders instantly
- 🔴 Integrated context menu actions

<br>

## Visualization & Analysis

- Multiple visualization and display styles
- Scan and storage analysis
- Export functions for scan and analysis data

<br>

## Documentation

📖 **Documentation:** See the [GitHub Wiki](https://github.com/SamuelTM/c2flux/wiki) for instructions and usage guides.

<br>

## Requirements

- Windows 10 or newer, macOS 12 or newer, or a 64-bit Linux desktop
- x64 or ARM64
- Nothing else: the packages include the .NET runtime

> On Windows, the fast NTFS MFT scan requires administrator privileges.

<br>

## Download and installation

Each release lists one file per system:

| System | File |
|---|---|
| Windows | `c2flux-v<version>-windows-x64.zip` or `-windows-arm64.zip` |
| macOS (Apple silicon / Intel) | `c2flux-v<version>-macos-arm64.dmg` / `-macos-x64.dmg` |
| Linux | `c2flux-v<version>-linux-x86_64.AppImage` / `-linux-aarch64.AppImage` |

### Windows

Unzip the folder anywhere and start `c2flux.exe`. Run it as administrator to use the fast NTFS MFT scan.

### macOS

1. Open the `.dmg` and drag **c2flux** to **Applications**.
2. c² flux is not notarized by Apple, so the first start is blocked. Open it once, then go to **System Settings → Privacy & Security** and click **Open Anyway**. On macOS 14 and older you can also Control-click the app and choose **Open**.
3. To scan your home folder completely, give c² flux **Full Disk Access** in **System Settings → Privacy & Security → Full Disk Access**. Without it, macOS hides folders such as Mail, Messages and Safari from every app; c² flux shows a notice with a button to these settings.

### Linux

Make the AppImage executable and start it:

```sh
chmod +x c2flux-v<version>-linux-x86_64.AppImage
./c2flux-v<version>-linux-x86_64.AppImage
```

If it reports that FUSE is missing, install `libfuse2` (`libfuse2t64` on Ubuntu 24.04 and newer), or start it with `--appimage-extract-and-run`. c² flux runs without root; folders it may not read are skipped and listed in the alert log. Chinese, Japanese and Korean texts use the system's fonts: install `fonts-noto-cjk` if they show as boxes.

<br>

## Support

❤️ If you’d like to support my work with a small contribution, I’d really appreciate it. ❤️

<br>

<a href="https://ko-fi.com/uncleriot"><img src="https://github.com/user-attachments/assets/57680fed-c0b7-44fa-ac74-076903bd7eec" alt="Support me on Ko-fi" width="174"></a>

<br>

## Windows SmartScreen warning

<img width="438" height="173" alt="grafik" src="https://github.com/user-attachments/assets/429951bb-ab24-49a3-89a8-ecb4b0a7fec1" />

Windows SmartScreen may block c² flux because the application is not yet widely recognized.

This warning does not automatically mean that the application is malicious.

Only continue if you downloaded c² flux from the official GitHub repository.

### How to start c² flux

1. Click **More info**.
2. Click **Run anyway**.
3. Confirm the Windows security prompt, if one appears.
4. c² flux will start.

> **Important:** Do not run the application if you downloaded it from an unknown or untrusted source.

<br>

## Security and Packaging

> **Security and packaging note:** VirusTotal and other automated analysis platforms may report heuristic findings for c² flux. A short explanation of these findings and the current evaluation of single-file versus multi-file releases is available here: https://github.com/SamuelTM/c2flux/wiki/Security-scanner-findings-and-release-packaging

<br>

## License

Licensed under the [GNU General Public License v3.0](LICENSE).
