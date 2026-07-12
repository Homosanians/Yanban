using Shouldly;
using Xunit;
using Yanban.Domain.Authorization;
using Yanban.Domain.Enums;

namespace Yanban.UnitTests;

public class BoardAccessTests
{
    [Theory]
    // Read — any member; archived state and ownership irrelevant; non-members denied.
    [InlineData(BoardPermission.Read, BoardRole.Viewer, false, false, true)]
    [InlineData(BoardPermission.Read, BoardRole.Viewer, false, true, true)]
    [InlineData(BoardPermission.Read, null, false, false, false)]

    // Write — Editor+ and not archived (ABAC: archived boards are read-only).
    [InlineData(BoardPermission.Write, BoardRole.Viewer, false, false, false)]
    [InlineData(BoardPermission.Write, BoardRole.Editor, false, false, true)]
    [InlineData(BoardPermission.Write, BoardRole.Admin, false, false, true)]
    [InlineData(BoardPermission.Write, BoardRole.Editor, false, true, false)]
    [InlineData(BoardPermission.Write, null, false, false, false)]

    // Manage — Admin+; still allowed while archived (needed to unarchive).
    [InlineData(BoardPermission.Manage, BoardRole.Editor, false, false, false)]
    [InlineData(BoardPermission.Manage, BoardRole.Admin, false, false, true)]
    [InlineData(BoardPermission.Manage, BoardRole.Admin, false, true, true)]
    [InlineData(BoardPermission.Manage, null, false, false, false)]

    // Delete — owner only, regardless of role.
    [InlineData(BoardPermission.Delete, BoardRole.Admin, false, false, false)]
    [InlineData(BoardPermission.Delete, BoardRole.Admin, true, false, true)]
    [InlineData(BoardPermission.Delete, null, true, false, true)]
    public void IsAllowed_MatchesTruthTable(
        BoardPermission permission, BoardRole? role, bool isOwner, bool isArchived, bool expected)
    {
        BoardAccess.IsAllowed(permission, role, isOwner, isArchived).ShouldBe(expected);
    }
}
