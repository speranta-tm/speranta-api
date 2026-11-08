using System.Security.Claims;

namespace Speranta.Api.Endpoints;

public static class CurrentUser
{
    /// <summary>
    /// The signed-in user's id. Throws rather than returning null: every call site
    /// sits behind RequireAuthorization, so a missing subject is a pipeline bug.
    /// </summary>
    public static Guid Id(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("Authenticated principal carries no usable subject claim.");
}

public static class CreatedAt
{
    /// <summary>
    /// <c>Results.Created</c> with a literal path ignores PathBase; behind a proxy that
    /// serves the API under a sub-path the Location header would point one level up.
    /// </summary>
    public static IResult Path(HttpContext context, string path, object? value) =>
        Results.Created(context.Request.PathBase.Add(path).Value, value);
}
