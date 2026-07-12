namespace Yanban.Domain.Entities;

public class Board
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>When set, the board is read-only regardless of member role.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<BoardMember> Members { get; set; } = new List<BoardMember>();
    public ICollection<BoardList> Lists { get; set; } = new List<BoardList>();
}
