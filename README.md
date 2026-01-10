# Discord Quest Manager

**Inspired by [discord-quest-claimer](https://gist.github.com/aamiaa/204cd9d42013ded9faf646fae7f89fbb)**

A robust, user-friendly desktop application for managing and automating Discord Quests. Built with .NET 8 and WebView2.

## ✨ Features

-   **Automation**: Automatically handles "Play Time" and "Stream Time" tracking for quests.
-   **Multi-Account**: Support for adding and managing multiple Discord tokens securely.
-   **Secure Claiming**: Integrated manual claim window (WebView2) to safely handle rewards without automated Captcha failures.
-   **Orb Balance**: Real-time tracking of User Orb balance.
-   **Notifications**: Native Windows Toast notifications for quest completion.

## 🖥️ Interface Overview

The application is divided into several tabs for easy management:

### 1. Available Quests
- Lists all quests available to your account.
- **Accept**: One-click to accept quests.
- **Filter**: Shows reward type and time requirements.

### 2. Accepted Quests
- Shows currently active quests.
- **Progress Tracking**: Visual progress bars for streaming/playing tasks.
- **Start Selected**: Select quests and click "Start" to begin automation.
- **Start All**: Automatically start all accepted quests.

### 3. Completed Quests (Claiming)
- Lists quests that have met their requirements.
- **Claim in Discord**: Opens a secure, authenticated browser window where you can manually click "Claim Reward".
    - *Note: This manual step is required because Discord's Captcha system is extremely difficult to bypass automatically.*

### 4. History
- A log of all past quests you have interacted with.

## � Installation

1.  Download the latest release ZIP.
2.  Extract to a folder.
3.  Run `DiscordQuestGUI.exe`.
    - *Requires .NET 10 Desktop Runtime.*

## 🤝 Credits

-   Original inspiration and logic: [aamiaa's gist](https://gist.github.com/aamiaa/204cd9d42013ded9faf646fae7f89fbb)
-   Built with ♥ by the community.

## ⚠️ Disclaimer

This tool is for educational purposes only. Use at your own risk and responsibility. Always adhere to Discord's Terms of Service.