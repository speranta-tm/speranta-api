using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Speranta.Domain;
using Speranta.Domain.Entities;
using Speranta.Infrastructure.Identity;

namespace Speranta.Infrastructure;

public class SperantaDbContext(DbContextOptions<SperantaDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public DbSet<Book> Books => Set<Book>();

    public DbSet<BookOrder> BookOrders => Set<BookOrder>();

    public DbSet<KidsSignup> KidsSignups => Set<KidsSignup>();

    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();

    public DbSet<ActivityEntry> Activity => Set<ActivityEntry>();

    public DbSet<Announcement> Announcements => Set<Announcement>();

    public DbSet<StoredFile> Files => Set<StoredFile>();

    public DbSet<ServiceTime> ServiceTimes => Set<ServiceTime>();

    public DbSet<KidsGroup> KidsGroups => Set<KidsGroup>();

    public DbSet<SiteSettings> Settings => Set<SiteSettings>();

    public DbSet<AdminInvite> AdminInvites => Set<AdminInvite>();

    public DbSet<OutboxEmail> Outbox => Set<OutboxEmail>();

    public DbSet<MailStatus> MailStatus => Set<MailStatus>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity names its own tables ("AspNetUsers"), which the snake-case
        // convention leaves alone. Renamed so the whole schema reads one way.
        builder.Entity<AppUser>().ToTable("users");
        builder.Entity<AppRole>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        builder.Entity<AppUser>(e => e.Property(x => x.DisplayName).HasMaxLength(100));

        builder.Entity<Book>(e =>
        {
            e.Property(x => x.Slug).HasMaxLength(80);
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasData(Seed.Books);
        });

        // Enums are stored as text, not ints: a person reading the table sees
        // 'Confirmed', and adding a value is a code change, not a magic number.
        builder.Entity<BookOrder>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(Limits.Name);
            e.Property(x => x.Phone).HasMaxLength(Limits.Phone);
            e.Property(x => x.Address).HasMaxLength(Limits.Address);
            e.Property(x => x.InternalNote).HasMaxLength(Limits.Note);
            e.Property(x => x.DeliveryMethod).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AgeGroup).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

            e.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId)
                .OnDelete(DeleteBehavior.Restrict);

            // The admin list is read newest-first, filtered by status.
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => x.CreatedAt);
        });

        builder.Entity<KidsSignup>(e =>
        {
            e.Property(x => x.ParentName).HasMaxLength(Limits.Name);
            e.Property(x => x.Phone).HasMaxLength(Limits.Phone);
            e.Property(x => x.Email).HasMaxLength(Limits.Email);
            e.Property(x => x.ChildFirstName).HasMaxLength(Limits.Name);
            e.Property(x => x.Message).HasMaxLength(Limits.Message);
            e.Property(x => x.InternalNote).HasMaxLength(Limits.Note);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => x.CreatedAt);
            e.ToTable(t => t.HasCheckConstraint(
                "ck_kids_signup_age", "child_age IS NULL OR child_age BETWEEN 0 AND 18"));
        });

        builder.Entity<ContactMessage>(e =>
        {
            e.Property(x => x.FirstName).HasMaxLength(Limits.Name);
            e.Property(x => x.LastName).HasMaxLength(Limits.Name);
            e.Property(x => x.Email).HasMaxLength(Limits.Email);
            e.Property(x => x.Subject).HasMaxLength(Limits.Subject);
            e.Property(x => x.Body).HasMaxLength(Limits.Message);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => x.CreatedAt);
        });

        builder.Entity<ActivityEntry>(e =>
        {
            e.ToTable("activity");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Text).HasMaxLength(500);
            e.HasIndex(x => new { x.Kind, x.SubmissionId, x.At });
        });

        builder.Entity<Announcement>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(100);
            e.Property(x => x.BodyHtml).HasMaxLength(Limits.AnnouncementBody);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.Status, x.PublishAt });
            e.HasOne(x => x.CoverFile).WithMany().HasForeignKey(x => x.CoverFileId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<StoredFile>(e =>
        {
            e.ToTable("files");
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.ContentType).HasMaxLength(100);
        });

        builder.Entity<ServiceTime>(e =>
        {
            e.Property(x => x.Day).HasConversion<string>().HasMaxLength(10);
            e.ToTable(t => t.HasCheckConstraint("ck_service_time_ordered", "\"end\" > start"));
            e.HasData(Seed.ServiceTimes);
        });

        builder.Entity<KidsGroup>(e =>
        {
            e.Property(x => x.Label).HasMaxLength(100);
            e.ToTable(t => t.HasCheckConstraint("ck_kids_group_ordered", "\"end\" > start"));
            e.HasData(Seed.KidsGroups);
        });

        builder.Entity<SiteSettings>(e =>
        {
            e.ToTable("site_settings", t => t.HasCheckConstraint(
                "ck_site_settings_singleton", $"id = {SiteSettings.SingletonId}"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.KidsPhone).HasMaxLength(Limits.Phone);
            e.HasOne(x => x.FlyerFile).WithMany().HasForeignKey(x => x.FlyerFileId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasData(new SiteSettings { Id = SiteSettings.SingletonId, KidsPhone = "0764 915 396" });
        });

        builder.Entity<OutboxEmail>(e =>
        {
            e.ToTable("outbox_emails");
            e.Property(x => x.To).HasMaxLength(Limits.Email);
            e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);

            // The sender's one query: what is due and not yet sent.
            e.HasIndex(x => x.NextAttemptAt).HasFilter("sent_at IS NULL");
            e.HasIndex(x => new { x.Kind, x.SubmissionId });
        });

        builder.Entity<MailStatus>(e =>
        {
            e.ToTable("mail_status", t => t.HasCheckConstraint(
                "ck_mail_status_singleton", $"id = {Speranta.Domain.Entities.MailStatus.SingletonId}"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasData(new MailStatus { Id = Speranta.Domain.Entities.MailStatus.SingletonId });
        });

        builder.Entity<AdminInvite>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(Limits.Email);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.Email);
        });
    }
}

/// <summary>Column lengths, shared with the API's validation so the two cannot drift.</summary>
public static class Limits
{
    public const int Name = 100;
    public const int Phone = 30;
    public const int Email = 254;
    public const int Address = 300;
    public const int Subject = 200;
    public const int Message = 4000;
    public const int Note = 2000;
    public const int AnnouncementBody = 100_000;
}

/// <summary>What the live WordPress site showed on 2026-11-08, so a fresh database
/// starts identical to it.</summary>
internal static class Seed
{
    public static readonly Book[] Books =
    [
        new() { Id = 1, Slug = "biblia", Title = "Biblia", SortOrder = 1, AsksAgeGroup = true, AsksBibleAtHome = true },
        new() { Id = 2, Slug = "viata-lui-isus", Title = "Viața lui Isus", SortOrder = 2, AsksAgeGroup = true },
        new() { Id = 3, Slug = "tragedia-veacurilor", Title = "Tragedia veacurilor", SortOrder = 3, AsksAgeGroup = true },
        new() { Id = 4, Slug = "calea-catre-hristos", Title = "Calea către Hristos", SortOrder = 4, AsksAgeGroup = true },
        new()
        {
            Id = 5, Slug = "biblia-pentru-elevi", Title = "Biblia pentru elevi", SortOrder = 5,
            AllowsPickup = true, IsForChildren = true,
        },
        new()
        {
            Id = 6, Slug = "mesajul-lui-dumnezeu-pentru-tine",
            Title = "Mesajul lui Dumnezeu pentru tine", SortOrder = 6,
            AllowsPickup = true, IsForChildren = true,
        },
    ];

    public static readonly ServiceTime[] ServiceTimes =
    [
        new() { Id = 1, Day = DayOfWeek.Friday, Start = new(20, 0), End = new(21, 0), SortOrder = 1 },
        new() { Id = 2, Day = DayOfWeek.Saturday, Start = new(9, 0), End = new(12, 0), SortOrder = 2 },
        new() { Id = 3, Day = DayOfWeek.Saturday, Start = new(18, 0), End = new(19, 0), SortOrder = 3 },
    ];

    public static readonly KidsGroup[] KidsGroups =
    [
        new() { Id = 1, Label = "Copii mici (până la 7 ani)", Start = new(10, 0), End = new(10, 50), SortOrder = 1 },
        new() { Id = 2, Label = "Copii între 7 și 14 ani", Start = new(9, 45), End = new(10, 50), SortOrder = 2 },
    ];
}
