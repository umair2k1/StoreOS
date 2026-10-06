using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Sales.Search;
using StoreOS.Application.Sales.GetSaleById;
using StoreOS.Application.Behaviors;
using StoreOS.Application.Sales.RecordPayment;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/sales")]
public class SalesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SalesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<SaleSummaryDto>>> GetSales(
        [FromQuery] GetSalesQuery query,
        CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleDetailDto>> GetSaleById(
        Guid id,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSaleByIdQuery(id), ct);
        return Ok(result);
    }

    public record RecordPaymentBody(decimal Amount, string PaymentMode);

    [HttpPost("{id:guid}/payments")]
    public async Task<ActionResult<RecordPaymentResult>> RecordPayment(
        Guid id,
        [FromBody] RecordPaymentBody body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromServices] IdempotencyReplayFlag replayFlag,
        CancellationToken ct)
    {
        var command = new RecordPaymentCommand(id, body.Amount, body.PaymentMode)
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
