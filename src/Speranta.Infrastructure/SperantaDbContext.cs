using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

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
            e.Property(x => x.Heading).HasMaxLength(200);
            e.Property(x => x.Blurb).HasMaxLength(300);
            e.Property(x => x.Lead).HasMaxLength(1000);
            e.Property(x => x.Closing).HasMaxLength(300);

            // Compare the text lists by content. Without this EF compares them by
            // reference, sees the seeded books as changed on every build, and every
            // new migration rewrites them.
            var byContent = new ValueComparer<List<string>>(
                (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
                v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode(StringComparison.Ordinal))),
                v => v.ToList());
            e.Property(x => x.Paragraphs).Metadata.SetValueComparer(byContent);
            e.Property(x => x.Benefits).Metadata.SetValueComparer(byContent);
            e.Property(x => x.FormEyebrow).HasMaxLength(100);
            e.Property(x => x.FormTitle).HasMaxLength(200);
            e.HasOne(x => x.CoverFile).WithMany().HasForeignKey(x => x.CoverFileId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.BannerFile).WithMany().HasForeignKey(x => x.BannerFileId).OnDelete(DeleteBehavior.SetNull);
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
            e.Property(x => x.SmtpPasswordProtected).HasMaxLength(1000);
            e.Property(x => x.VapidPublicKey).HasMaxLength(200);
            e.Property(x => x.VapidPrivateKeyProtected).HasMaxLength(1000);
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

        builder.Entity<PushSubscription>(e =>
        {
            e.ToTable("push_subscriptions");
            e.Property(x => x.Endpoint).HasMaxLength(1000);
            e.Property(x => x.P256dh).HasMaxLength(200);
            e.Property(x => x.Auth).HasMaxLength(100);
            e.HasIndex(x => x.Endpoint).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        builder.Entity<AdminInvite>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(Limits.Email);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(100);
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
/// starts identical to it. The book texts are the ones on the new site's pages.</summary>
internal static class Seed
{
    public static readonly Book[] Books =
    [
        new()
        {
            Id = 1,
            Slug = "biblia",
            Title = "Biblia",
            SortOrder = 1,
            AsksAgeGroup = true, AsksBibleAtHome = true,
            Heading = "Biblia sau Sfânta Scriptură",
            Blurb = "Biblia sau Sfânta Scriptură — Cartea care a schimbat milioane de vieți.",
            Lead = "Cauți sens? Răspunsuri? Vrei să afli ce rezervă viitorul?",
            Paragraphs = ["S-ar putea ca răspunsul să fie mai aproape decât crezi — într-o Carte care a schimbat milioane de vieți. Îți oferim cadou o Biblie, complet gratuit.", "Dumnezeu are un mesaj personal pentru tine, iar fiecare pagină poate fi începutul unei noi călătorii."],
            Benefits = ["Poți descoperi tu singur adevărul Bibliei și sfaturile lui Dumnezeu pentru tine.", "Dacă nu ai încă o Biblie, ne bucurăm să ți-o oferim noi, fără niciun cost.", "Poți studia direct în liniștea casei tale."],
            Closing = "Dă-ți o șansă să-L cunoști pe Dumnezeu prin Cuvântul Său.",
            FormEyebrow = "Comandă o Biblie",
            FormTitle = "Primește o Biblie gratuit",
        },
        new()
        {
            Id = 2,
            Slug = "viata-lui-isus",
            Title = "Viața lui Isus",
            SortOrder = 2,
            AsksAgeGroup = true,
            Heading = null,
            Blurb = "Ellen G. White despre Isus din Evanghelii, Mesia din profeții, Cel mort și înviat.",
            Lead = "Dacă ai ajuns pe această pagină, înseamnă că vrei să afli mai multe despre cine a fost Isus. Ne bucurăm să îți oferim această oportunitate!",
            Paragraphs = ["Despre Isus din Nazaret s-a spus: „Niciodată n-a vorbit vreun om ca omul acesta” (Ioan 7:46). Despre autoarea cărții Viața lui Isus s-ar putea spune: „Puțini au scris ca ea despre Omul acesta.”", "Citind această carte, Îl vei descoperi pe adevăratul Isus din Evanghelii, pe Mesia din profețiile Vechiului Testament, pe Cel mort și înviat. Pe Cel ce plânge alături de cei suferinzi și îi ține în brațe pe cei mici.", "Nu va fi doar o lectură, ci o întâlnire cu Acela care a spus: „Eu sunt Calea, Adevărul și Viața.”", "Autoarea cărții, Ellen G. White (1827 – 1915), a fost o persoană cu daruri spirituale remarcabile. A scris peste 40 de cărți și 5000 de articole pe teme diverse: religie, educație, relații sociale, profeție, sănătate și management. Opera sa este tradusă în multe limbi, iar în română sunt disponibile peste 50 de volume."],
            Benefits = [],
            Closing = null,
            FormEyebrow = "Comandă cartea",
            FormTitle = "Primește gratuit „Viața lui Isus”",
        },
        new()
        {
            Id = 3,
            Slug = "tragedia-veacurilor",
            Title = "Tragedia veacurilor",
            SortOrder = 3,
            AsksAgeGroup = true,
            Heading = null,
            Blurb = "Marea luptă dintre adevăr și minciună, de-a lungul istoriei lumii.",
            Lead = "Tragedia veacurilor se numără printre cele mai cunoscute și citite cărți ale scriitoarei creștine Ellen G. White, fiind a cincea dintr-o serie care prezintă istoria lumii din perspectiva Bibliei și a creștinismului.",
            Paragraphs = ["Volumul acesta abordează luptele din trecut pentru a scoate în evidență faptele și principiile care vor influența decisiv mersul lumii și evenimentele viitoare. În paginile sale sunt dezvăluite scenele marii lupte dintre adevăr și minciună, înșelăciunile lui Satana și mijloacele prin care i se poate rezista cu succes. Cititorii vor găsi o soluție satisfăcătoare pentru marea problemă a răului și a suferinței și vor descoperi dreptatea și iubirea lui Dumnezeu."],
            Benefits = [],
            Closing = null,
            FormEyebrow = "Comandă cartea",
            FormTitle = "Primește gratuit „Tragedia veacurilor”",
        },
        new()
        {
            Id = 4,
            Slug = "calea-catre-hristos",
            Title = "Calea către Hristos",
            SortOrder = 4,
            AsksAgeGroup = true,
            Heading = null,
            Blurb = "Un ABC al vieții spirituale, pas cu pas spre Isus Hristos.",
            Lead = "Acest ABC al vieții spirituale, un mic tratat în domeniul devoțiunii, își enunță chiar din titlu misiunea pe care o are.",
            Paragraphs = ["Cartea îl îndrumă pe cititor spre Isus Hristos, ca fiind Singurul care poate să împlinească nevoile sufletului. Pașii celui care se simte nesigur sau cuprins de îndoială sunt conduși pe cărarea păcii, iar cel ce caută neprihănirea și desăvârșirea caracterului este îndrumat, pas cu pas, pe calea adevăratei viețuiri creștine.", "Cartea dezvăluie secretul biruinței și descoperă, într-o manieră simplă, harul mântuitor și puterea susținătoare a Marelui Prieten al întregii omeniri."],
            Benefits = [],
            Closing = null,
            FormEyebrow = "Comandă cartea",
            FormTitle = "Primește gratuit „Calea către Hristos”",
        },
        new()
        {
            Id = 5,
            Slug = "biblia-pentru-elevi",
            Title = "Biblia pentru elevi",
            SortOrder = 5,
            AllowsPickup = true, IsForChildren = true,
            Heading = null,
            Blurb = "Scriptura pe înțelesul copiilor: limbaj clar și ilustrații pline de culoare.",
            Lead = "„Biblia pentru elevi” este genul de carte care face Scriptura accesibilă, prietenoasă și plină de culoare pentru copilul tău.",
            Paragraphs = ["Nu este doar o simplă Biblie, ci un ghid care îl ajută să înțeleagă mai ușor povestirile, personajele și ideile biblice. Folosește un limbaj clar, explicații scurte pe marginea paginii și ilustrații atractive, și este concepută în așa fel încât copilul tău să citească singur povestirile biblice fără să se plictisească.", "Dacă copilul tău e mai mic și încă nu știe să citească, i-o poți citi tu, descoperind împreună momentele minunate din Biblie, eroii credinței, oamenii lui Dumnezeu care și-au păstrat credința chiar și în momente grele."],
            Benefits = ["Poți descoperi povestirile Bibliei împreună cu copilul tău, sau le poate descoperi și el singur.", "Dacă nu ai această carte, suntem bucuroși să ți-o oferim complet gratuit.", "Copilul tău poate citi direct în liniștea casei."],
            Closing = "Dă-i o șansă copilului tău să-L cunoască pe Dumnezeu prin Cuvântul Său.",
            FormEyebrow = "Comandă cartea",
            FormTitle = "Primește gratuit „Biblia pentru elevi”",
        },
        new()
        {
            Id = 6,
            Slug = "mesajul-lui-dumnezeu-pentru-tine",
            Title = "Mesajul lui Dumnezeu pentru tine",
            SortOrder = 6,
            AllowsPickup = true, IsForChildren = true,
            Heading = null,
            Blurb = "Povestiri și versete biblice de citit împreună cu copilul.",
            Lead = "O colecție a celor mai importante povestiri și versete din Biblie, într-o traducere contemporană. Citindu-le la rând, capitol cu capitol, ajungi să cunoști Cuvântul lui Dumnezeu ca pe o povestire în desfășurare.",
            Paragraphs = ["Cel mai interesant este că și tu, ca părinte, ai un rol în această poveste. Cartea este concepută pentru a fi citită copiilor de către părinți. Fiecare capitol al acestei ediții frumos ilustrate conține texte luate direct din Biblie, astfel că poți citi Cuvântul lui Dumnezeu împreună cu copilul tău, așa cum a fost scris.", "Cartea are de asemenea scurte explicații ale evenimentelor culturale și istorice, ca să te ajute să urmărești acțiunea, și câteva teme de reflecție despre cum se poate aplica textul la viața noastră de astăzi."],
            Benefits = ["Poți descoperi minunatele povestiri ale Bibliei împreună cu copilul tău.", "Dacă nu ai această carte, suntem bucuroși să ți-o oferim complet gratuit.", "Poți citi împreună cu copilul direct în liniștea casei tale."],
            Closing = "Dă-i o șansă copilului tău să-L cunoască pe Dumnezeu prin Cuvântul Său.",
            FormEyebrow = "Comandă cartea",
            FormTitle = "Primește gratuit „Mesajul lui Dumnezeu pentru tine”",
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
