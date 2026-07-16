using System.ComponentModel.DataAnnotations;

namespace Yanban.Application.Templates;

public record CreateCardTemplateRequest(
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(500)] string Title,
    [MaxLength(10_000)] string? Description);

public record UpdateCardTemplateRequest(
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(500)] string Title,
    [MaxLength(10_000)] string? Description);

/// <summary>
/// Creates a real card from a template. <see cref="Title"/> overrides the template's title
/// when supplied; the common case is "Bug: &lt;what broke&gt;" off a "Bug report" template.
/// </summary>
public record CreateCardFromTemplateRequest(
    [Required] Guid TemplateId,
    [MaxLength(500)] string? Title);

/// <summary><see cref="Name"/> is what the template is called; <see cref="Title"/> is what it stamps onto a card.</summary>
public record CardTemplateDto(
    Guid Id,
    Guid BoardId,
    string Name,
    string Title,
    string? Description,
    DateTimeOffset CreatedAt);
