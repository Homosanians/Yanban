using Yanban.Application.Auth;

namespace Yanban.Application.Abstractions;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct);
    Task LogoutAsync(string refreshToken, CancellationToken ct);
    Task LogoutAllAsync(Guid userId, CancellationToken ct);

    /// <summary>Redeems a confirmation token. Single-use.</summary>
    Task ConfirmEmailAsync(string token, CancellationToken ct);

    /// <summary>Queues a fresh confirmation mail. A no-op if the address is already confirmed.</summary>
    Task ResendConfirmationAsync(Guid userId, CancellationToken ct);
}
