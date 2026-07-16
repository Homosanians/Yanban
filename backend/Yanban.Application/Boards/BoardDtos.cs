using System.ComponentModel.DataAnnotations;
using Yanban.Domain.Enums;

namespace Yanban.Application.Boards;

/// <summary>
/// <paramref name="Template"/> names a starter layout (see <c>BoardTemplateOptions</c>): "simple",
/// "dev-flow", or null for an empty board. Both default to their empty state, so a caller that
/// sends neither gets a blank board.
///
/// <para><paramref name="SeedDefaultLists"/> is the legacy boolean, kept for backward
/// compatibility: true means the simple template. <paramref name="Template"/> wins if both are sent.</para>
/// </summary>
public record CreateBoardRequest(
    [Required, MaxLength(200)] string Name,
    bool SeedDefaultLists = false,
    string? Template = null);

/// <summary>A starter layout offered to the new-board dialog.</summary>
public record BoardTemplateDto(string Id, string Name, IReadOnlyList<string> Lists);

public record RenameBoardRequest(
    [Required, MaxLength(200)] string Name);

/// <summary>A board plus the calling user's role on it (handy for the client UI).</summary>
public record BoardDto(
    Guid Id,
    string Name,
    Guid OwnerId,
    bool Archived,
    DateTimeOffset CreatedAt,
    BoardRole Role);

public record AddMemberRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [EnumDataType(typeof(BoardRole))] BoardRole Role);

public record UpdateMemberRequest(
    [EnumDataType(typeof(BoardRole))] BoardRole Role);

public record BoardMemberDto(Guid UserId, string Email, string DisplayName, BoardRole Role);
