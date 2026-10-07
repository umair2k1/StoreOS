using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Repairs.Services;
using StoreOS.Application.Repairs.Services.CreateRepairService;
using StoreOS.Application.Repairs.Services.DeactivateRepairService;
using StoreOS.Application.Repairs.Services.GetRepairServiceById;
using StoreOS.Application.Repairs.Services.GetRepairServices;
using StoreOS.Application.Repairs.Services.UpdateRepairService;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/repair-services")]
public class RepairServicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public RepairServicesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? isActive = true, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await _mediator.Send(new GetRepairServicesQuery(isActive, page, pageSize), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetRepairServiceByIdQuery(id), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRepairServiceCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRepairServiceBody body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateRepairServiceCommand(
            id,
            body.Name,
            body.Description,
            body.StandardCharge,
            body.Taxable,
            body.GstRate,
            body.SacCode,
            body.EstimatedDurationMinutes,
            body.WarrantyDays,
            body.IsActive), ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateRepairServiceCommand(id), ct);
        return NoContent();
    }
}

public record UpdateRepairServiceBody(
    string? Name = null,
    string? Description = null,
    decimal? StandardCharge = null,
    bool? Taxable = null,
    decimal? GstRate = null,
    string? SacCode = null,
    int? EstimatedDurationMinutes = null,
    int? WarrantyDays = null,
    bool? IsActive = null);
