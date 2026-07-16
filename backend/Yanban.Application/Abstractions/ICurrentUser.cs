namespace Yanban.Application.Abstractions;

/// <summary>
/// Ambient identity of the caller behind the current request. Lets Infrastructure
/// record "who did it" for audit purposes without threading a userId through every
/// service signature (many mutations, such as rename, archive and delete, carry none)
/// and without leaking <c>HttpContext</c> below the API layer.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The authenticated user's id, or null outside an authenticated request.</summary>
    Guid? UserId { get; }
}
