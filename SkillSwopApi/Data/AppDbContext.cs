using Microsoft.EntityFrameworkCore;
using SkillSwopApi.Models;

namespace SkillSwopApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<SkillCategoryEntity> Categories => Set<SkillCategoryEntity>();
    public DbSet<SkillListingEntity> Listings => Set<SkillListingEntity>();
    public DbSet<ExchangeEntity> Exchanges => Set<ExchangeEntity>();
    public DbSet<CoordinationMessageEntity> Messages => Set<CoordinationMessageEntity>();
    public DbSet<RatingEntity> Ratings => Set<RatingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>().HasKey(u => u.UserId);
        modelBuilder.Entity<SkillCategoryEntity>().HasKey(c => c.CategoryId);
        modelBuilder.Entity<SkillListingEntity>().HasKey(l => l.ListingId);
        modelBuilder.Entity<ExchangeEntity>().HasKey(e => e.ExchangeId);
        modelBuilder.Entity<CoordinationMessageEntity>().HasKey(m => m.MessageId);
        modelBuilder.Entity<RatingEntity>().HasKey(r => r.RatingId);

        // Matches the five categories already hardcoded in the Android app's MainActivity.kt,
        // so ids line up if you later switch the app to fetch /categories instead of using
        // its local hardcoded list.
        modelBuilder.Entity<SkillCategoryEntity>().HasData(
            new SkillCategoryEntity { CategoryId = "cat-sewing", NameEn = "Sewing & Crafting", NameZu = "Ukuthunga", NameAf = "Naaldwerk" },
            new SkillCategoryEntity { CategoryId = "cat-maths", NameEn = "Mathematics Tutoring", NameZu = "Ukufundisa iMaths", NameAf = "Wiskunde Onderrig" },
            new SkillCategoryEntity { CategoryId = "cat-garden", NameEn = "Gardening & Agriculture", NameZu = "Ingadi", NameAf = "Tuinbou" },
            new SkillCategoryEntity { CategoryId = "cat-repairs", NameEn = "Home Repairs", NameZu = "Ukulungisa Indlu", NameAf = "Huis Herstelwerk" },
            new SkillCategoryEntity { CategoryId = "cat-lang", NameEn = "Language Lessons", NameZu = "Izifundo Zolimi", NameAf = "Taal Lesse" }
        );
    }
}
