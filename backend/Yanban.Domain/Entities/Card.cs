namespace Yanban.Domain.Entities;

public class Card
{
    public Guid Id { get; set; }
    public Guid ListId { get; set; }
    public BoardList List { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Lexorank string; ordering key within the list.</summary>
    public string Rank { get; set; } = null!;

    public Guid? AssigneeId { get; set; }
    public User? Assignee { get; set; }

    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;

    public DateTimeOffset? DueDate { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    // Optimistic concurrency uses Postgres' system column xmin, configured as a
    // shadow concurrency token in CardConfiguration.
}
