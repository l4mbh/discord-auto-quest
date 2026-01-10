using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace DiscordQuestGUI.Services;

public class DiscordAuthService
{
    private TaskCompletionSource<(string token, string? cookies)>? _tokenTcs;
    private Form? _loginForm;
    private WebView2? _webView;
    private Label? _loadingLabel;

    public async Task<(string? token, string? cookies)> LoginAsync()
    {
        _tokenTcs = new TaskCompletionSource<(string token, string? cookies)>();

        try
        {
            LogError("Creating login form...");
            // Create and show login form
            _loginForm = CreateLoginForm();
            
            LogError("Showing login form...");
            // Show non-blocking
            _loginForm.Show();
            
            LogError("Initializing WebView2...");
            // Initialize WebView2 immediately
            await InitializeWebViewAsync();

            LogError("Waiting for login completion...");
            // Wait for token directly
            var result = await _tokenTcs.Task;
            
            LogError($"Login result: token={(!string.IsNullOrEmpty(result.token) ? "success" : "empty")}, cookies={(!string.IsNullOrEmpty(result.cookies) ? "found" : "none")}");
            
            // Cleanup
            try
            {
                LogError("Cleaning up login form...");
                if (_loginForm != null && !_loginForm.IsDisposed)
                {
                    _loginForm.Dispose();
                }

                // Clean up old auth folders (keep only last 5)
                try
                {
                    CleanupOldAuthFolders();
                }
                catch (Exception cleanupEx)
                {
                    LogError($"Cleanup old folders error (non-critical): {cleanupEx.Message}");
                }

                LogError("Cleanup complete");
            }
            catch (Exception ex)
            {
                LogError($"Cleanup error (non-critical): {ex.Message}");
            }
            
            return result;
        }
        catch (Exception ex)
        {
            LogError($"LoginAsync error: {ex}");
            return (null, null);
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
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [AuthService] {message}\n");
        }
        catch
        {
            // Can't log
        }
    }

    private Form CreateLoginForm()
    {
        var form = new Form
        {
            Text = "Login to Discord",
            Width = 850,
            Height = 550,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        // Add loading label
        _loadingLabel = new Label
        {
            Text = "Initializing WebView2...\nThis may take a moment on first launch.",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11),
            ForeColor = Color.Gray
        };
        form.Controls.Add(_loadingLabel);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = false
        };
        form.Controls.Add(_webView);
        
        form.FormClosed += (s, e) =>
        {
            LogError("FormClosed event triggered");
            if (!_tokenTcs!.Task.IsCompleted)
            {
                LogError("Task not completed, cancelling");
                _tokenTcs.TrySetCanceled();
            }
            else
            {
                LogError("Task already completed, skipping cancel");
            }
        };

        return form;
    }

    private async Task InitializeWebViewAsync()
    {
        if (_webView == null || _loginForm == null || _loadingLabel == null) return;

        try
        {
            // Update loading text
            UpdateLoadingText("Creating WebView2 environment...");

            // Use unique UserDataFolder for each login to ensure fresh session
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var authWebViewDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiscordQuestGUI",
                $"AuthWebView_Data_{timestamp}"
            );
            Directory.CreateDirectory(authWebViewDataFolder);
            LogError($"Auth WebView UserDataFolder: {authWebViewDataFolder}");

            // Create environment options for fresh session
            var options = new CoreWebView2EnvironmentOptions();
            
            var env = await CoreWebView2Environment.CreateAsync(null, authWebViewDataFolder, options);
            
            UpdateLoadingText("Initializing WebView2...");
            await _webView.EnsureCoreWebView2Async(env);
            
            // Use InPrivate context for this WebView
            var controller = _webView.CoreWebView2.Environment;

            UpdateLoadingText("Loading Discord login page...");

            // Hide loading label, show WebView2
            _loginForm.Invoke(() =>
            {
                _loadingLabel.Visible = false;
                _webView.Visible = true;
            });

            // Listen for network requests to capture Authorization header
            _webView.CoreWebView2.WebResourceResponseReceived += OnWebResourceResponseReceived;

            // Navigate to Discord login
            _webView.CoreWebView2.Navigate("https://discord.com/login");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to initialize login:\n{ex.Message}\n\nPlease ensure WebView2 Runtime is installed.\n\nDownload from: https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            _loginForm?.Close();
        }
    }

    private void UpdateLoadingText(string text)
    {
        if (_loadingLabel == null || _loginForm == null) return;

        if (_loginForm.InvokeRequired)
        {
            _loginForm.Invoke(() => _loadingLabel.Text = text);
        }
        else
        {
            _loadingLabel.Text = text;
        }
    }

    private async void OnWebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            // Already got token? Skip processing
            if (_tokenTcs == null || _tokenTcs.Task.IsCompleted)
                return;

            var request = e.Request;
            var headers = request.Headers;

            // COM-safe: Use Contains + GetHeader instead of LINQ
            if (!headers.Contains("Authorization"))
                return;

            LogError("Authorization header found!");
            
            var authHeaderValue = headers.GetHeader("Authorization");
            if (string.IsNullOrEmpty(authHeaderValue))
                return;

            var token = authHeaderValue;
            
            // Clean up token (remove "Bearer " if present)
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = token.Substring(7);
            }

            LogError($"Token captured: {token.Substring(0, Math.Min(10, token.Length))}...");
            
            // Get cookies from WebView2
            string? cookies = null;
            try
            {
                if (_webView?.CoreWebView2?.CookieManager != null)
                {
                    var cookieList = await _webView.CoreWebView2.CookieManager.GetCookiesAsync("https://discord.com");
                    if (cookieList != null && cookieList.Count > 0)
                    {
                        var cookieParts = new List<string>();
                        foreach (var cookie in cookieList)
                        {
                            cookieParts.Add($"{cookie.Name}={cookie.Value}");
                        }
                        cookies = string.Join("; ", cookieParts);
                        LogError($"Cookies captured: {cookies.Substring(0, Math.Min(50, cookies.Length))}...");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to get cookies: {ex.Message}");
            }
            
            // CRITICAL: Unsubscribe immediately to prevent multiple calls
            if (_webView != null && _webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.WebResourceResponseReceived -= OnWebResourceResponseReceived;
                LogError("Event unsubscribed");
            }

            // Set result
            LogError("Setting token result...");
            _tokenTcs?.TrySetResult((token, cookies));
            LogError("Token result set");
            
            // Close form safely on UI thread
            LogError("Closing login form...");
            if (_loginForm != null && !_loginForm.IsDisposed)
            {
                try
                {
                    if (_loginForm.InvokeRequired)
                    {
                        _loginForm.Invoke(() =>
                        {
                            try
                            {
                                LogError("Invoke: closing form");
                                if (!_loginForm.IsDisposed)
                                {
                                    _loginForm.Close();
                                }
                                LogError("Invoke: form closed");
                            }
                            catch (Exception ex)
                            {
                                LogError($"Invoke close error: {ex.Message}");
                            }
                        });
                    }
                    else
                    {
                        LogError("Direct: closing form");
                        if (!_loginForm.IsDisposed)
                        {
                            _loginForm.Close();
                        }
                        LogError("Direct: form closed");
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Form close outer error: {ex.Message}");
                }
            }
            LogError("Form close initiated");
        }
        catch (Exception ex)
        {
            LogError($"OnWebResourceResponseReceived error: {ex}");
        }
    }

    private void CleanupOldAuthFolders()
    {
        try
        {
            var basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiscordQuestGUI"
            );

            var authFolders = Directory.GetDirectories(basePath, "AuthWebView_Data_*")
                .Select(dir => new
                {
                    Path = dir,
                    Created = Directory.GetCreationTime(dir)
                })
                .OrderByDescending(x => x.Created)
                .Skip(5) // Keep last 5 folders
                .ToList();

            foreach (var folder in authFolders)
            {
                try
                {
                    Directory.Delete(folder.Path, true);
                    LogError($"Cleaned up old auth folder: {Path.GetFileName(folder.Path)}");
                }
                catch (Exception ex)
                {
                    LogError($"Failed to delete old auth folder {Path.GetFileName(folder.Path)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"CleanupOldAuthFolders error: {ex.Message}");
        }
    }
}
