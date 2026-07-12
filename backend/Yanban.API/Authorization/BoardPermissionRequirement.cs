using Microsoft.AspNetCore.Authorization;
using Yanban.Domain.Authorization;

namespace Yanban.API.Authorization;

/// <summary>Resource-based requirement: the caller must hold <see cref="Permission"/> on the board resource.</summary>
public sealed class BoardPermissionRequirement : IAuthorizationRequirement
{
    public BoardPermission Permission { get; }

    public BoardPermissionRequirement(BoardPermission permission) => Permission = permission;
}
