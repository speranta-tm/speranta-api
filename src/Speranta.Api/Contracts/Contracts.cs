using Speranta.Domain;

namespace Speranta.Api.Contracts;

// ---- Public forms -------------------------------------------------------------
//
// Every form carries `Website`, a honeypot the web client renders hidden. A human
// never fills it; a form-filling bot usually does. The old WordPress forms had the
// same trap (their hidden "Email" field), and it is the only spam guard they had.

public sealed record BookOrderRequest(
    string? BookSlug,
    string? FullName,
    string? Phone,
    DeliveryMethod? DeliveryMethod,
    string? Address,
    AgeGroup? AgeGroup,
    bool? HasBibleAtHome,
    bool AcceptsDeliveryArea,
    bool AcceptsDataUse,
    string? Website);

public sealed record KidsSignupRequest(
    string? ParentName,
    string? Phone,
    string? Email,
    string? ChildFirstName,
    int? ChildAge,
    bool? ParticipatedBefore,
    string? Message,
    bool AcceptsDataUse,
    string? Website);

public sealed record ContactMessageRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Subject,
    string? Message,
    bool AcceptsDataUse,
    string? Website);

/// <summary>What the thank-you page needs. A honeypot hit gets the same shape.</summary>
public sealed record SubmissionReceipt(int Reference);

// ---- Public content -----------------------------------------------------------

public sealed record ServiceTimeDto(DayOfWeek Day, TimeOnly Start, TimeOnly End);

public sealed record KidsGroupDto(string Label, TimeOnly Start, TimeOnly End);

public sealed record FlyerDto(string Url, string FileName, DateTimeOffset UploadedAt, long SizeBytes);

public sealed record BookDto(
    string Slug, string Title, bool IsAvailable,
    bool AsksAgeGroup, bool AsksBibleAtHome, bool AllowsPickup, bool IsForChildren);

public sealed record SiteInfo(
    IReadOnlyList<ServiceTimeDto> Services,
    IReadOnlyList<KidsGroupDto> KidsGroups,
    string KidsPhone,
    FlyerDto? Flyer,
    IReadOnlyList<BookDto> Books);

public sealed record AnnouncementSummary(
    string Slug, string Title, string Excerpt, string? CoverUrl, DateTimeOffset PublishedAt);

public sealed record AnnouncementDetail(
    string Slug, string Title, string BodyHtml, string? CoverUrl, DateTimeOffset PublishedAt);

public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int PageNumber, int PageSize);

// ---- Admin: submissions -------------------------------------------------------

public sealed record ActivityDto(DateTimeOffset At, string Text);

public sealed record BookOrderRow(
    int Id, DateTimeOffset CreatedAt, string FullName, string Phone, string BookSlug,
    string BookTitle, DeliveryMethod DeliveryMethod, string? Address, OrderStatus Status,
    bool EmailPending = false);

public sealed record BookOrderDetail(
    int Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string FullName, string Phone,
    string BookSlug, string BookTitle, DeliveryMethod DeliveryMethod, string? Address,
    AgeGroup? AgeGroup, bool? HasBibleAtHome, OrderStatus Status, string? InternalNote,
    IReadOnlyList<ActivityDto> History,
    bool EmailPending = false);

public sealed record UpdateBookOrderRequest(
    OrderStatus? Status, DeliveryMethod? DeliveryMethod, string? Address, string? InternalNote);

public sealed record KidsSignupRow(
    int Id, DateTimeOffset CreatedAt, string ParentName, string Phone, string? Email,
    string? ChildFirstName, int? ChildAge, string? Group, bool ParticipatedBefore,
    string? Message, SignupStatus Status,
    bool EmailPending = false);

public sealed record KidsSignupDetail(
    int Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string ParentName, string Phone,
    string? Email, string? ChildFirstName, int? ChildAge, string? Group, bool ParticipatedBefore,
    string? Message, SignupStatus Status, string? InternalNote, IReadOnlyList<ActivityDto> History,
    bool EmailPending = false);

public sealed record UpdateKidsSignupRequest(SignupStatus? Status, string? InternalNote);

public sealed record ContactMessageRow(
    int Id, DateTimeOffset CreatedAt, string Name, string Email, string? Subject,
    string Preview, MessageStatus Status,
    bool EmailPending = false);

public sealed record ContactMessageDetail(
    int Id, DateTimeOffset CreatedAt, string? FirstName, string? LastName, string Email,
    string? Subject, string Body, MessageStatus Status, IReadOnlyList<ActivityDto> History,
    bool EmailPending = false);

public sealed record UpdateContactMessageRequest(MessageStatus Status);

/// <summary>
/// <c>EmailPending</c> on rows and details: the notification for this submission has
/// not gone out yet (Gmail refused, or SMTP is not set up). It is retried until it does.
/// </summary>
/// <summary>A list page plus the count behind every status tab ("Noi (2)").</summary>
public sealed record SubmissionList<TRow, TStatus>(
    IReadOnlyList<TRow> Items, int Total, int PageNumber, int PageSize,
    IReadOnlyDictionary<TStatus, int> Counts, int ThisMonth)
    where TStatus : notnull;

public sealed record DashboardTodo(SubmissionKind Kind, int Id, string Title, string Detail, DateTimeOffset At);

public sealed record Dashboard(
    string? DisplayName,
    int NewOrders, int OrdersThisMonth,
    int NewSignups, int EnrolledKids,
    int NewMessages, int MessagesThisMonth,
    IReadOnlyList<DashboardTodo> Todo);

// ---- Admin: content -----------------------------------------------------------

public sealed record AdminAnnouncementRow(
    int Id, string Slug, string Title, AnnouncementStatus Status, DateTimeOffset? PublishAt,
    bool IsLive, string? CoverUrl, DateTimeOffset UpdatedAt);

public sealed record AdminAnnouncement(
    int Id, string Slug, string Title, string BodyHtml, Guid? CoverFileId, string? CoverUrl,
    AnnouncementStatus Status, DateTimeOffset? PublishAt, bool ShowOnHome,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>
/// Create and update both take the whole editor state, which is what the editor's
/// autosave sends. <c>PublishAt</c> null with Status Published means "now".
/// </summary>
public sealed record SaveAnnouncementRequest(
    string? Title, string? BodyHtml, Guid? CoverFileId, AnnouncementStatus Status,
    DateTimeOffset? PublishAt, bool ShowOnHome);

public sealed record FileDto(Guid Id, string Url, string FileName, string ContentType, long SizeBytes);

public sealed record ScheduleDto(
    IReadOnlyList<ServiceTimeDto> Services,
    IReadOnlyList<KidsGroupDto> KidsGroups,
    string KidsPhone);

public sealed record AdminFlyer(
    string Url, string FileName, DateTimeOffset UploadedAt, long SizeBytes, int Downloads);

public sealed record AdminBook(int Id, string Slug, string Title, bool IsAvailable, int SortOrder, int NewThisMonth);

public sealed record UpdateBookRequest(bool IsAvailable);

public sealed record ReorderBooksRequest(IReadOnlyList<int> Ids);

// ---- Admin: people ------------------------------------------------------------

public sealed record AccountInfo(Guid Id, string Email, string? DisplayName);

public sealed record UpdateAccountRequest(string? DisplayName);

public sealed record AdminUserDto(Guid Id, string Email, string? DisplayName, bool IsYou);

public sealed record PendingInviteDto(int Id, string Email, string? DisplayName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record AdminsDto(IReadOnlyList<AdminUserDto> Admins, IReadOnlyList<PendingInviteDto> Invites);

public sealed record CreateInviteRequest(string? Email, string? DisplayName);

public sealed record InviteInfo(string Email, string? DisplayName, string? InvitedBy);

public sealed record AcceptInviteRequest(string? Password, string? DisplayName);
