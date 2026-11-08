using Microsoft.AspNetCore.Identity;

namespace Speranta.Infrastructure.Identity;

/// <summary>
/// An admin. There are no public accounts: everyone here was invited, or was
/// named in Bootstrap:AdminEmails.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    /// <summary>Shown in the admin ("Bună, Marc") and in the history log.</summary>
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class AppRole : IdentityRole<Guid>
{
}

/// <summary>Role names, as constants so a typo is a compile error.</summary>
public static class AppRoles
{
    public const string Admin = "Admin";

    public static readonly string[] All = [Admin];
}
