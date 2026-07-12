using System.ComponentModel.DataAnnotations;

namespace Yanban.Application.Lists;

public record CreateListRequest(
    [Required, MaxLength(200)] string Name);

public record RenameListRequest(
    [Required, MaxLength(200)] string Name);

public record ListDto(Guid Id, Guid BoardId, string Name, string Rank);
