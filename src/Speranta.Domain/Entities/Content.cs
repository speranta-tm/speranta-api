namespace Speranta.Domain.Entities;

public class Announcement
{
    public int Id { get; set; }

    public required string Title { get; set; }

    /// <summary>URL segment, unique. Derived from the title when first created.</summary>
    public required string Slug { get; set; }

    /// <summary>Sanitised HTML: paragraphs, small headings, lists, links, bold, italic.</summary>
    public string BodyHtml { get; set; } = "";

    public Guid? CoverFileId { get; set; }

    public StoredFile? CoverFile { get; set; }

    public AnnouncementStatus Status { get; set; } = AnnouncementStatus.Draft;

    /// <summary>When it goes live. A future instant means scheduled.</summary>
    public DateTimeOffset? PublishAt { get; set; }

    public bool ShowOnHome { get; set; } = true;

    /// <summary>
    /// Set the first time it is saved as Published. From then on the slug is frozen,
    /// so a link someone already shared keeps working even if the title is edited.
    /// </summary>
    public DateTimeOffset? FirstPublishedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// An uploaded file (announcement cover, monthly flyer). The bytes live on disk
/// under the configured storage folder, named by <see cref="Id"/>; the row keeps
/// what is needed to serve them back.
/// </summary>
public class StoredFile
{
    public Guid Id { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One service, e.g. Sâmbătă 09:00–12:00.</summary>
public class ServiceTime
{
    public int Id { get; set; }

    public DayOfWeek Day { get; set; }

    public TimeOnly Start { get; set; }

    public TimeOnly End { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>One kids' group on Saturdays, e.g. "Copii mici (până la 7 ani)" 10:00–10:50.</summary>
public class KidsGroup
{
    public int Id { get; set; }

    public required string Label { get; set; }

    public TimeOnly Start { get; set; }

    public TimeOnly End { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>Single-row table for the few loose settings an admin edits.</summary>
public class SiteSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string KidsPhone { get; set; } = "";

    public Guid? FlyerFileId { get; set; }

    public StoredFile? FlyerFile { get; set; }

    public int FlyerDownloads { get; set; }
}

/// <summary>
/// The only way into the admin. The emailed link carries a random token; only its
/// SHA-256 is stored, so a database dump cannot be turned into a way in.
/// </summary>
public class AdminInvite
{
    public int Id { get; set; }

    public required string Email { get; set; }

    public required string TokenHash { get; set; }

    public Guid? InvitedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public bool IsOpen(DateTimeOffset now) => AcceptedAt is null && ExpiresAt > now;
}
