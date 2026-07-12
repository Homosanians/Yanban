namespace Yanban.Domain.Entities;

/// <summary>
/// A column on a board (the spec's "List"). Named BoardList in code to avoid
/// clashing with System.Collections.Generic.List&lt;T&gt;; mapped to table "lists".
/// </summary>
public class BoardList
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public Board Board { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>Lexorank string; ordering key within the board.</summary>
    public string Rank { get; set; } = null!;

    public ICollection<Card> Cards { get; set; } = new List<Card>();
}
