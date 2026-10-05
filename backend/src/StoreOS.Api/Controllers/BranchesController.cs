using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Tenancy.Branches.CreateBranch;
using StoreOS.Application.Tenancy.Branches.GetBranchById;
using StoreOS.Application.Tenancy.Branches.GetBranches;
using StoreOS.Application.Tenancy.Branches.UpdateBranch;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/branches")]
public class BranchesController : ControllerBase
{
    private readonly IMediator _mediator;
    public BranchesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await _mediator.Send(new GetBranchesQuery(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBranchCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetBranchByIdQuery(id), ct));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBranchBody body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateBranchCommand(id, body.Name, body.Address, body.Phone,
            body.Email, body.Gstin, body.IsActive), ct);
        return NoContent();
    }
}

public record UpdateBranchBody(string Name, string? Address, string? Phone, string? Email, string? Gstin, bool IsActive);
