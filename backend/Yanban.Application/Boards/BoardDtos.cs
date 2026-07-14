using System.ComponentModel.DataAnnotations;
using Yanban.Domain.Enums;

namespace Yanban.Application.Boards;

/// <summary>
/// <paramref name="SeedDefaultLists"/> opts into the starter template (Backlog / To Do / Doing /
/// Done). It defaults to false, so an empty board stays the default and every existing caller is
/// unaffected.
/// </summary>
public record CreateBoardRequest(
    [Required, MaxLength(200)] string Name,
    bool SeedDefaultLists = false);

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
