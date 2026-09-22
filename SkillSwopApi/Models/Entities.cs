namespace SkillSwopApi.Models;

// These entities mirror the Kotlin data classes in the Android app's Models.kt exactly
// (same field names) so the JSON round-trips without any mapping layer. Program.cs
// configures System.Text.Json to use camelCase globally, which turns e.g. ListingId
// into "listingId" on the wire, matching what Retrofit/Gson expects on the Android side.

public class UserEntity
{
    public string UserId { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string AreaSuburb { get; set; } = "";
    public string PreferredLanguage { get; set; } = "en";
    public double TrustScore { get; set; } = 4.0;
    public string BadgeTier { get; set; } = "NONE";
    public bool BiometricEnabled { get; set; } = false;
    public bool NotificationsEnabled { get; set; } = true;
    public string ThemePreference { get; set; } = "system";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SkillCategoryEntity
{
    public string CategoryId { get; set; } = Guid.NewGuid().ToString();
    public string NameEn { get; set; } = "";
    public string NameZu { get; set; } = "";
    public string NameAf { get; set; } = "";
}

public class SkillListingEntity
{
    public string ListingId { get; set; } = Guid.NewGuid().ToString();
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string Type { get; set; } = "OFFER"; // OFFER | REQUEST
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public string AreaSuburb { get; set; } = "";
    public string Status { get; set; } = "ACTIVE"; // ACTIVE | CLOSED
    public int SyncVersion { get; set; } = 1;
    public bool IsPendingSync { get; set; } = false;
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
}

public class ExchangeEntity
{
    public string ExchangeId { get; set; } = Guid.NewGuid().ToString();
    public string ListingId { get; set; } = "";
    public string ListingTitle { get; set; } = "";
    public string RequesterId { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string Status { get; set; } = "PENDING"; // PENDING | ACCEPTED | DECLINED | COMPLETED
    public int SyncVersion { get; set; } = 1;
    public bool IsPendingSync { get; set; } = false;
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
    public string? CompletedAt { get; set; }
}

public class CoordinationMessageEntity
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public string ExchangeId { get; set; } = "";
    public string SenderId { get; set; } = "";
    public string Body { get; set; } = "";
    public string SentAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
}

public class RatingEntity
{
    public string RatingId { get; set; } = Guid.NewGuid().ToString();
    public string ExchangeId { get; set; } = "";
    public string RaterId { get; set; } = "";
    public string TargetUserId { get; set; } = "";
    public int Score { get; set; }
    public string? Comment { get; set; }
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
}

// --- Response-shaping DTOs (not stored, just what the endpoints hand back) ---

public class FeedResponse
{
    public List<SkillListingEntity> Items { get; set; } = new();
    public int Page { get; set; }
    public int TotalPages { get; set; }
}

public class AuthResponse
{
    public UserEntity User { get; set; } = new();
    public bool IsNewUser { get; set; }
}

public class AreaImpactResponse
{
    public string Area { get; set; } = "";
    public int ExchangesThisMonth { get; set; }
    public string TopCategory { get; set; } = "";
    public int EstHoursExchanged { get; set; }
}
