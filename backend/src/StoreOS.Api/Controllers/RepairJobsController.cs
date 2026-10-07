using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Repairs.Jobs;
using StoreOS.Application.Repairs.Jobs.CreateRepairJob;
using StoreOS.Application.Repairs.Jobs.GetRepairJobById;
using StoreOS.Application.Repairs.Jobs.GetRepairJobs;
using StoreOS.Application.Repairs.Jobs.UpdateRepairJob;
using StoreOS.Application.Repairs.Jobs.UpdateRepairJobCommercials;
using StoreOS.Application.Repairs.Jobs.UpdateRepairJobDiagnosis;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/repair-jobs")]
public class RepairJobsController : ControllerBase
{
    private readonly IMediator _mediator;

    public RepairJobsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GetRepairJobsQuery query, CancellationToken ct) =>
        Ok(await _mediator.Send(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetRepairJobByIdQuery(id), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRepairJobCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRepairJobBody body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateRepairJobCommand(
            id,
            body.CustomerNotes,
            body.ProblemDescription,
            body.DeviceCondition,
            body.AccessoriesReceived,
            body.ExpectedCompletion), ct);

        return NoContent();
    }

    [HttpPatch("{id:guid}/diagnosis")]
    public async Task<IActionResult> UpdateDiagnosis(Guid id, [FromBody] UpdateRepairJobDiagnosisBody body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateRepairJobDiagnosisCommand(
            id,
            body.Diagnosis,
            body.TechnicianNotes,
            body.InternalNotes,
            body.Technician), ct);

        return NoContent();
    }

    [HttpPatch("{id:guid}/commercials")]
    public async Task<IActionResult> UpdateCommercials(Guid id, [FromBody] UpdateRepairJobCommercialsBody body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateRepairJobCommercialsCommand(
            id,
            body.EstimatedCost,
            body.ApprovedCost,
            body.PartsCost,
            body.LaborCharge,
            body.Discount,
            body.Tax,
            body.FinalTotal,
            body.AdvancePaid), ct);

        return NoContent();
    }
}

public record UpdateRepairJobBody(
    string? CustomerNotes = null,
    string? ProblemDescription = null,
    string? DeviceCondition = null,
    string? AccessoriesReceived = null,
    DateTimeOffset? ExpectedCompletion = null);

public record UpdateRepairJobDiagnosisBody(
    string? Diagnosis = null,
    string? TechnicianNotes = null,
    string? InternalNotes = null,
    string? Technician = null);

public record UpdateRepairJobCommercialsBody(
    decimal? EstimatedCost = null,
    decimal? ApprovedCost = null,
    decimal? PartsCost = null,
    decimal? LaborCharge = null,
    decimal? Discount = null,
    decimal? Tax = null,
    decimal? FinalTotal = null,
    decimal? AdvancePaid = null);
