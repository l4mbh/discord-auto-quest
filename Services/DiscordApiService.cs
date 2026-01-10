using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordQuestGUI.Models;

namespace DiscordQuestGUI.Services;

public class DiscordApiService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://discord.com/api/v9";
    private static readonly string[] SupportedTasks = 
    {
        "WATCH_VIDEO",
        "WATCH_VIDEO_ON_MOBILE",
        "PLAY_ON_DESKTOP",
        "STREAM_ON_DESKTOP",
        "PLAY_ACTIVITY"
    };
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DiscordApiService(string token, string? cookies = null)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", token);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) discord/1.0.9219 Chrome/138.0.7204.251 Electron/37.6.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        
        // Critical: x-super-properties header (base64 encoded client info)
        var superProperties = GenerateSuperProperties();
        _httpClient.DefaultRequestHeaders.Add("X-Super-Properties", superProperties);
        
        // Additional headers to mimic Discord client
        _httpClient.DefaultRequestHeaders.Add("X-Discord-Locale", "en-US");
        _httpClient.DefaultRequestHeaders.Add("X-Discord-Timezone", "Asia/Bangkok");
        _httpClient.DefaultRequestHeaders.Add("X-Debug-Options", "bugReporterEnabled");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://discord.com/quest-home");
        _httpClient.DefaultRequestHeaders.Add("Sec-Ch-Ua", "\"Not)A;Brand\";v=\"8\", \"Chromium\";v=\"138\"");
        _httpClient.DefaultRequestHeaders.Add("Sec-Ch-Ua-Mobile", "?0");
        _httpClient.DefaultRequestHeaders.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        _httpClient.DefaultRequestHeaders.Add("Priority", "u=1, i");
        
        // Add cookies if provided
        if (!string.IsNullOrEmpty(cookies))
        {
            _httpClient.DefaultRequestHeaders.Add("Cookie", cookies);
            LogDebug($"Cookies added to HttpClient");
        }
        
        LogDebug($"DiscordApiService initialized with x-super-properties");
    }
    
    private static string GenerateSuperProperties()
    {
        // Generate x-super-properties header matching Discord client
        var props = new
        {
            os = "Windows",
            browser = "Discord Client",
            release_channel = "stable",
            client_version = "1.0.9219",
            os_version = "10.0.19045",
            os_arch = "x64",
            app_arch = "x64",
            system_locale = "en-US",
            has_client_mods = false,
            browser_user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) discord/1.0.9219 Chrome/138.0.7204.251 Electron/37.6.0 Safari/537.36",
            browser_version = "37.6.0",
            os_sdk_version = "19045",
            client_build_number = 483861,
            native_build_number = 73385,
            client_event_source = (string?)null
        };
        
        var json = JsonSerializer.Serialize(props, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    public async Task<bool> ValidateTokenAsync()
    {
        try
        {
            var response = await RetryAsync(() => _httpClient.GetAsync($"{BaseUrl}/users/@me"));
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(string username, string discriminator, string avatar, string id)> ValidateTokenAndGetUserAsync()
    {
        try
        {
            var response = await RetryAsync(() => _httpClient.GetAsync($"{BaseUrl}/users/@me"));
            
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new Exception("Token invalid or expired.");
            }
            
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Authentication failed: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            LogDebug($"[RESEARCH] User Profile JSON: {json}");
            var user = JsonSerializer.Deserialize<JsonElement>(json);

            // Handle new Discord username format (discriminator = "0" for new accounts)
            var discriminator = user.TryGetProperty("discriminator", out var discProp) 
                ? discProp.GetString() ?? "0" 
                : "0";
            
            // For new accounts, discriminator is "0", so we show username without #0
            var displayDiscriminator = discriminator == "0" ? "0" : discriminator;

            return (
                user.GetProperty("username").GetString() ?? "User",
                displayDiscriminator,
                user.TryGetProperty("avatar", out var avatarProp) ? avatarProp.GetString() ?? "" : "",
                user.GetProperty("id").GetString() ?? ""
            );
        }
        catch (Exception ex) when (ex.Message.Contains("Token"))
        {
            throw; // Re-throw token errors
        }
        catch (Exception)
        {
            return ("User", "0", "", "");
        }
    }

    public async Task<long> GetVirtualCurrencyBalanceAsync()
    {
        try
        {
            var response = await RetryAsync(() => _httpClient.GetAsync($"{BaseUrl}/users/@me/virtual-currency/balance"));
            
            if (!response.IsSuccessStatusCode)
            {
                LogDebug($"GetVirtualCurrencyBalanceAsync failed: {response.StatusCode}");
                return 0;
            }

            var json = await response.Content.ReadAsStringAsync();
            LogDebug($"VirtualCurrencyResponse: {json}");
            
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("balance", out var balanceProp))
            {
                return balanceProp.GetInt64();
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            LogDebug($"Error getting virtual currency balance: {ex.Message}");
            return 0;
        }
    }

    public async Task<UserBalance> GetUserBalanceAsync()
    {
        var balance = await GetVirtualCurrencyBalanceAsync();
        return new UserBalance { Amount = (int)balance, Currency = "Orbs" };
    }

    public async Task<(List<Quest> available, List<Quest> accepted, List<Quest> completed)> GetQuestsAsync()
    {
        var requestUrl = $"{BaseUrl}/quests/@me";
        LogDebug($"GetQuestsAsync called - requesting: {requestUrl}");
        
        try
        {
            var response = await RetryAsync(() => _httpClient.GetAsync(requestUrl));
            LogDebug($"API response status: {response.StatusCode}");
            
            // Handle specific error cases
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                LogDebug("API returned 404 - returning empty lists");
                return (new List<Quest>(), new List<Quest>(), new List<Quest>());
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                LogDebug("API returned 401 - token expired");
                throw new Exception("Token expired. Please login again.");
            }
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                LogDebug($"API error: {response.StatusCode} - {errorContent}");
                throw new Exception($"API Error ({response.StatusCode}): {errorContent}");
            }

            var json = await response.Content.ReadAsStringAsync();
            LogDebug($"[RESEARCH] Quests API Response JSON: {json}");
            LogDebug($"Quests API response length: {json.Length}");
        
        // Parse response - API returns {quests: [...]}
        List<Quest> allQuests;
        try
        {
            var questsResponse = JsonSerializer.Deserialize<QuestsResponse>(json, JsonOptions);
            allQuests = questsResponse?.Quests ?? new List<Quest>();
            LogDebug($"Parsed wrapped response: {allQuests.Count} quests");
        }
        catch (Exception ex)
        {
            LogDebug($"Failed to parse wrapped response: {ex.Message}, trying array...");
            // Fallback: try as array directly
            allQuests = JsonSerializer.Deserialize<List<Quest>>(json, JsonOptions) ?? new List<Quest>();
        }
        
        LogDebug($"Total quests parsed: {allQuests.Count}");
        
        // Debug: log first quest details
        if (allQuests.Count > 0)
        {
            var first = allQuests[0];
            LogDebug($"First quest: id={first.Id}, config null={first.Config == null}");
            if (first.Config != null)
            {
                LogDebug($"  expires_at={first.Config.ExpiresAt}, quest_name={first.Config.Messages?.QuestName}");
                LogDebug($"  task_config null={first.Config.TaskConfig == null}, task_config_v2 null={first.Config.TaskConfigV2 == null}");
                var tc = first.Config.TaskConfigV2 ?? first.Config.TaskConfig;
                if (tc != null)
                {
                    LogDebug($"  tasks count={tc.Tasks?.Count ?? 0}, tasks keys={string.Join(",", tc.Tasks?.Keys.ToArray() ?? Array.Empty<string>())}");
                }
                // Debug assets
                LogDebug($"  assets null={first.Config.Assets == null}");
                if (first.Config.Assets != null)
                {
                    LogDebug($"  assets.hero={first.Config.Assets.Hero ?? "null"}");
                    LogDebug($"  assets.questBarHero={first.Config.Assets.QuestBarHero ?? "null"}");
                    LogDebug($"  assets.logotype={first.Config.Assets.Logotype ?? "null"}");
                }
                // Debug rewards
                LogDebug($"  rewardsConfig null={first.Config.RewardsConfig == null}");
                if (first.Config.RewardsConfig?.Rewards != null)
                {
                    LogDebug($"  rewards count={first.Config.RewardsConfig.Rewards.Count}");
                    foreach (var r in first.Config.RewardsConfig.Rewards)
                    {
                        LogDebug($"    reward: type={r.Type}, name={r.Messages?.Name ?? "null"}, asset={r.Asset ?? "null"}, orbs={r.OrbQuantity}");
                    }
                }
            }
            LogDebug($"  user_status null={first.UserStatus == null}");
            if (first.UserStatus != null)
            {
                LogDebug($"  enrolled_at={first.UserStatus.EnrolledAt}, completed_at={first.UserStatus.CompletedAt}");
            }
        }

        var now = DateTimeOffset.UtcNow;
        LogDebug($"Current UTC time: {now}");

        // Filter quests that have supported task types and not expired
        var validQuests = allQuests.Where(q =>
        {
            var questName = q.Config?.Messages?.QuestName ?? "Unknown";
            
            // Check not expired
            if (q.Config?.ExpiresAt <= now)
            {
                LogDebug($"Quest '{questName}' filtered: expired ({q.Config?.ExpiresAt} <= {now})");
                return false;
            }

            // Check has supported task type
            var taskConfig = q.Config?.TaskConfigV2 ?? q.Config?.TaskConfig;
            if (taskConfig?.Tasks == null || taskConfig.Tasks.Count == 0)
            {
                LogDebug($"Quest '{questName}' filtered: no tasks");
                return false;
            }

            var hasSupportedTask = taskConfig.Tasks.Keys.Any(taskName => SupportedTasks.Contains(taskName));
            if (!hasSupportedTask)
            {
                LogDebug($"Quest '{questName}' filtered: no supported task (has: {string.Join(",", taskConfig.Tasks.Keys)})");
            }
            return hasSupportedTask;
        }).ToList();
        
        LogDebug($"Valid quests after filter: {validQuests.Count}");

        // Separate into categories:
        // - available: not enrolled
        // - accepted: enrolled but not completed
        // - completed: completed but not claimed (ready to claim!)
        var available = validQuests.Where(q => q.UserStatus?.EnrolledAt == null).ToList();
        var accepted = validQuests.Where(q =>
            q.UserStatus?.EnrolledAt != null &&
            q.UserStatus?.CompletedAt == null
        ).ToList();
        var completed = validQuests.Where(q =>
            q.UserStatus?.CompletedAt != null &&
            q.UserStatus?.ClaimedAt == null
        ).ToList();
        
        LogDebug($"Available: {available.Count}, Accepted: {accepted.Count}, Completed (unclaimed): {completed.Count}");

        return (available, accepted, completed);
        }
        catch (Exception ex)
        {
            LogDebug($"GetQuestsAsync exception: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }
    
    private void LogDebug(string message)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiscordQuestGUI",
                "debug.log"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    public async Task<List<Quest>> GetCompletedQuestsAsync()
    {
        var response = await RetryAsync(() => _httpClient.GetAsync($"{BaseUrl}/quests/@me"));
        
        // Handle specific error cases
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new List<Quest>();
        }
        
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new Exception("Token expired. Please login again.");
        }
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"API Error ({response.StatusCode}): {errorContent}");
        }

        var json = await response.Content.ReadAsStringAsync();
        
        // Parse response - API returns {quests: [...]}
        List<Quest> allQuests;
        try
        {
            var questsResponse = JsonSerializer.Deserialize<QuestsResponse>(json, JsonOptions);
            allQuests = questsResponse?.Quests ?? new List<Quest>();
        }
        catch
        {
            // Fallback: try as array directly
            allQuests = JsonSerializer.Deserialize<List<Quest>>(json, JsonOptions) ?? new List<Quest>();
        }

        // Filter only completed quests
        var completedQuests = allQuests.Where(q =>
            q.UserStatus?.CompletedAt != null
        ).ToList();
        
        LogDebug($"Completed quests: {completedQuests.Count}");

        return completedQuests;
    }

    public async Task<VideoProgressResponse> SendVideoProgressAsync(string questId, double timestamp, CancellationToken ct = default)
    {
        var request = new VideoProgressRequest { Timestamp = timestamp };
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await RetryAsync(() =>
            _httpClient.PostAsync($"{BaseUrl}/quests/{questId}/video-progress", content, ct), ct: ct);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<VideoProgressResponse>(responseJson, JsonOptions) 
            ?? new VideoProgressResponse();
    }

    public async Task<HeartbeatResponse> SendHeartbeatAsync(string questId, HeartbeatRequest request, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await RetryAsync(() =>
            _httpClient.PostAsync($"{BaseUrl}/quests/{questId}/heartbeat", content, ct), ct: ct);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        LogDebug($"Heartbeat response for quest {questId}: {responseJson}");
        
        var result = JsonSerializer.Deserialize<HeartbeatResponse>(responseJson, JsonOptions) 
            ?? new HeartbeatResponse();
        
        LogDebug($"Parsed heartbeat - UserStatus: {(result.UserStatus != null ? "exists" : "null")}, CompletedAt: {result.UserStatus?.CompletedAt}, Progress: {result.UserStatus?.Progress?.Count ?? 0} keys");
        
        return result;
    }

    // Accept/Enroll a quest
    public async Task<EnrollQuestResponse> EnrollQuestAsync(string questId)
    {
        LogDebug($"EnrollQuestAsync called for quest: {questId}");
        
        var requestBody = new
        {
            location = 11,
            is_targeted = false,
            metadata_raw = (string?)null,
            metadata_sealed = (string?)null
        };
        
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await RetryAsync(() =>
            _httpClient.PostAsync($"{BaseUrl}/quests/{questId}/enroll", content));

        var responseJson = await response.Content.ReadAsStringAsync();
        LogDebug($"EnrollQuest response: {response.StatusCode} - {responseJson}");

        if (!response.IsSuccessStatusCode)
        {
            // Try to parse error message
            try
            {
                var errorObj = JsonSerializer.Deserialize<JsonElement>(responseJson);
                if (errorObj.TryGetProperty("message", out var msgProp))
                {
                    throw new Exception(msgProp.GetString() ?? "Failed to accept quest");
                }
            }
            catch (JsonException) { }
            
            throw new Exception($"Failed to accept quest: {response.StatusCode}");
        }

        return JsonSerializer.Deserialize<EnrollQuestResponse>(responseJson, JsonOptions) 
            ?? new EnrollQuestResponse();
    }

    // Claim quest reward - may require captcha
    public async Task<ClaimQuestResult> ClaimQuestAsync(string questId)
    {
        LogDebug($"ClaimQuestAsync called for quest: {questId}");

        // Try claim without captcha first
        var result = await ClaimQuestWithCaptchaAsync(questId, null);
        return result;
    }

    // Claim quest reward with optional captcha tokens
    public async Task<ClaimQuestResult> ClaimQuestWithCaptchaAsync(string questId, CaptchaTokens? captchaTokens = null)
    {
        LogDebug($"ClaimQuestWithCaptchaAsync called for quest: {questId}, captchaTokens: {(captchaTokens != null ? "provided" : "null")}");

        // Prepare request body like Discord client
        var requestBody = new
        {
            platform = 0, // Desktop platform
            location = 11, // Quest home location
            is_targeted = false,
            metadata_raw = (string?)null,
            metadata_sealed = (string?)null
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Create request with additional headers if captcha tokens are provided
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/quests/{questId}/claim-reward")
        {
            Content = content
        };

        // Add captcha headers if provided (based on user's fetch request)
        if (captchaTokens != null)
        {
            if (!string.IsNullOrEmpty(captchaTokens.CaptchaKey))
            {
                request.Headers.Add("x-captcha-key", captchaTokens.CaptchaKey);
                LogDebug($"Added x-captcha-key header");
            }
            if (!string.IsNullOrEmpty(captchaTokens.CaptchaRqtoken))
            {
                request.Headers.Add("x-captcha-rqtoken", captchaTokens.CaptchaRqtoken);
                LogDebug($"Added x-captcha-rqtoken header");
            }
            if (!string.IsNullOrEmpty(captchaTokens.CaptchaSessionId))
            {
                request.Headers.Add("x-captcha-session-id", captchaTokens.CaptchaSessionId);
                LogDebug($"Added x-captcha-session-id header");
            }
        }

        // Note: Use /claim-reward endpoint instead of /claim (based on user's fetch request)
        var response = await RetryAsync(() => _httpClient.SendAsync(request));

        var responseJson = await response.Content.ReadAsStringAsync();
        LogDebug($"ClaimQuest response: {response.StatusCode} - {responseJson}");

        // Check for captcha required
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var errorObj = JsonSerializer.Deserialize<JsonElement>(responseJson);

                // Check if captcha is required
                JsonElement captchaSitekey = default;
                if (errorObj.TryGetProperty("captcha_key", out var captchaKey) ||
                    errorObj.TryGetProperty("captcha_sitekey", out captchaSitekey) ||
                    errorObj.TryGetProperty("sitekey", out captchaSitekey) || // Alternative property name
                    (errorObj.TryGetProperty("message", out var msgProp) &&
                     msgProp.GetString()?.Contains("captcha", StringComparison.OrdinalIgnoreCase) == true))
                {
                    LogDebug($"Captcha required for claim! Full response: {responseJson}");

                    // Extract captcha details for potential future handling
                    var captchaDetails = new CaptchaRequiredDetails();

                    // Try multiple ways to get sitekey
                    if (captchaSitekey.ValueKind == JsonValueKind.String)
                    {
                        captchaDetails.Sitekey = captchaSitekey.GetString();
                        LogDebug($"Found sitekey from captcha_sitekey/sitekey: {captchaDetails.Sitekey}");
                    }
                    else
                    {
                        // Use known Discord hCaptcha sitekey as fallback
                        captchaDetails.Sitekey = "4bb5aadb-b50f-4f23-b1c2-92b59ba400d5";
                        LogDebug($"Using default Discord hCaptcha sitekey: {captchaDetails.Sitekey}");
                    }

                    if (errorObj.TryGetProperty("captcha_service", out var captchaService))
                    {
                        captchaDetails.Service = captchaService.GetString();
                    }
                    if (errorObj.TryGetProperty("captcha_session_id", out var captchaSessionId))
                    {
                        captchaDetails.SessionId = captchaSessionId.GetString();
                    }
                    if (errorObj.TryGetProperty("captcha_rqdata", out var captchaRqdata))
                    {
                        captchaDetails.Rqdata = captchaRqdata.GetString();
                    }

                    return new ClaimQuestResult
                    {
                        Success = false,
                        RequiresCaptcha = true,
                        Message = "Captcha verification required. Please claim in Discord app.",
                        CaptchaDetails = captchaDetails
                    };
                }

                // Other error
                var errorMessage = msgProp.GetString() ?? $"Failed to claim: {response.StatusCode}";
                return new ClaimQuestResult
                {
                    Success = false,
                    RequiresCaptcha = false,
                    Message = errorMessage
                };
            }
            catch (JsonException)
            {
                return new ClaimQuestResult
                {
                    Success = false,
                    Message = $"Failed to claim: {response.StatusCode}"
                };
            }
        }

        // Success!
        return new ClaimQuestResult
        {
            Success = true,
            Message = "Reward claimed successfully!"
        };
    }

    private async Task<HttpResponseMessage> RetryAsync(
        Func<Task<HttpResponseMessage>> action,
        int maxRetries = 5,
        CancellationToken ct = default)
    {
        int attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            
            try
            {
                var response = await action();
                
                // Handle rate limit (429 Too Many Requests)
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt >= maxRetries)
                    {
                        LogDebug($"Rate limit: max retries ({maxRetries}) exceeded");
                        return response;
                    }
                    
                    var retryAfter = await GetRetryAfterAsync(response);
                    LogDebug($"Rate limited! Waiting {retryAfter}ms before retry (attempt {attempt + 1}/{maxRetries})");
                    
                    await Task.Delay(retryAfter, ct);
                    attempt++;
                    continue;
                }
                
                return response;
            }
            catch (OperationCanceledException)
            {
                throw; // Don't retry on cancellation
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                LogDebug($"Request failed: {ex.Message}, retrying...");
                attempt++;
                await Task.Delay((int)Math.Pow(2, attempt) * 1000, ct);
            }
        }
    }
    
    // Parse retry_after from rate limit response
    private async Task<int> GetRetryAfterAsync(HttpResponseMessage response)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(content);
            
            if (json.TryGetProperty("retry_after", out var retryAfterProp))
            {
                // retry_after can be in seconds (float)
                var retryAfterSeconds = retryAfterProp.GetDouble();
                // Convert to milliseconds, add small buffer
                return (int)(retryAfterSeconds * 1000) + 100;
            }
        }
        catch (Exception ex)
        {
            LogDebug($"Failed to parse retry_after: {ex.Message}");
        }
        
        // Default: wait 1 second
        return 1000;
    }
}
