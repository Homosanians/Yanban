using Yanban.Application.Abstractions;

namespace Yanban.API.Identity;

/// <summary>
/// Resolves <see cref="ICurrentUser"/> from the request's authenticated principal.
/// Lives in the API layer because it reads <c>HttpContext</c>; Infrastructure depends
/// only on the interface, so no web types leak downward. Mirrors the <c>sub</c>-claim
/// read used by the controllers (JWT inbound-claim mapping is disabled).
/// </summary>
public class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid? UserId =>
        Guid.TryParse(_accessor.HttpContext?.User.FindFirst("sub")?.Value, out var id) ? id : null;
}
