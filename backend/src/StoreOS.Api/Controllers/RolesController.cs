using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Identity.Roles.CreateRole;
using StoreOS.Application.Identity.Roles.GetRoles;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/roles")]
public class RolesController : ControllerBase
{
    private readonly IMediator _mediator;
    public RolesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _mediator.Send(new GetRolesQuery(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoleCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Created($"/api/roles/{id}", new { id });
    }
}
