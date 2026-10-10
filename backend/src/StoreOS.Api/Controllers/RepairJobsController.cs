using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Repairs.Jobs;
using StoreOS.Application.Repairs.Jobs.CreateRepairJob;
using StoreOS.Application.Repairs.Jobs.GetRepairJobById;
using StoreOS.Application.Repairs.Jobs.GetRepairJobs;
using StoreOS.Application.Repairs.Jobs.UpdateRepairJob;
using StoreOS.Application.Repairs.Jobs.UpdateRepairJobCommercials;
using StoreOS.Application.Repairs.Jobs.UpdateRepairJobDiagnosis;
using StoreOS.Application.Repairs.Jobs.ChangeStatus;
using StoreOS.Application.Repairs.Jobs.GetStatusHistory;
using StoreOS.Application.Repairs.Parts;
using StoreOS.Application.Repairs.Parts.ConsumePart;
using StoreOS.Application.Repairs.Parts.GetRepairJobParts;
using StoreOS.Application.Repairs.Parts.ReservePart;
using StoreOS.Application.Repairs.Parts.ReturnPart;
using StoreOS.Application.Repairs.Approval;
using StoreOS.Application.Repairs.Deliver;
using StoreOS.Application.Repairs.BillRepair;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Application.Behaviors;
using StoreOS.Application.Abstractions;
using StoreOS.Infrastructure.Printing;


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
    [HttpGet("{id:guid}/ticket.pdf")]
    public async Task<IActionResult> GetTicketPdf(
        Guid id,
        [FromServices] RepairPdfRenderer renderer,
        [FromServices] IAppDbContext db,
        [FromServices] IObjectStorageService storage,
        [FromServices] IAccessScope scope,
        CancellationToken ct)
    {
        var job = await _mediator.Send(new GetRepairJobByIdQuery(id), ct);
        var parts = await _mediator.Send(new GetRepairJobPartsQuery(id), ct);
        var dto = await MapToRepairPdfDto(job, parts, db, storage, scope.TenantId, ct);
        var bytes = renderer.Render(dto);
        return File(bytes, "application/pdf", $"repair-{job.JobNumber}.pdf");
    }

    private static async Task<RepairPdfDto> MapToRepairPdfDto(
        RepairJobDto job,
        IReadOnlyList<RepairJobPartDto> parts,
        IAppDbContext db,
        IObjectStorageService storage,
        Guid tenantId,
        CancellationToken ct)
    {
        var branding = await ShopBrandingHelper.ResolveAsync(job.BranchId, db, ct, storage, tenantId);

        var partLines = parts.Select(p => new RepairPartLineDto(
            PartName: p.PartName,
            Quantity: p.Quantity,
            ChargeAmount: p.ChargeAmount
        )).ToList();

        return new RepairPdfDto(
            ShopName: branding.ShopName,
            ShopAddress: branding.Address,
            ShopPhone: branding.Phone,
            JobNumber: job.JobNumber,
            ReceivedAt: job.ReceivedAt ?? job.CreatedAt,
            CustomerName: job.CustomerName ?? "Customer",
            CustomerPhone: job.CustomerPhone,
            DeviceBrand: job.DeviceBrand,
            DeviceModel: job.DeviceModel,
            SerialNumber: job.SerialNumber,
            Imei1: job.Imei1,
            ProblemDescription: job.ProblemDescription,
            Status: job.Status,
            EstimatedCost: job.EstimatedCost,
            FinalTotal: job.FinalTotal,
            AdvancePaid: job.AdvancePaid,
            BalanceDue: job.BalanceDue,
            WarrantyUntil: job.WarrantyUntil,
            Parts: partLines,
            InvoiceNumber: job.InvoiceNumber,
            LogoBytes: branding.LogoBytes
        );
    }


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

    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeRepairStatusBody body, CancellationToken ct)
    {
        await _mediator.Send(new ChangeRepairStatusCommand(id, body.Status, body.Notes), ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/status-history")]
    public async Task<IActionResult> GetStatusHistory(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetRepairStatusHistoryQuery(id), ct));

    [HttpGet("{id:guid}/parts")]
    public async Task<IActionResult> GetParts(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetRepairJobPartsQuery(id), ct));

    [HttpPost("{id:guid}/parts")]
    public async Task<IActionResult> ReservePart(Guid id, [FromBody] ReserveRepairPartBody body, CancellationToken ct)
    {
        var partId = await _mediator.Send(new ReservePartCommand(
            id,
            body.ProductId,
            body.PartName,
            body.Quantity,
            body.UnitCost,
            body.ChargeAmount), ct);

        return Ok(new { id = partId });
    }

    [HttpPost("{id:guid}/parts/{partId:guid}/consume")]
    public async Task<IActionResult> ConsumePart(Guid id, Guid partId, CancellationToken ct)
    {
        await _mediator.Send(new ConsumePartCommand(id, partId), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/parts/{partId:guid}/return")]
    public async Task<IActionResult> ReturnPart(Guid id, Guid partId, CancellationToken ct)
    {
        await _mediator.Send(new ReturnPartCommand(id, partId), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/approval")]
    public async Task<IActionResult> RecordApproval(Guid id, [FromBody] RecordApprovalBody body, CancellationToken ct)
    {
        await _mediator.Send(new RecordApprovalCommand(id, body.ApprovalState), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/deliver")]
    public async Task<IActionResult> Deliver(Guid id, [FromBody] DeliverRepairJobBody? body, CancellationToken ct)
    {
        await _mediator.Send(new DeliverRepairJobCommand(id, body?.WarrantyDaysOverride), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/bill")]
    public async Task<IActionResult> BillRepair(
        Guid id,
        [FromBody] BillRepairBody? body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromServices] IdempotencyReplayFlag replayFlag,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new
            {
                code = "idempotency_key_required",
                message = "POST /api/repair-jobs/{id}/bill requires an Idempotency-Key header."
            });
        }

        var command = new BillRepairCommand(id, body?.Payments)
        {
            IdempotencyKey = idempotencyKey
        };

        var result = await _mediator.Send(command, ct);

        if (replayFlag.Replayed)
        {
            Response.Headers["Idempotent-Replayed"] = "true";
        }

        return Ok(result);
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

public record ChangeRepairStatusBody(string Status, string? Notes = null);

public record ReserveRepairPartBody(
    Guid? ProductId,
    string PartName,
    int Quantity,
    decimal UnitCost,
    decimal ChargeAmount);

public record RecordApprovalBody(string ApprovalState);
public record DeliverRepairJobBody(int? WarrantyDaysOverride = null);
public record BillRepairBody(IReadOnlyList<CheckoutPayment>? Payments = null);
