using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yanban.Application.Abstractions;
using Yanban.Application.Boards;

namespace Yanban.API.Controllers;

/// <summary>
/// The starter layouts a new board can be seeded from. Authenticated but not board-scoped — there
/// is no board yet, and anyone who may create one may see the templates on offer.
/// </summary>
[ApiController]
[Authorize]
[Route("board-templates")]
public class BoardTemplatesController : ControllerBase
{
    private readonly IBoardService _boards;

    public BoardTemplatesController(IBoardService boards) => _boards = boards;

    [HttpGet]
    public ActionResult<IReadOnlyList<BoardTemplateDto>> List() => Ok(_boards.ListTemplates());
}
