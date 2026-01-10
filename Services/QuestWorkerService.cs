using DiscordQuestGUI.Models;

namespace DiscordQuestGUI.Services;

public class QuestProgressEventArgs : EventArgs
{
    public string QuestId { get; set; } = string.Empty;
    public int Percentage { get; set; }
    public int SecondsRemaining { get; set; }
    public bool IsCompleted { get; set; }
    public string? ErrorMessage { get; set; }
}

public class QuestWorkerService
{
    private readonly DiscordApiService _apiService;
    private CancellationTokenSource? _cts;
    private bool _isCancelling;

    public event EventHandler<QuestProgressEventArgs>? ProgressChanged;

    public QuestWorkerService(DiscordApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task StartQuestAsync(Quest quest, CancellationToken cancellationToken = default)
    {
        LogDebug($"StartQuestAsync called for quest: {quest.Id}");
        
        // Dispose previous CTS if exists
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isCancelling = false;

        try
        {
            var taskConfig = quest.Config?.TaskConfigV2 ?? quest.Config?.TaskConfig;
            LogDebug($"TaskConfig: {(taskConfig == null ? "null" : "exists")}, Tasks: {(taskConfig?.Tasks == null ? "null" : taskConfig.Tasks.Count.ToString())}");
            
            if (taskConfig == null || taskConfig.Tasks == null)
            {
                RaiseProgressChanged(quest.Id, 0, 0, false, "No task config found");
                return;
            }

            // Find supported task type
            var supportedTasks = new[] { "WATCH_VIDEO", "WATCH_VIDEO_ON_MOBILE", "PLAY_ACTIVITY", "PLAY_ON_DESKTOP", "STREAM_ON_DESKTOP" };
            var taskName = supportedTasks.FirstOrDefault(t => taskConfig.Tasks.ContainsKey(t));
            LogDebug($"Found task: {taskName ?? "none"}, Available tasks: {string.Join(", ", taskConfig.Tasks.Keys)}");

            if (taskName == null)
            {
                RaiseProgressChanged(quest.Id, 0, 0, false, "No supported task found");
                return;
            }

            var taskInfo = taskConfig.Tasks[taskName];
            var secondsNeeded = taskInfo.Target;
            LogDebug($"Task {taskName} needs {secondsNeeded} seconds");

            switch (taskName)
            {
                case "WATCH_VIDEO":
                case "WATCH_VIDEO_ON_MOBILE":
                    await HandleVideoQuestAsync(quest, secondsNeeded, _cts.Token);
                    break;

                case "PLAY_ACTIVITY":
                    await HandleActivityQuestAsync(quest, secondsNeeded, _cts.Token);
                    break;

                case "PLAY_ON_DESKTOP":
                    await HandleDesktopGameQuestAsync(quest, secondsNeeded, _cts.Token);
                    break;

                case "STREAM_ON_DESKTOP":
                    await HandleStreamQuestAsync(quest, secondsNeeded, _cts.Token);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Only report if we initiated the cancel
            if (_isCancelling)
            {
                // Don't raise error - just silently stop
            }
        }
        catch (ObjectDisposedException)
        {
            // CTS was disposed, ignore
        }
        catch (Exception ex)
        {
            RaiseProgressChanged(quest.Id, 0, 0, false, ex.Message);
        }
    }

    private async Task HandleVideoQuestAsync(Quest quest, int secondsNeeded, CancellationToken ct)
    {
        LogDebug($"HandleVideoQuestAsync started for quest {quest.Id}, need {secondsNeeded}s");
        
        const int maxFuture = 10;
        const int speed = 7;
        const int interval = 1;

        var enrolledAt = quest.UserStatus?.EnrolledAt ?? DateTime.UtcNow;
        var secondsDone = quest.UserStatus?.Progress?.GetValueOrDefault("WATCH_VIDEO")?.Value 
            ?? quest.UserStatus?.Progress?.GetValueOrDefault("WATCH_VIDEO_ON_MOBILE")?.Value 
            ?? 0;
        var completed = false;
        
        LogDebug($"Initial progress: {secondsDone}s done, enrolledAt: {enrolledAt}");
        
        // Send initial progress to UI immediately
        var initialPercentage = (int)((double)secondsDone / secondsNeeded * 100);
        RaiseProgressChanged(quest.Id, initialPercentage, secondsNeeded - secondsDone, false);

        while (!ct.IsCancellationRequested && secondsDone < secondsNeeded)
        {
            try
            {
                var maxAllowed = (int)(DateTime.UtcNow - enrolledAt).TotalSeconds + maxFuture;
                var diff = maxAllowed - secondsDone;
                var timestamp = secondsDone + speed;

                LogDebug($"Loop: maxAllowed={maxAllowed}, diff={diff}, timestamp={timestamp}");

                if (diff >= speed)
                {
                    LogDebug($"Sending video progress: {Math.Min(secondsNeeded, timestamp)}");
                    var response = await _apiService.SendVideoProgressAsync(
                        quest.Id,
                        Math.Min(secondsNeeded, timestamp + Random.Shared.NextDouble()),
                        ct
                    );

                    completed = response.CompletedAt != null;
                    secondsDone = Math.Min(secondsNeeded, timestamp);

                    var percentage = (int)((double)secondsDone / secondsNeeded * 100);
                    var remaining = secondsNeeded - secondsDone;
                    LogDebug($"API response received, completed={completed}, percentage={percentage}%");
                    RaiseProgressChanged(quest.Id, percentage, remaining, completed);

                    if (completed) break;
                }

                await Task.Delay(interval * 1000, ct);
            }
            catch (OperationCanceledException)
            {
                LogDebug("Operation cancelled");
                break;
            }
            catch (Exception ex)
            {
                LogDebug($"Error in loop: {ex.Message}");
                throw;
            }
        }

        if (!completed && !ct.IsCancellationRequested)
        {
            LogDebug("Sending final progress");
            await _apiService.SendVideoProgressAsync(quest.Id, secondsNeeded, ct);
            RaiseProgressChanged(quest.Id, 100, 0, true);
        }
        
        LogDebug($"HandleVideoQuestAsync finished, completed={completed}");
    }

    private async Task HandleActivityQuestAsync(Quest quest, int secondsNeeded, CancellationToken ct)
    {
        LogDebug($"HandleActivityQuestAsync started for quest {quest.Id}");
        
        var streamKey = "call:0:1";
        var secondsDone = quest.UserStatus?.Progress?.GetValueOrDefault("PLAY_ACTIVITY")?.Value ?? 0;
        
        // Send initial progress to UI
        var initialPercentage = (int)((double)secondsDone / secondsNeeded * 100);
        RaiseProgressChanged(quest.Id, initialPercentage, secondsNeeded - secondsDone, false);

        // FIX: Continue loop until API confirms completion or cancelled
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var heartbeatRequest = new HeartbeatRequest
                {
                    StreamKey = streamKey,
                    Terminal = false
                };

                LogDebug($"Sending Activity Heartbeat for {quest.Id}: StreamKey={heartbeatRequest.StreamKey}");

                var response = await _apiService.SendHeartbeatAsync(quest.Id, heartbeatRequest, ct);
                
                // Check if completed via API
                var completed = response.UserStatus?.CompletedAt != null;
                if (completed)
                {
                    LogDebug($"Quest {quest.Id} marked completed by API (Activity)");
                    // Send terminal heartbeat
                    heartbeatRequest.Terminal = true;
                    await _apiService.SendHeartbeatAsync(quest.Id, heartbeatRequest, ct);
                    RaiseProgressChanged(quest.Id, 100, 0, true);
                    break;
                }
                
                var newProgress = response.UserStatus?.Progress?.GetValueOrDefault("PLAY_ACTIVITY")?.Value ?? 0;
                LogDebug($"Activity Heartbeat response for {quest.Id}: API Progress={newProgress}, Local={secondsDone}");

                if (newProgress > secondsDone)
                {
                    secondsDone = newProgress;
                }
                else
                {
                    secondsDone += 20; // Activity uses 20s interval
                }

                var displaySeconds = Math.Min(secondsDone, secondsNeeded);
                var percentage = (int)((double)displaySeconds / secondsNeeded * 100);
                var remaining = Math.Max(0, secondsNeeded - displaySeconds);

                RaiseProgressChanged(quest.Id, percentage, remaining, false);

                // FIX: Polling for Activity quests
                if (secondsDone >= secondsNeeded - 40)
                {
                    LogDebug($"Activity Quest {quest.Id} check ({secondsDone}/{secondsNeeded}), polling API...");
                    try 
                    {
                        var (available, accepted, finished) = await _apiService.GetQuestsAsync();
                        var remoteQuest = finished.FirstOrDefault(q => q.Id == quest.Id) 
                                       ?? accepted.FirstOrDefault(q => q.Id == quest.Id);
                        
                        if (remoteQuest?.UserStatus?.CompletedAt != null)
                        {
                            LogDebug($"Activity Quest {quest.Id} confirmed completed by Polling");
                            // Send terminal heartbeat
                            heartbeatRequest.Terminal = true;
                            await _apiService.SendHeartbeatAsync(quest.Id, heartbeatRequest, ct);
                            RaiseProgressChanged(quest.Id, 100, 0, true);
                            break;
                        }
                    }
                    catch (Exception ex) { LogDebug($"Polling failed: {ex.Message}"); }
                }

                await Task.Delay(20 * 1000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task HandleDesktopGameQuestAsync(Quest quest, int secondsNeeded, CancellationToken ct)
    {
        LogDebug($"HandleDesktopGameQuestAsync started for quest {quest.Id}");
        
        var pid = Random.Shared.Next(1000, 30000);
        
        // Get initial progress - check both possible keys
        var initialProgress = quest.UserStatus?.Progress?.GetValueOrDefault("PLAY_ON_DESKTOP")?.Value 
            ?? quest.UserStatus?.StreamProgressSeconds 
            ?? 0;
        var secondsDone = initialProgress;
        
        LogDebug($"Initial progress: {secondsDone}s / {secondsNeeded}s");
        
        // Send initial progress to UI
        var initialPercentage = (int)((double)secondsDone / secondsNeeded * 100);
        RaiseProgressChanged(quest.Id, initialPercentage, secondsNeeded - secondsDone, false);

        // FIX: Continue loop until API confirms completion or cancelled
        // Do NOT break just because secondsDone >= secondsNeeded locally
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var heartbeatRequest = new HeartbeatRequest
                {
                    ApplicationId = quest.Config?.Application?.Id,
                    Pid = pid
                };
                
                LogDebug($"Sending Heartbeat for {quest.Id}: AppId={heartbeatRequest.ApplicationId}, Pid={heartbeatRequest.Pid}");

                var response = await _apiService.SendHeartbeatAsync(quest.Id, heartbeatRequest, ct);
                
                // Check if quest completed via API response
                var completed = response.UserStatus?.CompletedAt != null;
                if (completed)
                {
                    LogDebug($"Quest {quest.Id} marked completed by API");
                    RaiseProgressChanged(quest.Id, 100, 0, true);
                    break;
                }

                // Get progress from response - check multiple possible locations
                var newProgress = response.UserStatus?.Progress?.GetValueOrDefault("PLAY_ON_DESKTOP")?.Value
                    ?? response.UserStatus?.StreamProgressSeconds;
                
                LogDebug($"Heartbeat response for {quest.Id}: API Progress={newProgress}, Local={secondsDone}");
                
                // Update local progress with API truth if available and higher
                if (newProgress.HasValue && newProgress.Value > secondsDone)
                {
                    secondsDone = newProgress.Value;
                }
                else
                {
                    // Increment locally if API is lagging, but blindly trusting API 0 when we know we worked is bad.
                    // However, we shouldn't increment indefinitely if API is stuck.
                    // For now, we assume heartbeat works and increment local time for UI feedback.
                    secondsDone += 30;
                }
                
                // Calculate display values
                // Cap effective progress at secondsNeeded for UI calculation so we don't show 105%
                var displaySeconds = Math.Min(secondsDone, secondsNeeded);
                var percentage = (int)((double)displaySeconds / secondsNeeded * 100);
                var remaining = Math.Max(0, secondsNeeded - displaySeconds);

                RaiseProgressChanged(quest.Id, percentage, remaining, false);

                // FIX: If we think we are done (or close), actively poll the API to confirm
                // Heartbeat response 'UserStatus' is often stale regarding completion
                if (secondsDone >= secondsNeeded - 60) // Check if within 60s of completion or over
                {
                    LogDebug($"Quest {quest.Id} near completion ({secondsDone}/{secondsNeeded}), polling API for confirmation...");
                    try 
                    {
                        var (available, accepted, finished) = await _apiService.GetQuestsAsync();
                        var remoteQuest = finished.FirstOrDefault(q => q.Id == quest.Id) 
                                       ?? accepted.FirstOrDefault(q => q.Id == quest.Id);
                        
                        if (remoteQuest?.UserStatus?.CompletedAt != null)
                        {
                            LogDebug($"Quest {quest.Id} confirmed completed by Polling");
                            RaiseProgressChanged(quest.Id, 100, 0, true);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Polling failed: {ex.Message}");
                    }
                }

                await Task.Delay(30 * 1000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task HandleStreamQuestAsync(Quest quest, int secondsNeeded, CancellationToken ct)
    {
        LogDebug($"HandleStreamQuestAsync started for quest {quest.Id}");
        
        var pid = Random.Shared.Next(1000, 30000);
        var secondsDone = quest.UserStatus?.Progress?.GetValueOrDefault("STREAM_ON_DESKTOP")?.Value 
            ?? quest.UserStatus?.StreamProgressSeconds 
            ?? 0;
        
        // Send initial progress to UI
        var initialPercentage = (int)((double)secondsDone / secondsNeeded * 100);
        RaiseProgressChanged(quest.Id, initialPercentage, secondsNeeded - secondsDone, false);

        // FIX: Continue loop until API confirms completion or cancelled
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var heartbeatRequest = new HeartbeatRequest
                {
                    ApplicationId = quest.Config?.Application?.Id,
                    StreamKey = "guild:0:0:0",
                    Pid = pid
                };

                LogDebug($"Sending Stream Heartbeat for {quest.Id}: AppId={heartbeatRequest.ApplicationId}, StreamKey={heartbeatRequest.StreamKey}");

                var response = await _apiService.SendHeartbeatAsync(quest.Id, heartbeatRequest, ct);
                
                // Check if completed via API
                var completed = response.UserStatus?.CompletedAt != null;
                if (completed)
                {
                    LogDebug($"Quest {quest.Id} marked completed by API (Stream)");
                    RaiseProgressChanged(quest.Id, 100, 0, true);
                    break;
                }

                var newProgress = quest.Config?.ConfigVersion == 1
                    ? response.UserStatus?.StreamProgressSeconds ?? 0
                    : response.UserStatus?.Progress?.GetValueOrDefault("STREAM_ON_DESKTOP")?.Value ?? 0;

                LogDebug($"Stream Heartbeat response for {quest.Id}: API Progress={newProgress}, Local={secondsDone}");

                if (newProgress > secondsDone)
                {
                    secondsDone = newProgress;
                }
                else
                {
                    secondsDone += 30;
                }

                // Cap effective progress
                var displaySeconds = Math.Min(secondsDone, secondsNeeded);
                var percentage = (int)((double)displaySeconds / secondsNeeded * 100);
                var remaining = Math.Max(0, secondsNeeded - displaySeconds);

                RaiseProgressChanged(quest.Id, percentage, remaining, false);

                // FIX: Polling for Stream quests
                if (secondsDone >= secondsNeeded - 60)
                {
                    LogDebug($"Stream Quest {quest.Id} check ({secondsDone}/{secondsNeeded}), polling API...");
                    try 
                    {
                        var (available, accepted, finished) = await _apiService.GetQuestsAsync();
                        var remoteQuest = finished.FirstOrDefault(q => q.Id == quest.Id) 
                                       ?? accepted.FirstOrDefault(q => q.Id == quest.Id);
                        
                        if (remoteQuest?.UserStatus?.CompletedAt != null)
                        {
                            LogDebug($"Stream Quest {quest.Id} confirmed completed by Polling");
                            RaiseProgressChanged(quest.Id, 100, 0, true);
                            break;
                        }
                    }
                    catch (Exception ex) { LogDebug($"Polling failed: {ex.Message}"); }
                }

                await Task.Delay(30 * 1000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void RaiseProgressChanged(string questId, int percentage, int secondsRemaining, bool isCompleted, string? errorMessage = null)
    {
        LogDebug($"RaiseProgressChanged: questId={questId}, percentage={percentage}, remaining={secondsRemaining}, completed={isCompleted}, error={errorMessage}");
        ProgressChanged?.Invoke(this, new QuestProgressEventArgs
        {
            QuestId = questId,
            Percentage = percentage,
            SecondsRemaining = secondsRemaining,
            IsCompleted = isCompleted,
            ErrorMessage = errorMessage
        });
    }
    
    private void LogDebug(string message)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiscordQuestGUI",
                "quest_worker.log"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    public void Cancel()
    {
        try
        {
            _isCancelling = true;
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, ignore
        }
    }
}
