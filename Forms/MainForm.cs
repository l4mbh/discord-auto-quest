using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using DiscordQuestGUI.Models;
using DiscordQuestGUI.Services;

namespace DiscordQuestGUI.Forms;

public partial class MainForm : Form
{
    private WebView2 _webView = null!;
    private ConfigService _configService = null!;
    private AccountService _accountService = null!;
    private DiscordAuthService _authService = null!;
    private DiscordApiService? _apiService;
    private QuestWorkerService? _questWorkerService;
    private NotificationService _notificationService = null!;
    private string? _currentToken;
    private List<Quest> _acceptedQuests = new();
    private List<Quest> _availableQuests = new();
    private List<Quest> _completedQuests = new();
    private Queue<string> _questQueue = new();
    private bool _isProcessingQueue = false;
    private WebView2? _claimWebView;
    
    // Tray icon fields
    private NotifyIcon? _trayIcon;
    private string _currentQuestName = "";
    private int _currentQuestProgress = 0;
    private string _currentUsername = "";

    public MainForm()
    {
        this.WindowState = FormWindowState.Maximized;
        InitializeComponent();

        // Register global exception handlers
        System.Windows.Forms.Application.ThreadException += Application_ThreadException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        _configService = new ConfigService();
        _accountService = new AccountService(_configService);
        _authService = new DiscordAuthService();
        _notificationService = new NotificationService();
        
        // Initialize tray icon
        InitializeTrayIcon();
        
        // Handle FormClosing event
        this.FormClosing += MainForm_FormClosing;
    }

    private void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
    {
        // NEVER call WebView2/UI here - it might be the crash source!
        try
        {
            LogError($"[ThreadException] {e.Exception}");
            // Only show MessageBox as last resort
            MessageBox.Show($"Unexpected error: {e.Exception.Message}\n\nSee error.log for details.",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            // Can't do anything
        }
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // NEVER call WebView2/UI here - it might be the crash source!
        try
        {
            var ex = e.ExceptionObject as Exception;
            LogError($"[UnhandledException] {ex?.ToString() ?? "Unknown"}");
        }
        catch
        {
            // Can't do anything - app is dying
        }
    }

    private void InitializeComponent()
    {
        this.Text = "Discord Quest Auto-Complete";
        this.Size = new Size(900, 700);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MinimumSize = new Size(800, 600);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill
        };

        this.Controls.Add(_webView);
        this.Load += MainForm_Load;
    }

    private void InitializeServices()
    {
        _configService = new ConfigService();
        _accountService = new AccountService(_configService);
        _authService = new DiscordAuthService();
        _notificationService = new NotificationService();
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            LogError("App starting...");
            
            // Initialize WebView2 FIRST - wait until fully ready
            await InitializeWebView();
            
            LogError("WebView2 initialized");

            // NOW we can send loading states
            LogError("Sending loading state...");
            SendLoadingState(true, "Checking authentication...");
            LogError("Loading state sent");
            
            // Check for existing account and validate
            LogError("Getting current account credentials...");
            var (token, cookies) = _accountService.GetCurrentAccountCredentials();
            LogError($"Token retrieved: {(string.IsNullOrEmpty(token) ? "empty" : "exists")}");

            if (!string.IsNullOrEmpty(token))
            {
                LogError("Validating token...");
                var isValid = await ValidateTokenAsync(token);
                LogError($"Token valid: {isValid}");

                if (isValid)
                {
                    // Auto-login with existing valid token
                    LogError("Initializing API services...");
                    await InitializeApiServices(token, cookies);
                    LogError("API services initialized");
                    return;
                }
            }
            
            // Show login screen in UI
            LogError("Showing login screen...");
            SendLoadingState(false, "");
            SendMessageToJS("showLoginScreen", new { });
            LogError("Login screen shown");
        }
        catch (Exception ex)
        {
            LogError($"MainForm_Load error: {ex}");
            SendLoadingState(false, "");
            SendToast($"Initialization error: {ex.Message}", "error");
            
            // Don't exit, just show login screen
            try
            {
                SendMessageToJS("showLoginScreen", new { });
            }
            catch (Exception ex2)
            {
                LogError($"Fatal - cannot show login: {ex2}");
                // Fatal error - can't even show UI
                MessageBox.Show($"Fatal error:\n{ex.Message}\n\nCannot start application.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                System.Windows.Forms.Application.Exit();
            }
        }
    }

    private void LogError(string message)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiscordQuestGUI",
                "error.log"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch
        {
            // Can't log
        }
    }

    private async Task InitializeApiServices(string token, string? cookies = null)
    {
        try
        {
            LogError("InitializeApiServices started");
            
            // Ensure we're on UI thread for WebView operations
            if (InvokeRequired)
            {
                LogError("Switching to UI thread...");
                Invoke(() => SendLoadingState(true, "Initializing services..."));
            }
            else
            {
                SendLoadingState(true, "Initializing services...");
            }
            
            LogError("Creating DiscordApiService...");
            _currentToken = token;
            _apiService = new DiscordApiService(token, cookies);
            LogError("DiscordApiService created");
            
            LogError("Creating QuestWorkerService...");
            _questWorkerService = new QuestWorkerService(_apiService);
            _questWorkerService.ProgressChanged += OnQuestProgressChanged;
            LogError("QuestWorkerService created");

            if (InvokeRequired)
            {
                Invoke(() => SendLoadingState(true, "Loading user profile..."));
            }
            else
            {
                SendLoadingState(true, "Loading user profile...");
            }
            
            // Get user info
            LogError("Getting user info...");
            var userInfo = await GetUserInfoAsync();
            
            // Get balance
            LogError("Getting user balance...");
            long balance = await _apiService.GetVirtualCurrencyBalanceAsync();
            LogError($"User info retrieved: {userInfo.username}, Balance: {balance}");

            // Save account info
            var account = new UserAccount
            {
                Id = userInfo.id,
                Username = userInfo.username,
                Discriminator = userInfo.discriminator,
                Avatar = userInfo.avatar,
                EncryptedToken = EncryptToken(token),
                EncryptedCookies = string.IsNullOrEmpty(cookies) ? null : EncryptToken(cookies),
                IsActive = true
            };
            _accountService.AddAccount(account);

            // Send to UI - ensure on UI thread
            LogError("Sending loginSuccess to UI...");
            
            // Update current username for tray icon context menu
            if (string.IsNullOrEmpty(userInfo.discriminator) || userInfo.discriminator == "0")
            {
                _currentUsername = userInfo.username;
            }
            else
            {
                _currentUsername = $"{userInfo.username}#{userInfo.discriminator}";
            }
            UpdateContextMenu();
            
            // Clear all quests only when switching account (not on first login)
            // Check if there was a previous account by looking at service state
            var hadPreviousAccount = !string.IsNullOrEmpty(_currentUsername);
            if (hadPreviousAccount)
            {
                LogError("Switching account - clearing quests");
                SendMessageToJS("clearAllQuests", new { });
            }

            Action sendSuccess = () =>
            {
                SendLoadingState(false, "");
                SendMessageToJS("loginSuccess", new
                {
                    username = userInfo.username,
                    discriminator = userInfo.discriminator,
                    avatar = userInfo.avatar,
                    id = userInfo.id,
                    balance = balance,
                    accounts = GetAllAccountsForUI()
                });
            };

            if (InvokeRequired)
            {
                Invoke(sendSuccess);
            }
            else
            {
                sendSuccess();
            }
            
            LogError("loginSuccess sent");
        }
        catch (Exception ex)
        {
            LogError($"InitializeApiServices error: {ex}");
            LogError($"Stack trace: {ex.StackTrace}");
            
            Action showError = () =>
            {
                SendLoadingState(false, "");
                SendToast($"Failed to initialize: {ex.Message}", "error");
                SendMessageToJS("showLoginScreen", new { });
            };
            
            if (InvokeRequired)
            {
                Invoke(showError);
            }
            else
            {
                showError();
            }
        }
    }

    private async Task<(string username, string discriminator, string avatar, string id)> GetUserInfoAsync()
    {
        try
        {
            LogError("GetUserInfoAsync: Calling API...");
            
            if (_apiService == null)
            {
                LogError("GetUserInfoAsync: _apiService is null");
                return ("User", "0000", "", "");
            }

            LogError("GetUserInfoAsync: About to call ValidateTokenAndGetUserAsync");
            var response = await _apiService.ValidateTokenAndGetUserAsync();
            LogError($"GetUserInfoAsync: API returned, username={response.username}");
            
            return response;
        }
        catch (Exception ex)
        {
            LogError($"GetUserInfoAsync error: {ex}");
            LogError($"GetUserInfoAsync stack trace: {ex.StackTrace}");
            SendToast($"Could not load user info: {ex.Message}", "warning");
            return ("User", "0000", "", "");
        }
    }

    private async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            var apiService = new DiscordApiService(token);
            return await apiService.ValidateTokenAsync();
        }
        catch
        {
            return false;
        }
    }

    private async Task InitializeWebView()
    {
        try
        {
            LogError("InitializeWebView started");
            
            // CRITICAL: Use separate UserDataFolder to avoid conflict with AuthService
            var mainWebViewDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiscordQuestGUI",
                "MainWebView_Data"
            );
            Directory.CreateDirectory(mainWebViewDataFolder);
            LogError($"MainForm WebView UserDataFolder: {mainWebViewDataFolder}");

            var env = await CoreWebView2Environment.CreateAsync(null, mainWebViewDataFolder);
            await _webView.EnsureCoreWebView2Async(env);
            
            LogError("MainForm WebView2 initialized successfully");

            // Enable DevTools for debugging
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            // Listen for messages from JavaScript
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // Add navigation completed handler
            _webView.CoreWebView2.NavigationCompleted += (s, e) =>
            {
                if (!e.IsSuccess)
                {
                    LogError($"Navigation failed: {e.WebErrorStatus}");
                }
            };

            // Navigate directly to index.html using file path
            var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "index.html");
            LogError($"Navigating to: {htmlPath}");
            
            // Check if file exists
            if (!File.Exists(htmlPath))
            {
                LogError($"ERROR: index.html not found at: {htmlPath}");
                throw new FileNotFoundException($"index.html not found at: {htmlPath}");
            }
            
            _webView.CoreWebView2.Navigate($"file:///{htmlPath.Replace("\\", "/")}");
        }
        catch (Exception ex)
        {
            LogError($"InitializeWebView critical error: {ex}");
            // Don't crash - just show message without using WebView
            MessageBox.Show(
                $"Failed to initialize WebView2:\n{ex.Message}\n\nPlease install WebView2 Runtime from:\nhttps://go.microsoft.com/fwlink/p/?LinkId=2124703",
                "Initialization Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            throw; // Re-throw to stop app
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var json = e.WebMessageAsJson;
        var message = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

        if (message == null) return;

        var action = message["action"].GetString();

        switch (action)
        {
            case "login":
                await HandleLoginAsync();
                break;

            case "loadQuests":
                await LoadQuestsAsync();
                break;

            case "loadHistoryQuests":
                if (message.ContainsKey("page") && message.ContainsKey("pageSize"))
                {
                    var page = message["page"].GetInt32();
                    var pageSize = message["pageSize"].GetInt32();
                    await LoadHistoryQuestsAsync(page, pageSize);
                }
                else
                {
                    await LoadHistoryQuestsAsync();
                }
                break;

            case "startQuests":
                if (message.ContainsKey("questIds"))
                {
                    var questIds = message["questIds"].EnumerateArray()
                        .Select(x => x.GetString())
                        .Where(x => !string.IsNullOrEmpty(x))
                        .Cast<string>()
                        .ToList();

                    if (questIds.Any())
                    {
                        StartQuests(questIds);
                    }
                }
                break;

            case "getSettings":
                SendSettings();
                break;

            case "saveSettings":
                if (message.ContainsKey("settings"))
                {
                    // Load current settings first
                    var currentSettings = _configService.LoadSettings();
                    
                    // Parse the settings object from JS
                    var settingsElement = message["settings"];
                    
                    // Update only the fields sent from JS
                    if (settingsElement.TryGetProperty("enableNotifications", out var enableNotif))
                    {
                        currentSettings.EnableNotifications = enableNotif.GetBoolean();
                    }
                    
                    if (settingsElement.TryGetProperty("minimizeToTray", out var minimizeToTray))
                    {
                        currentSettings.MinimizeToTray = minimizeToTray.GetBoolean();
                    }
                    
                    // Save merged settings
                    _configService.SaveSettings(currentSettings);
                }
                break;

            case "logout":
                Logout();
                break;

            case "switchAccount":
                if (message.ContainsKey("accountId"))
                {
                    var accountId = message["accountId"].GetString();
                    if (!string.IsNullOrEmpty(accountId))
                    {
                        await HandleSwitchAccountAsync(accountId);
                    }
                }
                break;

            case "addAccount":
                HandleAddAccount();
                break;

            case "removeAccount":
                if (message.ContainsKey("accountId"))
                {
                    var accountId = message["accountId"].GetString();
                    if (!string.IsNullOrEmpty(accountId))
                    {
                        HandleRemoveAccount(accountId);
                    }
                }
                break;

            case "getAccounts":
                SendMessageToJS("accountsLoaded", new { accounts = GetAllAccountsForUI() });
                break;
            
            case "acceptQuest":
                if (message.ContainsKey("questId"))
                {
                    var questId = message["questId"].GetString();
                    if (!string.IsNullOrEmpty(questId))
                    {
                        await AcceptQuestAsync(questId);
                    }
                }
                break;
            
            case "openClaimWindow":
                _ = HandleOpenClaimWindow();
                break;

            case "closeClaimWindow":
                _ = HandleCloseClaimWindow();
                break;
            
            case "videoProgress":
                // Handle progress updates if needed
                break;
            
        case "stopQuests":
                StopAllQuests();
                break;

        case "clearCache":
                 PerformClearCache();
                 break;

            case "refreshBalance":
                _ = HandleRefreshBalanceAsync();
                break;
        }
    }

    private async Task HandleRefreshBalanceAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_currentToken)) return;

            // Notify UI loading start
            SendMessageToJS("balanceLoading", new { loading = true });

            var balance = await _apiService!.GetUserBalanceAsync();
            
            // Send update to UI
            SendMessageToJS("userInfoUpdated", new { 
                balance = balance 
            });
        }
        catch (Exception ex)
        {
            LogError($"HandleRefreshBalanceAsync error: {ex.Message}");
        }
        finally
        {
             // Notify UI loading end
            SendMessageToJS("balanceLoading", new { loading = false });
        }
    }
    
    private async Task AcceptQuestAsync(string questId)
    {
        try
        {
            LogError($"AcceptQuestAsync started for quest: {questId}");
            
            var result = await _apiService!.EnrollQuestAsync(questId);
            
            if (result.EnrolledAt != null)
            {
                LogError($"Quest {questId} accepted successfully");
                
                // Notify UI - this will trigger reload and tab switch
                SendMessageToJS("questAccepted", new { questId = questId });
            }
            else
            {
                SendToast("Quest enrollment failed", "error");
            }
        }
        catch (Exception ex)
        {
            LogError($"AcceptQuestAsync error: {ex}");
            SendToast($"Failed to accept quest: {ex.Message}", "error");
            
            // Reset button state on error
            SendMessageToJS("questAcceptError", new { questId = questId });
        }
    }

    private async Task HandleOpenClaimWindow()
    {
        try
        {
            if (_claimWebView == null)
            {
                await InitializeClaimWebView();
            }

            // Get bounds of the host container from main WebView
            var script = @"
                (function() {
                    var el = document.getElementById('claimWebViewHost');
                    if (!el) return null;
                    var rect = el.getBoundingClientRect();
                    return JSON.stringify({ x: rect.x, y: rect.y, width: rect.width, height: rect.height });
                })();
            ";
            
            var result = await _webView.ExecuteScriptAsync(script);
            if (result == "null") return;

            var boundsJson = JsonSerializer.Deserialize<string>(result);
            if (boundsJson == null) return;

            var bounds = JsonSerializer.Deserialize<Dictionary<string, double>>(boundsJson);

            if (bounds != null)
            {
                _claimWebView!.SetBounds(
                    (int)bounds["x"],
                    (int)bounds["y"],
                    (int)bounds["width"],
                    (int)bounds["height"]
                );
            }

            _claimWebView!.Visible = true;
            _claimWebView.BringToFront();

            // Navigate
            if (!string.IsNullOrEmpty(_currentToken))
            {
                // Navigate to login to inject token
                _claimWebView.CoreWebView2.Navigate("https://discord.com/login");
            }
            else
            {
                _claimWebView.CoreWebView2.Navigate("https://discord.com/quest-home");
            }
        }
        catch (Exception ex)
        {
            LogError($"HandleOpenClaimWindow error: {ex.Message}");
            SendToast("Failed to open claim window", "error");
        }
    }

    private async Task HandleCloseClaimWindow()
    {
        if (_claimWebView != null)
        {
            _claimWebView.Visible = false;
            try 
            {
                // Stop loading
                _claimWebView.CoreWebView2.Stop();
                // Clear browsing data to ensure clean state next time (as requested)
                await _claimWebView.CoreWebView2.Profile.ClearBrowsingDataAsync();
            }
            catch (Exception ex)
            {
                LogError($"Error clearing WebView data: {ex.Message}");
            }
            
            // Auto-refresh balance when closing claim window
            _ = HandleRefreshBalanceAsync();
        }
    }

    private async Task InitializeClaimWebView()
    {
        _claimWebView = new WebView2();
        _claimWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(47, 49, 54);
        _claimWebView.Visible = false;
        this.Controls.Add(_claimWebView);

        // Use a temp user data folder to keep it isolated
        var userDataFolder = Path.Combine(System.IO.Path.GetTempPath(), "DiscordQuestGUI_ClaimSession");
        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        
        await _claimWebView.EnsureCoreWebView2Async(env);
        
        _claimWebView.CoreWebView2.NavigationCompleted += ClaimWebView_NavigationCompleted;
        _claimWebView.CoreWebView2.NewWindowRequested += (s, e) => {
            e.Handled = true;
            _claimWebView.CoreWebView2.Navigate(e.Uri);
        };
    }

    private void ClaimWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;

        var currentUrl = _claimWebView?.Source.ToString() ?? "";
        
        // Inject token on login page
        if (currentUrl.Contains("discord.com/login") && !string.IsNullOrEmpty(_currentToken))
        {
            var script = $@"
                (function() {{
                    const token = '{_currentToken}';
                    let interval = setInterval(() => {{
                        document.body.appendChild(document.createElement('iframe')).contentWindow.localStorage.token = '""' + token + '""';
                    }}, 50);
                    setTimeout(() => {{ 
                        clearInterval(interval); 
                        location.reload(); 
                    }}, 2500);
                }})();
            ";
            _claimWebView?.ExecuteScriptAsync(script);
        }
    }

    private async Task HandleLoginAsync()
    {
        try
        {
            LogError("HandleLoginAsync started");
            SendLoadingState(true, "Authenticating...");

            LogError("Calling LoginAsync...");
            var (token, cookies) = await _authService.LoginAsync();
            LogError($"LoginAsync returned, token: {(string.IsNullOrEmpty(token) ? "empty" : "exists")}, cookies: {(string.IsNullOrEmpty(cookies) ? "empty" : "exists")}");

            if (string.IsNullOrEmpty(token))
            {
                LogError("Token is empty, login cancelled");
                SendLoadingState(false, "");
                SendToast("Login cancelled or failed", "error");
                return;
            }

            LogError("Saving account credentials...");
            SendLoadingState(true, "Saving authentication...");

            // Account will be created/updated in InitializeApiServices
            // Don't update here to avoid conflicts

            // Initialize services
            LogError("Calling InitializeApiServices...");
            await InitializeApiServices(token, cookies);
            LogError("InitializeApiServices completed");
        }
        catch (Exception ex)
        {
            LogError($"HandleLoginAsync error: {ex}");
            SendLoadingState(false, "");
            SendToast($"Login failed: {ex.Message}", "error");
        }
    }

    private async Task LoadQuestsAsync()
    {
        try
        {
            if (_apiService == null) 
            {
                SendMessageToJS("error", new { message = "Not logged in. Please login again." });
                return;
            }

            var (available, accepted, completed) = await _apiService.GetQuestsAsync();
            _availableQuests = available;
            _acceptedQuests = accepted;
            _completedQuests = completed;

            var questsData = new
            {
                available = SerializeQuests(available),
                accepted = SerializeQuests(accepted),
                completed = SerializeQuests(completed)
            };

            SendMessageToJS("questsLoaded", questsData);
            
            // Show message if no quests found
            if (available.Count == 0 && accepted.Count == 0 && completed.Count == 0)
            {
                SendToast("No quests found. Please check Discord app.", "info");
            }
        }
        catch (Exception ex)
        {
            LogError($"LoadQuestsAsync error: {ex}");
            
            // Check if token expired
            if (ex.Message.Contains("Token") || ex.Message.Contains("Unauthorized") || ex.Message.Contains("401"))
            {
                SendMessageToJS("error", new { message = "Token expired. Please login again." });
                _configService.ClearToken();
                SendMessageToJS("showLoginScreen", new { });
            }
            else
            {
                SendMessageToJS("error", new { message = ex.Message });
            }
        }
    }

    private object[] SerializeQuests(List<Quest> quests)
    {
        return quests.Where(q => q.Config != null).Select(q => new
        {
            id = q.Id,
            config = new
            {
                configVersion = q.Config?.ConfigVersion,
                expiresAt = q.Config?.ExpiresAt,
                application = q.Config?.Application != null ? new
                {
                    id = q.Config.Application.Id,
                    name = q.Config.Application.Name
                } : null,
                messages = q.Config?.Messages != null ? new
                {
                    questName = q.Config.Messages.QuestName,
                    gameTitle = q.Config.Messages.GameTitle
                } : null,
                assets = q.Config?.Assets != null ? new
                {
                    hero = q.Config.Assets.Hero,
                    questBarHero = q.Config.Assets.QuestBarHero,
                    gameTile = q.Config.Assets.GameTile,
                    logotype = q.Config.Assets.Logotype
                } : null,
                rewardsConfig = q.Config?.RewardsConfig != null ? new
                {
                    rewards = q.Config.RewardsConfig.Rewards?.Select(r => new
                    {
                        type = r.Type,
                        skuId = r.SkuId,
                        asset = r.Asset,
                        messages = r.Messages != null ? new
                        {
                            name = r.Messages.Name,
                            nameWithArticle = r.Messages.NameWithArticle
                        } : null,
                        orbQuantity = r.OrbQuantity
                    }).ToArray()
                } : null,
                taskConfig = q.Config?.TaskConfig,
                taskConfigV2 = q.Config?.TaskConfigV2
            },
            userStatus = q.UserStatus != null ? new
            {
                userId = q.UserStatus.UserId,
                questId = q.UserStatus.QuestId,
                enrolledAt = q.UserStatus.EnrolledAt,
                completedAt = q.UserStatus.CompletedAt,
                claimedAt = q.UserStatus.ClaimedAt,
                progress = q.UserStatus.Progress,
                streamProgressSeconds = q.UserStatus.StreamProgressSeconds
            } : null
        }).ToArray();
    }

    private void StartQuests(List<string> questIds)
    {
        _questQueue.Clear();
        foreach (var id in questIds)
        {
            _questQueue.Enqueue(id);
        }

        // Notify UI that quests are starting
        SendMessageToJS("questsStarted", new { questIds = questIds });
        SendLoadingState(false, "");

        if (!_isProcessingQueue)
        {
            ProcessNextQuest();
        }
    }
    
    private async void StopAllQuests()
    {
        // Clear the queue
        _questQueue.Clear();
        _isProcessingQueue = false;
        
        // Cancel current running quest
        _questWorkerService?.Cancel();
        
        // Notify UI immediately
        SendMessageToJS("questsStopped", new { });
        SendToast("Stopping quests...", "info");
        
        // Note: Discord server may continue tracking for ~30-60s after last heartbeat
        // This is normal Discord behavior - progress is tracked server-side
        LogError("Quests stopped. Note: Discord may show progress for ~30-60s more (server-side tracking)");
    }

    private async void ProcessNextQuest()
    {
        if (_questQueue.Count == 0)
        {
            _isProcessingQueue = false;

            try
            {
                // Show notification if enabled
                var settings = _configService.LoadSettings();
                if (settings.EnableNotifications && _notificationService != null)
                {
                    // Ensure we have a valid window handle
                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        _notificationService.ShowAllCompletedNotification(_acceptedQuests.Count);
                    }
                    else
                    {
                        LogError("Cannot show notification: window handle not created or disposed");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error showing completion notification: {ex.Message}");
                // Don't crash the app for notification errors
            }

            return;
        }

        _isProcessingQueue = true;
        var questId = _questQueue.Dequeue();
        
        // Reload quests to get latest progress data
        LogError($"Reloading quest data before starting quest {questId}");
        try
        {
            var (available, accepted, completed) = await _apiService!.GetQuestsAsync();
            _availableQuests = available;
            _acceptedQuests = accepted;
            _completedQuests = completed;
        }
        catch (Exception ex)
        {
            LogError($"Failed to reload quests: {ex.Message}");
        }
        
        var quest = _acceptedQuests.FirstOrDefault(q => q.Id == questId) 
                 ?? _availableQuests.FirstOrDefault(q => q.Id == questId);

        if (quest == null)
        {
            LogError($"Quest {questId} not found after reload");
            ProcessNextQuest();
            return;
        }
        
        // Check if quest is already completed
        if (quest.UserStatus?.CompletedAt != null)
        {
            LogError($"Quest {questId} is already completed, skipping");
            SendMessageToJS("questCompleted", new { questId = quest.Id });
            ProcessNextQuest();
            return;
        }
        
        var currentProgress = quest.UserStatus?.Progress?.GetValueOrDefault("PLAY_ON_DESKTOP")?.Value
            ?? quest.UserStatus?.Progress?.GetValueOrDefault("WATCH_VIDEO")?.Value
            ?? quest.UserStatus?.StreamProgressSeconds
            ?? 0;
        LogError($"Starting quest {questId}, current progress: {currentProgress}s");

        if (_questWorkerService != null)
        {
            await _questWorkerService.StartQuestAsync(quest);
        }
    }

    private void OnQuestProgressChanged(object? sender, QuestProgressEventArgs e)
    {
        LogError($"OnQuestProgressChanged: quest={e.QuestId}, percentage={e.Percentage}, completed={e.IsCompleted}, error={e.ErrorMessage}");

        if (InvokeRequired)
        {
            Invoke(() => OnQuestProgressChanged(sender, e));
            return;
        }

        try
        {
            if (e.IsCompleted)
            {
                LogError($"Quest {e.QuestId} completed, sending to JS");
                SendMessageToJS("questCompleted", new { questId = e.QuestId });
                
                // Show Windows notification
                try
                {
                    // Find the quest object to pass
                    var completedQuest = _acceptedQuests.FirstOrDefault(q => q.Id == e.QuestId) 
                                      ?? _completedQuests.FirstOrDefault(q => q.Id == e.QuestId);
                                      
                    if (completedQuest != null)
                    {
                        _notificationService.ShowCompletionNotification(completedQuest);
                    }
                    else
                    {
                         _notificationService.ShowNotification(
                            "Quest Complete!",
                            "Quest has been completed successfully."
                        );
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Failed to show notification: {ex.Message}");
                }
                
                ProcessNextQuest();
            }
            else if (string.IsNullOrEmpty(e.ErrorMessage))
            {
                LogError($"Sending progress to JS: {e.Percentage}%");
                
                // Update tray tooltip with current quest progress
                var currentQuest = _acceptedQuests.FirstOrDefault(q => q.Id == e.QuestId);
                if (currentQuest != null)
                {
                    _currentQuestName = currentQuest.Config?.Messages?.QuestName ?? "Current Quest";
                    _currentQuestProgress = e.Percentage;
                    UpdateTrayTooltip();
                }
                
                SendMessageToJS("questProgress", new
                {
                    questId = e.QuestId,
                    percentage = e.Percentage,
                    secondsRemaining = e.SecondsRemaining
                });
            }
            else
            {
                LogError($"Quest error: {e.ErrorMessage}");
                SendMessageToJS("error", new { message = e.ErrorMessage });
                ProcessNextQuest();
            }
        }
        catch (Exception ex)
        {
            LogError($"Error in OnQuestProgressChanged: {ex.Message}");
            LogError($"Stack trace: {ex.StackTrace}");

            // Try to continue processing next quest even if UI update fails
            try
            {
                if (e.IsCompleted)
                {
                    ProcessNextQuest();
                }
            }
            catch (Exception ex2)
            {
                LogError($"Failed to process next quest: {ex2.Message}");
            }
        }
    }

    private void SendSettings()
    {
        var settings = _configService.LoadSettings();
        SendMessageToJS("settingsLoaded", new
        {
            enableNotifications = settings.EnableNotifications,
            autoRefreshInterval = settings.AutoRefreshInterval,
            minimizeToTray = settings.MinimizeToTray
        });
    }

    private void Logout()
    {
        // Clear current account
        var currentAccount = _accountService.GetCurrentAccount();
        if (currentAccount != null)
        {
            _accountService.RemoveAccount(currentAccount.Id);
        }

        // Check if there are other accounts
        var accounts = _accountService.GetAllAccounts();
        if (accounts.Any())
        {
            // Switch to first available account
            _accountService.SwitchToAccount(accounts[0].Id);
            System.Windows.Forms.Application.Restart();
        }
        else
        {
            // No accounts left, restart to login screen
            System.Windows.Forms.Application.Restart();
        }
    }

    // Account management methods
    private object[] GetAllAccountsForUI()
    {
        var accounts = _accountService.GetAllAccounts();
        var currentAccount = _accountService.GetCurrentAccount();

        return accounts.Select(account => new
        {
            id = account.Id,
            username = account.Username,
            discriminator = account.Discriminator,
            avatar = account.Avatar,
            isActive = account.Id == currentAccount?.Id,
            lastLogin = account.LastLogin
        }).ToArray();
    }

    private async Task HandleSwitchAccountAsync(string accountId)
    {
        try
        {
            SendLoadingState(true, "Switching account...");

            // Switch to selected account
            _accountService.SwitchToAccount(accountId);

            // Get credentials for new account
            var (token, cookies) = _accountService.GetCurrentAccountCredentials();

            if (string.IsNullOrEmpty(token))
            {
                SendLoadingState(false, "");
                SendToast("Account credentials not found", "error");
                return;
            }

            // Validate token
            var isValid = await ValidateTokenAsync(token);
            if (!isValid)
            {
                SendLoadingState(false, "");
                SendToast("Account token expired, please login again", "error");
                SendMessageToJS("showLoginScreen", new { });
                return;
            }

            // Initialize services with new account
            await InitializeApiServices(token, cookies);

        }
        catch (Exception ex)
        {
            LogError($"SwitchAccount error: {ex}");
            SendLoadingState(false, "");
            SendToast($"Failed to switch account: {ex.Message}", "error");
        }
    }

    private void HandleAddAccount()
    {
        // This will trigger the login flow for a new account
        SendMessageToJS("showLoginScreen", new { });
    }

    private void HandleRemoveAccount(string accountId)
    {
        try
        {
            var currentAccount = _accountService.GetCurrentAccount();
            if (currentAccount?.Id == accountId)
            {
                SendToast("Cannot remove currently active account", "warning");
                return;
            }

            _accountService.RemoveAccount(accountId);
            SendMessageToJS("accountsUpdated", new { accounts = GetAllAccountsForUI() });
            SendToast("Account removed", "success");
        }
        catch (Exception ex)
        {
            LogError($"RemoveAccount error: {ex}");
            SendToast($"Failed to remove account: {ex.Message}", "error");
        }
    }

    private string EncryptToken(string token)
    {
        // Use the same encryption as ConfigService
        using var aes = Aes.Create();
        var key = Encoding.UTF8.GetBytes("DiscordQuest2024");
        var iv = Encoding.UTF8.GetBytes("QuestAutoComp16!");
        aes.Key = key;
        aes.IV = iv;

        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
        using var sw = new StreamWriter(cs);
        sw.Write(token);
        sw.Flush();
        cs.FlushFinalBlock();

        return Convert.ToBase64String(ms.ToArray());
    }

    private void SendMessageToJS(string type, object data)
    {
        try
        {
            if (_webView == null || _webView.CoreWebView2 == null)
            {
                // WebView not ready yet, queue or ignore
                System.Diagnostics.Debug.WriteLine($"WebView not ready, cannot send: {type}");
                LogError($"WebView not ready, cannot send message type: {type}");
                return;
            }

            var message = new { type, data };
            var json = JsonSerializer.Serialize(message);

            if (InvokeRequired)
            {
                Invoke(() => _webView.CoreWebView2.PostWebMessageAsJson(json));
            }
            else
            {
                _webView.CoreWebView2.PostWebMessageAsJson(json);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to send message to JS: {ex.Message}");
            LogError($"Failed to send message to JS (type: {type}): {ex.Message}");
            // Don't re-throw, just log the error
        }
    }

    private void SendToast(string message, string type = "info")
    {
        SendMessageToJS("toast", new { message, type });
    }

    private void SendLoadingState(bool isLoading, string message = "")
    {
        SendMessageToJS("loading", new { isLoading, message });
    }
    private void PerformClearCache()
    {
        try
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiscordQuestGUI"
            );

            var exePath = System.Windows.Forms.Application.ExecutablePath;
            var batchPath = Path.Combine(Path.GetTempPath(), "discord_quest_cleanup.bat");

            var script = $@"
@echo off
timeout /t 2 /nobreak >nul
rmdir /s /q ""{appDataPath}""
start """" ""{exePath}""
del ""%~f0""
";
            File.WriteAllText(batchPath, script);

            var startInfo = new ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(startInfo);
            System.Windows.Forms.Application.Exit();
        }
        catch (Exception ex)
        {
            LogError($"PerformClearCache error: {ex}");
            SendToast($"Clear cache failed: {ex.Message}", "error");
        }
    }

    #region Tray Icon Methods

    private void InitializeTrayIcon()
    {
        try
        {
            _trayIcon = new NotifyIcon();

            // Load icon from appicon.ico
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appicon.ico");
            if (File.Exists(iconPath))
            {
                _trayIcon.Icon = new Icon(iconPath);
            }
            else
            {
                // Fallback to system icon
                _trayIcon.Icon = SystemIcons.Application;
                LogError("appicon.ico not found, using fallback icon");
            }

            _trayIcon.Text = "Discord Quest Auto-Complete";
            _trayIcon.Visible = false; // Hidden by default, shown when minimized

            // Double-click to restore
            _trayIcon.DoubleClick += (s, e) => ShowMainForm();

            // Create context menu
            UpdateContextMenu();
        }
        catch (Exception ex)
        {
            LogError($"InitializeTrayIcon error: {ex}");
        }
    }

    private void UpdateTrayTooltip()
    {
        if (_trayIcon == null) return;

        try
        {
            string tooltip;
            if (!string.IsNullOrEmpty(_currentQuestName) && _currentQuestProgress > 0)
            {
                tooltip = $"{_currentQuestName} - {_currentQuestProgress}%";
            }
            else
            {
                tooltip = "Discord Quest Auto-Complete";
            }

            // Tooltip max length is 63 characters
            if (tooltip.Length > 63)
            {
                tooltip = tooltip.Substring(0, 60) + "...";
            }

            _trayIcon.Text = tooltip;
        }
        catch (Exception ex)
        {
            LogError($"UpdateTrayTooltip error: {ex}");
        }
    }

    private void UpdateContextMenu()
    {
        if (_trayIcon == null) return;

        try
        {
            var contextMenu = new ContextMenuStrip();
            
            // Apply Discord style - Segoe UI font
            contextMenu.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            contextMenu.BackColor = Color.FromArgb(47, 49, 54); // Discord dark background
            contextMenu.ForeColor = Color.FromArgb(220, 221, 222); // Discord light text
            
            // Remove image margin (white column on left)
            contextMenu.ShowImageMargin = false;
            contextMenu.ShowCheckMargin = false;
            
            // Use custom renderer for Discord-style hover
            contextMenu.Renderer = new DiscordContextMenuRenderer();

            // Current User (disabled, bold) - only if we have username
            if (!string.IsNullOrEmpty(_currentUsername))
            {
                var userItem = new ToolStripMenuItem
                {
                    Text = _currentUsername,
                    Enabled = false,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(185, 187, 190) // Slightly dimmed for disabled
                };
                contextMenu.Items.Add(userItem);

                // Separator
                contextMenu.Items.Add(new ToolStripSeparator());
            }

            // Show
            var showItem = new ToolStripMenuItem("Show", null, (s, e) => ShowMainForm());
            showItem.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            contextMenu.Items.Add(showItem);

            // Exit
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());
            exitItem.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = contextMenu;
        }
        catch (Exception ex)
        {
            LogError($"UpdateContextMenu error: {ex}");
        }
    }

    private void ShowMainForm()
    {
        try
        {
            this.Show();
            this.WindowState = FormWindowState.Maximized;
            this.Activate();
            
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
            }
        }
        catch (Exception ex)
        {
            LogError($"ShowMainForm error: {ex}");
        }
    }

    private void ExitApplication()
    {
        try
        {
            // Stop all running quests
            StopAllQuests();

            // Cleanup tray icon
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            // Exit application
            System.Windows.Forms.Application.Exit();
        }
        catch (Exception ex)
        {
            LogError($"ExitApplication error: {ex}");
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        try
        {
            // Check MinimizeToTray setting
            var settings = _configService.LoadSettings();
            if (settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                // Cancel close and minimize to tray instead
                e.Cancel = true;
                this.Hide();
                
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = true;
                    
                    // Show balloon tip on first minimize
                    if (!_trayIcon.Tag?.Equals("shown") == true)
                    {
                        _trayIcon.ShowBalloonTip(
                            2000,
                            "Discord Quest Auto-Complete",
                            "Application minimized to tray. Double-click icon to restore.",
                            ToolTipIcon.Info
                        );
                        _trayIcon.Tag = "shown";
                    }
                }
            }
            else
            {
                // Actually closing - cleanup
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"MainForm_FormClosing error: {ex}");
        }
    }

    #endregion
    
    #region Discord Context Menu Renderer
    
    private class DiscordContextMenuRenderer : ToolStripProfessionalRenderer
    {
        public DiscordContextMenuRenderer() : base(new DiscordColorTable())
        {
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected)
            {
                // Not selected - use background color
                using (var brush = new SolidBrush(Color.FromArgb(47, 49, 54)))
                {
                    e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
                }
            }
            else
            {
                // Selected/hover - use Discord blue-ish gray
                using (var brush = new SolidBrush(Color.FromArgb(67, 70, 79)))
                {
                    e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
                }
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            // Discord-style separator
            var rect = new Rectangle(3, e.Item.Height / 2, e.Item.Width - 6, 1);
            using (var brush = new SolidBrush(Color.FromArgb(60, 63, 69)))
            {
                e.Graphics.FillRectangle(brush, rect);
            }
        }
    }

    private class DiscordColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(67, 70, 79); // Discord hover color
        public override Color MenuItemBorder => Color.FromArgb(47, 49, 54); // No border
        public override Color MenuBorder => Color.FromArgb(32, 34, 37); // Darker border
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(67, 70, 79);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(67, 70, 79);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(67, 70, 79);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(67, 70, 79);
        public override Color ImageMarginGradientBegin => Color.FromArgb(47, 49, 54);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(47, 49, 54);
        public override Color ImageMarginGradientEnd => Color.FromArgb(47, 49, 54);
    }
    
    #endregion
}
