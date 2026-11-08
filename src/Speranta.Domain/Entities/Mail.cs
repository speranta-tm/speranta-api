namespace Speranta.Domain.Entities;

/// <summary>
/// An email waiting to go out, or already sent. Kept in the database rather than
/// in memory so that nothing is lost when Gmail refuses (an expired password, an
/// outage) or the API restarts: the sender keeps retrying, and once the problem is
/// fixed every notification that was missed is delivered.
///
/// This is the fix for the old site's real problem: its WP Mail SMTP connection
/// silently stopped every few days, and the notifications from those days were
/// simply gone.
/// </summary>
public class OutboxEmail
{
    public long Id { get; set; }

    public required string To { get; set; }

    public required string Subject { get; set; }

    public required string HtmlBody { get; set; }

    /// <summary>The submission this email announces, if any; its history gets a line once it is sent.</summary>
    public SubmissionKind? Kind { get; set; }

    public int? SubmissionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public string? LastError { get; set; }
}

/// <summary>
/// Single row: what the API last learned about its mailbox, from real sends and
/// from the periodic sign-in check. The admin's warning banner is built from it.
/// </summary>
public class MailStatus
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public DateTimeOffset? LastSuccessAt { get; set; }

    public DateTimeOffset? LastFailureAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? LastCheckAt { get; set; }

    public bool? LastCheckOk { get; set; }
}
