using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;
using DiscordQuestGUI.Models;
using System.Linq;

namespace DiscordQuestGUI.Services;

public class NotificationService
{
    public void ShowCompletionNotification(Quest quest)
    {
        try
        {
            var rewardText = "Reward available";
            var rewardIcon = "";

            if (quest.Config?.RewardsConfig?.Rewards != null)
            {
                var orbReward = quest.Config.RewardsConfig.Rewards.FirstOrDefault(r => r.OrbQuantity > 0);
                if (orbReward != null)
                {
                    rewardText = $"{orbReward.OrbQuantity} Orbs";
                    rewardIcon = "https://cdn.discordapp.com/assets/content/39957396a3a99aa230ad8b925b03ccdf974e156a75357df8491e577903c1b782.png";
                }
                else
                {
                    var itemReward = quest.Config.RewardsConfig.Rewards.FirstOrDefault();
                    if (itemReward != null)
                    {
                        rewardText = itemReward.Messages?.Name ?? "In-Game Reward";
                        if (!string.IsNullOrEmpty(itemReward.Asset))
                        {
                            rewardIcon = $"https://cdn.discordapp.com/assets/quests/{quest.Id}/{itemReward.Asset}.png";
                        }
                    }
                }
            }

            var builder = new ToastContentBuilder()
                .AddText("Quest Completed! 🎉")
                .AddText(quest.Config?.Messages?.GameTitle ?? "Discord Quest")
                .AddText(rewardText);

            if (!string.IsNullOrEmpty(quest.Config?.Assets?.Hero))
            {
                var heroUrl = $"https://cdn.discordapp.com/assets/quests/{quest.Id}/{quest.Config.Assets.Hero}.png";
                builder.AddHeroImage(new Uri(heroUrl));
            }

            if (!string.IsNullOrEmpty(rewardIcon))
            {
                builder.AddAppLogoOverride(new Uri(rewardIcon), ToastGenericAppLogoCrop.Circle);
            }

            builder.Show();
        }
        catch (Exception ex)
        {
            // Fallback
            MessageBox.Show($"Quest {quest.Config?.Messages?.GameTitle} Completed!", "Quest Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    public void ShowNotification(string title, string message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
        }
        catch
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    public void ClearNotifications()
    {
        try
        {
            ToastNotificationManagerCompat.History.Clear();
        }
        catch { }
    }

    public void ShowAllCompletedNotification(int count)
    {
        try
        {
            new ToastContentBuilder()
                .AddText("All Quests Completed! 🏆")
                .AddText($"Finished {count} quest(s) successfully.")
                .AddText("Time to claim your rewards!")
                .Show();
        }
        catch
        {
            MessageBox.Show($"Completed {count} quest(s)! Time to claim rewards.", "All Quests Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
