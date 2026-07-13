namespace Yanban.Domain.Entities;

/// <summary>
/// A board-scoped preset a card can be created from ("Bug report", "Release checklist").
///
/// Deliberately its own table rather than an IsTemplate flag on <see cref="Card"/> (which
/// is how Trello models it): a flagged card would occupy a real list, hold a real rank,
/// and — decisively — its text would land in the card search vector and pollute every
/// search result. A template is a blueprint, not a card.
/// </summary>
public class CardTemplate
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public Board Board { get; set; } = null!;

    /// <summary>What the template is called in the picker (not the card's title).</summary>
    public string Name { get; set; } = null!;

    /// <summary>The title stamped onto cards created from this template.</summary>
    public string Title { get; set; } = null!;
    public string? Description { get; set; }

    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
