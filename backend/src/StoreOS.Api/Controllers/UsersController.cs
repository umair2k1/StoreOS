using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Identity.Users.AssignBranch;
using StoreOS.Application.Identity.Users.AssignRole;
using StoreOS.Application.Identity.Users.CreateUser;
using StoreOS.Application.Identity.Users.DeactivateUser;
using StoreOS.Application.Identity.Users.GetUsers;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;
    public UsersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _mediator.Send(new GetUsersQuery(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Created($"/api/users/{id}", new { id });
    }

    [HttpPost("{id:guid}/roles")]
    public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignRoleBody body, CancellationToken ct)
    {
        await _mediator.Send(new AssignRoleCommand(id, body.RoleId, body.BranchId), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/branches")]
    public async Task<IActionResult> AssignBranch(Guid id, [FromBody] AssignBranchBody body, CancellationToken ct)
    {
        await _mediator.Send(new AssignBranchCommand(id, body.BranchId, body.IsDefault), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateUserCommand(id), ct);
        return NoContent();
    }
}

public record AssignRoleBody(Guid RoleId, Guid? BranchId = null);
public record AssignBranchBody(Guid BranchId, bool IsDefault = false);
