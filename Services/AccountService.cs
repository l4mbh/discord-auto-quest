using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DiscordQuestGUI.Models;

namespace DiscordQuestGUI.Services;

public class AccountService
{
    private readonly ConfigService _configService;
    private static readonly byte[] _key = Encoding.UTF8.GetBytes("DiscordQuest2024"); // Exactly 16 bytes for AES-128
    private static readonly byte[] _iv = Encoding.UTF8.GetBytes("QuestAutoComp16!"); // Exactly 16 bytes IV

    public AccountService(ConfigService configService)
    {
        _configService = configService;
    }

    public List<UserAccount> GetAllAccounts()
    {
        var settings = _configService.LoadSettings();
        return settings.Accounts ?? new List<UserAccount>();
    }

    public UserAccount? GetCurrentAccount()
    {
        var settings = _configService.LoadSettings();
        if (string.IsNullOrEmpty(settings.CurrentAccountId))
            return null;

        return settings.Accounts?.FirstOrDefault(a => a.Id == settings.CurrentAccountId);
    }

    public void AddAccount(UserAccount account)
    {
        var settings = _configService.LoadSettings();

        // Ensure accounts list exists
        settings.Accounts ??= new List<UserAccount>();

        // Deactivate all existing accounts first
        foreach (var existingAcc in settings.Accounts)
        {
            existingAcc.IsActive = false;
        }

        // Check if account already exists
        var existingAccount = settings.Accounts.FirstOrDefault(a => a.Id == account.Id);
        if (existingAccount != null)
        {
            // Update existing account
            existingAccount.Username = account.Username;
            existingAccount.Discriminator = account.Discriminator;
            existingAccount.Avatar = account.Avatar;
            existingAccount.EncryptedToken = account.EncryptedToken;
            existingAccount.EncryptedCookies = account.EncryptedCookies;
            existingAccount.LastLogin = DateTime.Now;
            existingAccount.IsActive = true; // Reactivate this account
        }
        else
        {
            // Add new account
            account.LastLogin = DateTime.Now;
            account.IsActive = true; // This is the new active account
            settings.Accounts.Add(account);
        }

        // Set as current account
        settings.CurrentAccountId = account.Id;

        _configService.SaveSettings(settings);
    }

    public void SwitchToAccount(string accountId)
    {
        var settings = _configService.LoadSettings();

        // Deactivate all accounts
        foreach (var account in settings.Accounts)
        {
            account.IsActive = false;
        }

        // Activate selected account
        var targetAccount = settings.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (targetAccount != null)
        {
            targetAccount.IsActive = true;
            targetAccount.LastLogin = DateTime.Now;
            settings.CurrentAccountId = accountId;
        }

        _configService.SaveSettings(settings);
    }

    public void RemoveAccount(string accountId)
    {
        var settings = _configService.LoadSettings();

        var accountToRemove = settings.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (accountToRemove != null)
        {
            settings.Accounts.Remove(accountToRemove);

            // If we removed the current account, switch to the first available account
            if (settings.CurrentAccountId == accountId && settings.Accounts.Any())
            {
                settings.CurrentAccountId = settings.Accounts[0].Id;
                settings.Accounts[0].IsActive = true;
            }
            else if (!settings.Accounts.Any())
            {
                settings.CurrentAccountId = null;
            }
        }

        _configService.SaveSettings(settings);
    }

    public (string? token, string? cookies) GetCurrentAccountCredentials()
    {
        var currentAccount = GetCurrentAccount();
        if (currentAccount == null)
            return (null, null);

        try
        {
            var token = DecryptToken(currentAccount.EncryptedToken);
            var cookies = string.IsNullOrEmpty(currentAccount.EncryptedCookies)
                ? null
                : DecryptToken(currentAccount.EncryptedCookies);
            return (token, cookies);
        }
        catch
        {
            return (null, null);
        }
    }

    public void UpdateCurrentAccountCredentials(string token, string? cookies = null)
    {
        var currentAccount = GetCurrentAccount();
        if (currentAccount == null)
            return;

        currentAccount.EncryptedToken = EncryptToken(token);
        if (!string.IsNullOrEmpty(cookies))
        {
            currentAccount.EncryptedCookies = EncryptToken(cookies);
        }
        currentAccount.LastLogin = DateTime.Now;

        var settings = _configService.LoadSettings();
        _configService.SaveSettings(settings);
    }

    private string EncryptToken(string token)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;

        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
        using var sw = new StreamWriter(cs);
        sw.Write(token);
        sw.Flush();
        cs.FlushFinalBlock();

        return Convert.ToBase64String(ms.ToArray());
    }

    private string DecryptToken(string encryptedToken)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;

        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        var buffer = Convert.FromBase64String(encryptedToken);

        using var ms = new MemoryStream(buffer);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);
        return sr.ReadToEnd();
    }
}
