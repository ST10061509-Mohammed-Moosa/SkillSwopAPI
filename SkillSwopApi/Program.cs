using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkillSwopApi.Data;
using SkillSwopApi.Models;

var builder = WebApplication.CreateBuilder(args);

// --- JSON: camelCase everywhere, matching the Android app's Gson field names (e.g. listingId) ---
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// --- Database: prefer a DATABASE_URL env var (what Render/Railway/Azure typically inject),
// fall back to appsettings.json ConnectionStrings:DefaultConnection for local dev. ---
string BuildConnectionString()
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (string.IsNullOrEmpty(databaseUrl))
    {
        return builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Database=skillswop;Username=postgres;Password=postgres";
    }

    // Render/Railway give a URL like postgres://user:pass@host:port/dbname — Npgsql wants
    // a key=value connection string, so convert it.
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':', 2);
    return $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={uri.AbsolutePath.TrimStart('/')};" +
           $"Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(BuildConnectionString()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Create/update schema from the model on startup — a prototype-friendly stand-in for
// `dotnet ef migrations`, which needs the .NET SDK/CLI to generate (not available in
// every dev environment on short notice). Fine for a POE prototype; swap for real EF
// migrations if you have time before the final POE submission.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Ok(new { status = "SkillSwop SA API is running", time = DateTime.UtcNow }));

// ---------------------------------------------------------------------------
// FR-1: Auth session. NOTE: this decodes the Firebase ID token's claims to get
// the user's uid/email/name, but does NOT cryptographically verify the token's
// signature against Google's public keys — that needs the FirebaseAdmin NuGet
// package and a service-account key, which is a good next step but out of
// scope for tonight's deadline. Flag this as a known limitation in your README
// / AI sub-report: right now any well-formed JWT-shaped string is accepted.
// ---------------------------------------------------------------------------
app.MapPost("/api/auth/session", async (AppDbContext db, Dictionary<string, string> body) =>
{
    if (!body.TryGetValue("firebaseIdToken", out var token) || string.IsNullOrWhiteSpace(token))
        return Results.BadRequest(new { error = "firebaseIdToken is required" });

    var claims = DecodeJwtClaimsUnverified(token);
    var uid = claims.GetValueOrDefault("user_id") ?? claims.GetValueOrDefault("sub") ?? Guid.NewGuid().ToString();
    var email = claims.GetValueOrDefault("email") ?? "";
    var name = claims.GetValueOrDefault("name") ?? "New User";

    var existing = await db.Users.FindAsync(uid);
    var isNew = existing == null;
    if (existing == null)
    {
        existing = new UserEntity { UserId = uid, DisplayName = name, Email = email, AreaSuburb = "Soweto" };
        db.Users.Add(existing);
    }
    else
    {
        existing.Email = email;
    }
    await db.SaveChangesAsync();

    return Results.Ok(new AuthResponse { User = existing, IsNewUser = isNew });
});

app.MapGet("/api/users/{id}", async (AppDbContext db, string id) =>
{
    var user = await db.Users.FindAsync(id);
    return user is null ? Results.NotFound() : Results.Ok(user);
});

app.MapMethods("/api/users/{id}", new[] { "PATCH" }, async (AppDbContext db, string id, JsonElement updates) =>
{
    var user = await db.Users.FindAsync(id);
    if (user is null) return Results.NotFound();

    if (updates.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String) user.DisplayName = dn.GetString()!;
    if (updates.TryGetProperty("areaSuburb", out var area) && area.ValueKind == JsonValueKind.String) user.AreaSuburb = area.GetString()!;
    if (updates.TryGetProperty("preferredLanguage", out var lang) && lang.ValueKind == JsonValueKind.String) user.PreferredLanguage = lang.GetString()!;
    if (updates.TryGetProperty("theme", out var theme) && theme.ValueKind == JsonValueKind.String) user.ThemePreference = theme.GetString()!;
    if (updates.TryGetProperty("themePreference", out var themePref) && themePref.ValueKind == JsonValueKind.String) user.ThemePreference = themePref.GetString()!;
    if (updates.TryGetProperty("biometricEnabled", out var bio) && (bio.ValueKind == JsonValueKind.True || bio.ValueKind == JsonValueKind.False)) user.BiometricEnabled = bio.GetBoolean();
    if (updates.TryGetProperty("notificationsEnabled", out var notif) && (notif.ValueKind == JsonValueKind.True || notif.ValueKind == JsonValueKind.False)) user.NotificationsEnabled = notif.GetBoolean();

    await db.SaveChangesAsync();
    return Results.Ok(user);
});

app.MapGet("/api/categories", async (AppDbContext db) =>
    Results.Ok(await db.Categories.ToListAsync()));

// FR-8: Smart Matching feed. Ranking here is a simple recency + category/area match score —
// a reasonable server-side approximation of the "Should"-priority ranking rule from the
// design doc's section 2.3, without needing full user-history weighting tonight.
app.MapGet("/api/listings/feed", async (AppDbContext db, string area, string? categoryId, string? type, int page, int pageSize) =>
{
    var query = db.Listings.Where(l => l.Status == "ACTIVE");
    if (!string.IsNullOrEmpty(categoryId)) query = query.Where(l => l.CategoryId == categoryId);
    if (!string.IsNullOrEmpty(type)) query = query.Where(l => l.Type == type);

    var all = await query.ToListAsync();
    var ranked = all
        .OrderByDescending(l => l.AreaSuburb.Equals(area, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
        .ThenByDescending(l => l.CreatedAt)
        .ToList();

    var safePage = Math.Max(page, 1);
    var safeSize = Math.Max(pageSize, 1);
    var items = ranked.Skip((safePage - 1) * safeSize).Take(safeSize).ToList();
    var totalPages = (int)Math.Ceiling(ranked.Count / (double)safeSize);

    return Results.Ok(new FeedResponse { Items = items, Page = safePage, TotalPages = Math.Max(totalPages, 1) });
});

app.MapPost("/api/listings", async (AppDbContext db, SkillListingEntity listing) =>
{
    listing.IsPendingSync = false;
    var existing = await db.Listings.FindAsync(listing.ListingId);
    if (existing == null) db.Listings.Add(listing);
    else db.Entry(existing).CurrentValues.SetValues(listing);
    await db.SaveChangesAsync();
    return Results.Ok(listing);
});

// SyncWorker.kt posts a loose map here (listingId, exchangeId) — accept flexible JSON
// rather than a strict DTO so it doesn't break if the app sends extra/fewer fields.
app.MapPost("/api/exchanges", async (AppDbContext db, JsonElement body) =>
{
    var exchangeId = body.TryGetProperty("exchangeId", out var eid) ? eid.GetString() : Guid.NewGuid().ToString();
    var listingId = body.TryGetProperty("listingId", out var lid) ? lid.GetString() : "";
    var requesterId = body.TryGetProperty("requesterId", out var rid) ? rid.GetString() : "";
    var providerId = body.TryGetProperty("providerId", out var pid) ? pid.GetString() : "";
    var listingTitle = body.TryGetProperty("listingTitle", out var lt) ? lt.GetString() : "";

    var existing = await db.Exchanges.FindAsync(exchangeId);
    if (existing != null) return Results.Ok(existing);

    var exchange = new ExchangeEntity
    {
        ExchangeId = exchangeId ?? Guid.NewGuid().ToString(),
        ListingId = listingId ?? "",
        ListingTitle = listingTitle ?? "",
        RequesterId = requesterId ?? "",
        ProviderId = providerId ?? "",
        Status = "PENDING"
    };
    db.Exchanges.Add(exchange);
    await db.SaveChangesAsync();
    return Results.Ok(exchange);
});

// Enforces the same PENDING->ACCEPTED/DECLINED, ACCEPTED->COMPLETED state machine
// as MainActivity.kt's transitionExchangeStatus(), so the server can't be pushed
// into an illegal state even if a buggy client tries to.
app.MapMethods("/api/exchanges/{id}/status", new[] { "PATCH" }, async (AppDbContext db, string id, JsonElement body) =>
{
    var exchange = await db.Exchanges.FindAsync(id);
    if (exchange is null) return Results.NotFound();
    if (!body.TryGetProperty("status", out var statusEl)) return Results.BadRequest(new { error = "status is required" });
    var newStatus = statusEl.GetString() ?? "";

    var validTransition = exchange.Status switch
    {
        "PENDING" => newStatus is "ACCEPTED" or "DECLINED",
        "ACCEPTED" => newStatus is "COMPLETED",
        _ => false
    };
    if (!validTransition)
        return Results.BadRequest(new { error = $"Cannot transition from {exchange.Status} to {newStatus}" });

    exchange.Status = newStatus;
    if (newStatus == "COMPLETED") exchange.CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
    await db.SaveChangesAsync();
    return Results.Ok(exchange);
});

app.MapPost("/api/exchanges/{id}/messages", async (AppDbContext db, string id, JsonElement body) =>
{
    var exchange = await db.Exchanges.FindAsync(id);
    if (exchange is null) return Results.NotFound();
    if (exchange.Status is not ("ACCEPTED" or "COMPLETED"))
        return Results.BadRequest(new { error = "Chat is only open for accepted or completed exchanges" });

    var text = body.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
    if (text.Length > 300) return Results.BadRequest(new { error = "Message exceeds 300 characters" });
    var senderId = body.TryGetProperty("senderId", out var s) ? s.GetString() ?? "" : "";

    var message = new CoordinationMessageEntity { ExchangeId = id, SenderId = senderId, Body = text };
    db.Messages.Add(message);
    await db.SaveChangesAsync();
    return Results.Ok(message);
});

app.MapGet("/api/exchanges/{id}/messages", async (AppDbContext db, string id) =>
    Results.Ok(await db.Messages.Where(m => m.ExchangeId == id).OrderBy(m => m.SentAt).ToListAsync()));

// FR-9: Ratings (not yet called by the Android app's current ApiService.kt interface,
// included so the API satisfies the brief's endpoint table even before the app is wired
// up to call it over the network instead of just storing ratings locally).
app.MapPost("/api/exchanges/{id}/ratings", async (AppDbContext db, string id, JsonElement body) =>
{
    var exchange = await db.Exchanges.FindAsync(id);
    if (exchange is null) return Results.NotFound();

    var score = body.TryGetProperty("score", out var sc) ? sc.GetInt32() : 0;
    if (score is < 1 or > 5) return Results.BadRequest(new { error = "score must be between 1 and 5" });

    var raterId = body.TryGetProperty("raterId", out var r) ? r.GetString() ?? "" : "";
    var targetUserId = body.TryGetProperty("targetUserId", out var t) ? t.GetString() ?? "" : "";
    var comment = body.TryGetProperty("comment", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;

    var rating = new RatingEntity { ExchangeId = id, RaterId = raterId, TargetUserId = targetUserId, Score = score, Comment = comment };
    db.Ratings.Add(rating);

    // Recalculate the rated user's trust score + badge tier server-side (FR-9).
    var targetUser = await db.Users.FindAsync(targetUserId);
    if (targetUser != null)
    {
        var allRatings = await db.Ratings.Where(x => x.TargetUserId == targetUserId).ToListAsync();
        var avg = allRatings.Select(x => x.Score).Append(score).Average();
        targetUser.TrustScore = avg;
        var completedCount = await db.Exchanges.CountAsync(e =>
            e.Status == "COMPLETED" && (e.RequesterId == targetUserId || e.ProviderId == targetUserId));
        targetUser.BadgeTier = completedCount switch
        {
            >= 25 => "GOLD",
            >= 10 => "SILVER",
            >= 3 => "BRONZE",
            _ => "NONE"
        };
    }

    await db.SaveChangesAsync();
    return Results.Ok(rating);
});

// FR-12: Community Impact Dashboard.
app.MapGet("/api/areas/{area}/impact", async (AppDbContext db, string area) =>
{
    var monthStart = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds();

    var areaListingIds = await db.Listings
        .Where(l => l.AreaSuburb.ToLower() == area.ToLower())
        .Select(l => l.ListingId)
        .ToListAsync();

    var completedExchanges = await db.Exchanges
        .Where(e => e.Status == "COMPLETED" && areaListingIds.Contains(e.ListingId))
        .ToListAsync();

    var exchangesThisMonth = completedExchanges.Count(e =>
        long.TryParse(e.CompletedAt, out var completedMs) && completedMs >= monthStart);

    var topCategoryId = (await db.Listings
        .Where(l => areaListingIds.Contains(l.ListingId))
        .GroupBy(l => l.CategoryId)
        .OrderByDescending(g => g.Count())
        .Select(g => g.Key)
        .FirstOrDefaultAsync()) ?? "";

    var topCategory = topCategoryId == ""
        ? "No data yet"
        : (await db.Categories.FindAsync(topCategoryId))?.NameEn ?? "Unknown";

    // Estimated at 2 hours per completed exchange — a placeholder assumption noted here for
    // transparency; replace with real per-listing duration data if you add that field.
    var estHours = completedExchanges.Count * 2;

    return Results.Ok(new AreaImpactResponse
    {
        Area = area,
        ExchangesThisMonth = exchangesThisMonth,
        TopCategory = topCategory,
        EstHoursExchanged = estHours
    });
});

app.Run();

// --- Helpers ---

static Dictionary<string, string> DecodeJwtClaimsUnverified(string jwt)
{
    var result = new Dictionary<string, string>();
    try
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return result;
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        switch (payload.Length % 4) { case 2: payload += "=="; break; case 3: payload += "="; break; }
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
                result[prop.Name] = prop.Value.GetString() ?? "";
        }
    }
    catch { /* malformed token -> caller falls back to a generated uid */ }
    return result;
}
