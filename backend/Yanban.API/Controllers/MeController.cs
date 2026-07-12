using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yanban.Application.Auth;
using Yanban.Infrastructure.Persistence;

namespace Yanban.API.Controllers;

/// <summary>Minimal authenticated probe endpoint proving the JWT gate works.</summary>
[ApiController]
[Route("me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly YanbanDbContext _db;

    public MeController(YanbanDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<UserDto>> Get(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst("sub")!.Value);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return NotFound();

        return Ok(new UserDto(user.Id, user.Email, user.DisplayName));
    }
}
