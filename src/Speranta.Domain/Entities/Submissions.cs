namespace Speranta.Domain.Entities;

/// <summary>
/// One of the free books. The page text for each book lives in the web client;
/// the API owns only what an admin changes: the order on the site and whether
/// it is still in stock.
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

    public AgeGroup AgeGroup { get; set; }

    /// <summary>"Aveți Biblie în casă?"</summary>
    public bool HasBibleAtHome { get; set; }

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
