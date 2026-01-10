using System.Text.Json.Serialization;

namespace DiscordQuestGUI.Models;

// API Response Models
public class QuestsResponse
{
    [JsonPropertyName("quests")]
    public List<Quest> Quests { get; set; } = new();
}

public class VideoProgressRequest
{
    [JsonPropertyName("timestamp")]
    public double Timestamp { get; set; }
}

public class VideoProgressResponse
{
    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }
}

public class HeartbeatRequest
{
    [JsonPropertyName("stream_key")]
    public string? StreamKey { get; set; }

    [JsonPropertyName("terminal")]
    public bool? Terminal { get; set; }

    [JsonPropertyName("application_id")]
    public string? ApplicationId { get; set; }

    [JsonPropertyName("pid")]
    public int? Pid { get; set; }
}

public class HeartbeatResponse
{
    [JsonPropertyName("user_status")]
    public UserStatus? UserStatus { get; set; }
}

public class EnrollQuestResponse
{
    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }
    
    [JsonPropertyName("quest_id")]
    public string? QuestId { get; set; }
    
    [JsonPropertyName("enrolled_at")]
    public DateTimeOffset? EnrolledAt { get; set; }
    
    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }
    
    [JsonPropertyName("claimed_at")]
    public DateTimeOffset? ClaimedAt { get; set; }
    
    [JsonPropertyName("stream_progress_seconds")]
    public int StreamProgressSeconds { get; set; }
}

// Result of claim attempt
public class ClaimQuestResult
{
    public bool Success { get; set; }
    public bool RequiresCaptcha { get; set; }
    public string Message { get; set; } = string.Empty;
    public CaptchaRequiredDetails? CaptchaDetails { get; set; }
}

// Captcha details when verification is required
// public class CaptchaRequiredDetails
// {
//     public string? Sitekey { get; set; }
//     public string? Service { get; set; } // e.g., "hcaptcha"
//     public string? SessionId { get; set; }
//     public string? Rqdata { get; set; }
// }

// Captcha tokens for solving verification
public class CaptchaTokens
{
    public string? CaptchaKey { get; set; } // JWT token from captcha service
    public string? CaptchaRqtoken { get; set; } // Request token
    public string? CaptchaSessionId { get; set; } // Session identifier
}

public class UserBalance
{
    [JsonPropertyName("amount")]
    public int Amount { get; set; }
    
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "Orbs";
}
