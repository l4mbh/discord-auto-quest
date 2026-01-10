using DiscordQuestGUI.Models;

namespace DiscordQuestGUI.Forms;

// Partial class for history quests functionality (all completed quests - lazy loaded)
public partial class MainForm
{
    private async Task LoadHistoryQuestsAsync(int page = 0, int pageSize = 10)
    {
        try
        {
            LogError($"LoadHistoryQuestsAsync started - page: {page}, pageSize: {pageSize}");
            
            if (_apiService == null)
            {
                SendMessageToJS("historyQuestsLoaded", new { quests = Array.Empty<object>(), hasMore = false, total = 0 });
                return;
            }
            
            if (page == 0)
            {
                SendLoadingState(true, "Loading quest history...");
            }

            // Get all completed quests (including claimed) and paginate client-side
            var allHistory = await _apiService.GetCompletedQuestsAsync();
            var totalCount = allHistory.Count;
            var pagedQuests = allHistory.Skip(page * pageSize).Take(pageSize).ToList();
            var hasMore = (page + 1) * pageSize < totalCount;
            
            LogError($"Fetched {pagedQuests.Count} history quests (page {page}), hasMore: {hasMore}, total: {totalCount}");

            SendLoadingState(false, "");

            // Send to UI with pagination info
            var result = new
            {
                quests = SerializeQuests(pagedQuests),
                hasMore = hasMore,
                total = totalCount
            };
            
            SendMessageToJS("historyQuestsLoaded", result);
            LogError("History quests sent to UI");
        }
        catch (Exception ex)
        {
            LogError($"LoadHistoryQuestsAsync error: {ex}");
            SendLoadingState(false, "");
            // Send empty result with hasMore = false to stop infinite loading
            SendMessageToJS("historyQuestsLoaded", new { quests = Array.Empty<object>(), hasMore = false, total = 0 });
            SendToast($"Failed to load quest history: {ex.Message}", "error");
        }
    }
}
