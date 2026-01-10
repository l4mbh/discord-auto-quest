using System.Text.Json.Serialization;

namespace DiscordQuestGUI.Models;

public class Quest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("config")]
    public QuestConfig Config { get; set; } = new();

    [JsonPropertyName("user_status")]
    public UserStatus? UserStatus { get; set; }
}

public class QuestConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("config_version")]
    public int ConfigVersion { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }
    
    [JsonPropertyName("starts_at")]
    public DateTimeOffset StartsAt { get; set; }

    [JsonPropertyName("application")]
    public Application Application { get; set; } = new();

    [JsonPropertyName("messages")]
    public Messages Messages { get; set; } = new();
    
    [JsonPropertyName("assets")]
    public QuestAssets? Assets { get; set; }
    
    [JsonPropertyName("rewards_config")]
    public RewardsConfig? RewardsConfig { get; set; }

    [JsonPropertyName("task_config")]
    public TaskConfig? TaskConfig { get; set; }

    [JsonPropertyName("task_config_v2")]
    public TaskConfig? TaskConfigV2 { get; set; }
}

public class QuestAssets
{
    [JsonPropertyName("hero")]
    public string? Hero { get; set; }
    
    [JsonPropertyName("hero_video")]
    public string? HeroVideo { get; set; }
    
    [JsonPropertyName("quest_bar_hero")]
    public string? QuestBarHero { get; set; }
    
    [JsonPropertyName("quest_bar_hero_video")]
    public string? QuestBarHeroVideo { get; set; }
    
    [JsonPropertyName("game_tile")]
    public string? GameTile { get; set; }
    
    [JsonPropertyName("logotype")]
    public string? Logotype { get; set; }
    
    [JsonPropertyName("game_tile_light")]
    public string? GameTileLight { get; set; }
    
    [JsonPropertyName("game_tile_dark")]
    public string? GameTileDark { get; set; }
}

public class RewardsConfig
{
    [JsonPropertyName("assignment_method")]
    public int? AssignmentMethod { get; set; }
    
    [JsonPropertyName("rewards")]
    public List<Reward>? Rewards { get; set; }
    
    [JsonPropertyName("rewards_expire_at")]
    public DateTimeOffset? RewardsExpireAt { get; set; }
}

public class Reward
{
    [JsonPropertyName("type")]
    public int Type { get; set; }
    
    [JsonPropertyName("sku_id")]
    public string? SkuId { get; set; }
    
    [JsonPropertyName("asset")]
    public string? Asset { get; set; }
    
    [JsonPropertyName("asset_video")]
    public string? AssetVideo { get; set; }
    
    [JsonPropertyName("messages")]
    public RewardMessages? Messages { get; set; }
    
    [JsonPropertyName("orb_quantity")]
    public int? OrbQuantity { get; set; }
}

public class RewardMessages
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("name_with_article")]
    public string? NameWithArticle { get; set; }
}

public class Application
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("link")]
    public string? Link { get; set; }
}

public class Messages
{
    [JsonPropertyName("quest_name")]
    public string QuestName { get; set; } = string.Empty;

    [JsonPropertyName("game_title")]
    public string? GameTitle { get; set; }
    
    [JsonPropertyName("game_publisher")]
    public string? GamePublisher { get; set; }
}

public class TaskConfig
{
    [JsonPropertyName("tasks")]
    public Dictionary<string, TaskInfo> Tasks { get; set; } = new();
    
    [JsonPropertyName("join_operator")]
    public string? JoinOperator { get; set; }
}

public class TaskInfo
{
    [JsonPropertyName("target")]
    public int Target { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("event_name")]
    public string? EventName { get; set; }
}

public class UserStatus
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

    [JsonPropertyName("progress")]
    public Dictionary<string, ProgressInfo>? Progress { get; set; }

    [JsonPropertyName("stream_progress_seconds")]
    public int? StreamProgressSeconds { get; set; }
}

public class ProgressInfo
{
    [JsonPropertyName("value")]
    public int Value { get; set; }
    
    [JsonPropertyName("event_name")]
    public string? EventName { get; set; }
    
    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }
    
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}
