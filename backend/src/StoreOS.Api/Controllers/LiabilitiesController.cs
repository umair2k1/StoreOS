using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.FinanceOps.Liabilities.CreateLiability;
using StoreOS.Application.FinanceOps.Liabilities.GetLiabilities;
using StoreOS.Application.FinanceOps.Liabilities.GetLiabilityById;
using StoreOS.Application.FinanceOps.Liabilities.RecordRepayment;
using StoreOS.Application.FinanceOps.Liabilities.UpdateLiability;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/liabilities")]
public class LiabilitiesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LiabilitiesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GetLiabilitiesQuery query, CancellationToken ct) =>
        Ok(await _mediator.Send(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetLiabilityByIdQuery(id), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLiabilityCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLiabilityBody body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateLiabilityCommand(
            id,
            body.Title,
            body.LenderName,
            body.DueDate,
            body.Status,
            body.Notes), ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/repayments")]
    public async Task<IActionResult> RecordRepayment(Guid id, [FromBody] RecordRepaymentBody body, CancellationToken ct)
    {
        var paymentId = await _mediator.Send(new RecordRepaymentCommand(
            id,
            body.Amount,
            body.PaymentMode,
            body.PaymentDate,
            body.Notes), ct);

        return Ok(new { id = paymentId });
    }
}

public record UpdateLiabilityBody(
    string Title,
    string? LenderName = null,
    DateOnly? DueDate = null,
    string? Status = null,
    string? Notes = null);

public record RecordRepaymentBody(
    decimal Amount,
    string PaymentMode,
    DateOnly PaymentDate,
    string? Notes = null);
