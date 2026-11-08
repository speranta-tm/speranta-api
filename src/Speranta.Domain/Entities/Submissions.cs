namespace Speranta.Domain.Entities;

/// <summary>
/// One of the free books, with everything its page shows. Admins add and edit books
/// from the panel; the six the site launched with are seeded word for word.
/// </summary>
public class Book
{
    public int Id { get; set; }

    /// <summary>The URL segment, e.g. "biblia". Matches the web client's route.</summary>
    public required string Slug { get; set; }

    public required string Title { get; set; }

    /// <summary>False shows "Stoc epuizat" on the site and refuses new orders.</summary>
    public bool IsAvailable { get; set; } = true;

    public int SortOrder { get; set; }

    // Each book's form asks slightly different things, exactly as the design (and
    // the old site) does. The flags live here so the API and the web client agree
    // on what is required.

    /// <summary>The form asks "Grupa de vârstă" (the adult books).</summary>
    public bool AsksAgeGroup { get; set; }

    /// <summary>The form asks "Aveți Biblie în casă?" (only the Bible).</summary>
    public bool AsksBibleAtHome { get; set; }

    /// <summary>The form offers pickup from the church (the children's books).</summary>
    public bool AllowsPickup { get; set; }

    /// <summary>Shows the "Pentru copii" label.</summary>
    public bool IsForChildren { get; set; }

    /// <summary>The page's big title, when it differs from <see cref="Title"/> ("Biblia sau Sfânta Scriptură").</summary>
    public string? Heading { get; set; }

    /// <summary>One sentence on the "Cărți gratuite" catalogue.</summary>
    public string Blurb { get; set; } = "";

    /// <summary>The first paragraph, set larger.</summary>
    public string Lead { get; set; } = "";

    public List<string> Paragraphs { get; set; } = [];

    /// <summary>The ticked points under the text. Often empty.</summary>
    public List<string> Benefits { get; set; } = [];

    /// <summary>The bold closing line. Optional.</summary>
    public string? Closing { get; set; }

    /// <summary>Above the order form: "Comandă cartea".</summary>
    public string FormEyebrow { get; set; } = "Comandă cartea";

    /// <summary>The order form's title: "Primește gratuit „…”".</summary>
    public string FormTitle { get; set; } = "";

    /// <summary>
    /// Uploaded in the admin. Null for the six launch books, whose covers ship with
    /// the web client; the client uses its own copy when this is null.
    /// </summary>
    public Guid? CoverFileId { get; set; }

    public StoredFile? CoverFile { get; set; }

    /// <summary>The blurred banner behind the title. Falls back to the cover.</summary>
    public Guid? BannerFileId { get; set; }

    public StoredFile? BannerFile { get; set; }
}

public class BookOrder
{
    /// <summary>Also the human order number, shown as "Comanda #128".</summary>
    public int Id { get; set; }

    public int BookId { get; set; }

    public Book Book { get; set; } = null!;

    public required string FullName { get; set; }

    public required string Phone { get; set; }

    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.Delivery;

    /// <summary>Required while the order is for delivery.</summary>
    public string? Address { get; set; }

    /// <summary>Null when the book's form does not ask it.</summary>
    public AgeGroup? AgeGroup { get; set; }

    /// <summary>"Aveți Biblie în casă?" Null when the book's form does not ask it.</summary>
    public bool? HasBibleAtHome { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.New;

    /// <summary>Admins only, e.g. "A cerut livrare după ora 17. Curier: Daniel."</summary>
    public string? InternalNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public class KidsSignup
{
    public int Id { get; set; }

    public required string ParentName { get; set; }

    public required string Phone { get; set; }

    public string? Email { get; set; }

    /// <summary>Optional, by the church's choice.</summary>
    public string? ChildFirstName { get; set; }

    /// <summary>Optional. Decides which group the admin list shows.</summary>
    public int? ChildAge { get; set; }

    /// <summary>"Ați mai participat la astfel de activități?"</summary>
    public bool ParticipatedBefore { get; set; }

    public string? Message { get; set; }

    public SignupStatus Status { get; set; } = SignupStatus.New;

    public string? InternalNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public class ContactMessage
{
    public int Id { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public required string Email { get; set; }

    public string? Subject { get; set; }

    public required string Body { get; set; }

    public MessageStatus Status { get; set; } = MessageStatus.New;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One line of a submission's history ("Istoric"): received, emailed, status
/// changed and by whom. Its own table rather than columns, so the history can
/// grow without a migration per kind of event.
/// </summary>
public class ActivityEntry
{
    public long Id { get; set; }

    public SubmissionKind Kind { get; set; }

    public int SubmissionId { get; set; }

    public required string Text { get; set; }

    public Guid? ActorUserId { get; set; }

    public DateTimeOffset At { get; set; }
}
