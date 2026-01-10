using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DiscordQuestGUI.Models;
using System;

namespace DiscordQuestGUI.Services;

public class ConfigService
{
    private readonly string _configPath;
    private static readonly byte[] _key = Encoding.UTF8.GetBytes("DiscordQuest2024"); // Exactly 16 bytes for AES-128
    private static readonly byte[] _iv = Encoding.UTF8.GetBytes("QuestAutoComp16!"); // Exactly 16 bytes IV

    public ConfigService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiscordQuestGUI"
        );
        Directory.CreateDirectory(appDataPath);
        _configPath = Path.Combine(appDataPath, "settings.json");
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(_configPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_configPath, json);
    }

    // Legacy methods for backward compatibility
    public void SaveToken(string token)
    {
        var settings = LoadSettings();

        // If there's a current account, update it; otherwise create a new one
        var currentAccount = settings.Accounts?.FirstOrDefault(a => a.IsActive);
        if (currentAccount != null)
        {
            currentAccount.EncryptedToken = EncryptToken(token);
            currentAccount.LastLogin = DateTime.Now;
        }
        else
        {
            // Create a new account with placeholder data - will be updated when user info is fetched
            var account = new UserAccount
            {
                Id = Guid.NewGuid().ToString(),
                Username = "Unknown",
                Discriminator = "0000",
                Avatar = "",
                EncryptedToken = EncryptToken(token),
                IsActive = true
            };
            settings.Accounts ??= new List<UserAccount>();
            settings.Accounts.Add(account);
            settings.CurrentAccountId = account.Id;
        }

        SaveSettings(settings);
    }

    public string? GetDecryptedToken()
    {
        var settings = LoadSettings();
        var currentAccount = settings.Accounts?.FirstOrDefault(a => a.IsActive);
        if (currentAccount == null || string.IsNullOrEmpty(currentAccount.EncryptedToken))
            return null;

        try
        {
            return DecryptToken(currentAccount.EncryptedToken);
        }
        catch
        {
            return null;
        }
    }

    public void ClearToken()
    {
        var settings = LoadSettings();
        var currentAccount = settings.Accounts?.FirstOrDefault(a => a.IsActive);
        if (currentAccount != null)
        {
            settings.Accounts.Remove(currentAccount);
            if (settings.Accounts.Any())
            {
                settings.CurrentAccountId = settings.Accounts[0].Id;
                settings.Accounts[0].IsActive = true;
            }
            else
            {
                settings.CurrentAccountId = null;
            }
        }
        SaveSettings(settings);
    }

    public void SaveCookies(string? cookies)
    {
        if (string.IsNullOrEmpty(cookies)) return;

        var settings = LoadSettings();
        var currentAccount = settings.Accounts?.FirstOrDefault(a => a.IsActive);
        if (currentAccount != null)
        {
            currentAccount.EncryptedCookies = EncryptToken(cookies);
            SaveSettings(settings);
        }
    }

    public string? GetDecryptedCookies()
    {
        var settings = LoadSettings();
        var currentAccount = settings.Accounts?.FirstOrDefault(a => a.IsActive);
        if (currentAccount == null || string.IsNullOrEmpty(currentAccount.EncryptedCookies))
            return null;

        try
        {
            return DecryptToken(currentAccount.EncryptedCookies);
        }
        catch
        {
            return null;
        }
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
