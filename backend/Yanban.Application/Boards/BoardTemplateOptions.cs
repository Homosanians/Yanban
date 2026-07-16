namespace Yanban.Application.Boards;

/// <summary>One named starter layout: an id the client sends, a label it shows, and the lists.</summary>
public record BoardTemplate(string Id, string Name, IReadOnlyList<string> Lists);

/// <summary>
/// The starter templates a new board can be seeded from. Configurable rather than a hardcoded
/// array (a new layout is a config change, not a deploy), but shipped with sensible built-ins so
/// the app works out of the box with no configuration.
///
/// <para>The list order is meaningful: it becomes the left-to-right rank order on the board.</para>
/// </summary>
public class BoardTemplateOptions
{
    public const string SectionName = "BoardTemplates";

    public List<BoardTemplate> Templates { get; set; } =
    [
        new("simple", "Simple", ["Backlog", "To Do", "Doing", "Done"]),
        new("dev-flow", "Dev flow", ["Backlog", "Ready for Dev", "In Progress", "Code Review", "QA", "Done"]),
    ];

    /// <summary>The template with this id, or null. Ids are matched case-insensitively.</summary>
    public BoardTemplate? Find(string id) =>
        Templates.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}
