using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Speranta.Domain;
using Speranta.Domain.Entities;
using Speranta.Infrastructure;

namespace Speranta.Api.Email;

public enum MailState
{
    /// <summary>Sending works (or nothing has failed yet).</summary>
    Ok,

    /// <summary>Gmail refuses: a send or the periodic sign-in failed, and nothing has gone through since.</summary>
    Failing,

    /// <summary>No SMTP credentials at all; mail waits in the outbox.</summary>
    NotConfigured,
}

/// <summary>What the admin's warning banner shows.</summary>
public sealed record MailHealthDto(
    MailState State,
    int Pending,
    DateTimeOffset? FailingSince,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastCheckAt,
    string? LastError,
    string NotifyTo);

public sealed class MailHealth(
    SperantaDbContext db, IMailTransport transport, IOptions<EmailOptions> options)
{
    public async Task<MailHealthDto> GetAsync(CancellationToken ct)
    {
        var status = await db.MailStatus.AsNoTracking().FirstAsync(ct);
        var unsent = db.Outbox.AsNoTracking().Where(m => m.SentAt == null);
        var pending = await unsent.CountAsync(ct);
        var oldestFailed = await unsent.Where(m => m.Attempts > 0)
            .OrderBy(m => m.CreatedAt).Select(m => (DateTimeOffset?)m.CreatedAt).FirstOrDefaultAsync(ct);

        // The sign-in check failing counts only if nothing has gone through since:
        // a later successful send means the password works again.
        var checkFailing = status.LastCheckOk == false
            && (status.LastSuccessAt is null || status.LastCheckAt > status.LastSuccessAt);

        var state = !transport.IsConfigured ? MailState.NotConfigured
            : oldestFailed is not null || checkFailing ? MailState.Failing
            : MailState.Ok;

        DateTimeOffset? since = state switch
        {
            MailState.Failing => Earliest(oldestFailed, checkFailing ? status.LastFailureAt : null),
            MailState.NotConfigured => await unsent.OrderBy(m => m.CreatedAt)
                .Select(m => (DateTimeOffset?)m.CreatedAt).FirstOrDefaultAsync(ct),
            _ => null,
        };

        return new MailHealthDto(
            state, pending, since, status.LastSuccessAt, status.LastCheckAt,
            state == MailState.Ok ? null : status.LastError,
            options.Value.NotifyTo);
    }

    /// <summary>Ids among <paramref name="ids"/> whose notification has not gone out yet.</summary>
    public async Task<HashSet<int>> PendingAsync(SubmissionKind kind, IReadOnlyCollection<int> ids, CancellationToken ct) =>
        [.. await db.Outbox.AsNoTracking()
            .Where(m => m.Kind == kind && m.SubmissionId != null && ids.Contains(m.SubmissionId.Value) && m.SentAt == null)
            .Select(m => m.SubmissionId!.Value)
            .Distinct()
            .ToListAsync(ct)];

    private static DateTimeOffset? Earliest(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : (a < b ? a : b);
}
