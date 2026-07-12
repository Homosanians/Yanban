using Yanban.Domain.Enums;

namespace Yanban.Domain.Entities;

/// <summary>Membership join row. Composite PK (BoardId, UserId).</summary>
public class BoardMember
{
    public Guid BoardId { get; set; }
    public Board Board { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public BoardRole Role { get; set; }
}
