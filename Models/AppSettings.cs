namespace DiscordQuestGUI.Models;

public class AppSettings
{
    public List<UserAccount> Accounts { get; set; } = new();
    public string? CurrentAccountId { get; set; }
    public bool EnableNotifications { get; set; } = true;
    public int AutoRefreshInterval { get; set; } = 5; // minutes
    public bool MinimizeToTray { get; set; } = true; // Default: enabled
}

public class UserAccount
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Discriminator { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string EncryptedToken { get; set; } = string.Empty;
    public string? EncryptedCookies { get; set; }
    public DateTime LastLogin { get; set; } = DateTime.Now;
    public bool IsActive { get; set; }
}
