namespace Yanban.Domain.Entities;

public class Comment
{
    public Guid Id { get; set; }

    public Guid CardId { get; set; }
    public Card Card { get; set; } = null!;

    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;

    public string Body { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set the first time the author edits the body; null means never edited.</summary>
    public DateTimeOffset? EditedAt { get; set; }
}
