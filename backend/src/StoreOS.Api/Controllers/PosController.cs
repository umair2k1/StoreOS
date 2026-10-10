using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Sales.Search;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/pos")]
public class PosController : ControllerBase
{
    private readonly IMediator _mediator;

    public PosController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<PosSearchResultDto>>> Search([FromQuery] string? q, CancellationToken ct)
    {
        var result = await _mediator.Send(new PosSearchQuery(q ?? string.Empty), ct);
        return Ok(result);
    }

    [HttpGet("product/{id:guid}")]
    public async Task<ActionResult<PosProductDetailDto>> GetProduct(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPosProductQuery(id), ct);
        return Ok(result);
    }

    [HttpPost("checkout")]
    [ProducesResponseType(typeof(StoreOS.Application.Sales.Checkout.CheckoutResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StoreOS.Application.Sales.Checkout.CheckoutResult), StatusCodes.Status201Created)]
    public async Task<ActionResult<StoreOS.Application.Sales.Checkout.CheckoutResult>> Checkout(
        [FromBody] StoreOS.Application.Sales.Checkout.CheckoutCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromServices] StoreOS.Application.Behaviors.IdempotencyReplayFlag replayFlag,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new
            {
                code = "idempotency_key_required",
                message = "POST /pos/checkout requires an Idempotency-Key header."
            });
        }

        var cmdWithKey = command with { IdempotencyKey = idempotencyKey };
        var result = await _mediator.Send(cmdWithKey, ct);

        if (replayFlag.Replayed)
        {
            Response.Headers["Idempotent-Replayed"] = "true";
        }

        return Created($"/api/sales/{result.SaleId}", result);
    }

    public record CreateEstimateBody(
        IReadOnlyList<StoreOS.Application.Sales.Checkout.CheckoutCartLine> Lines,
        Guid? CustomerId,
        string? WalkInCustomerName,
        decimal InvoiceDiscount,
        IReadOnlyList<StoreOS.Application.Sales.Checkout.CheckoutPayment> Payments,
        string? Description);

    [HttpPost("estimate")]
    [ProducesResponseType(typeof(StoreOS.Application.Sales.Checkout.CheckoutResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StoreOS.Application.Sales.Checkout.CheckoutResult), StatusCodes.Status201Created)]
    public async Task<ActionResult<StoreOS.Application.Sales.Checkout.CheckoutResult>> CreateEstimate(
        [FromBody] CreateEstimateBody body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromServices] StoreOS.Application.Behaviors.IdempotencyReplayFlag replayFlag,
        CancellationToken ct)
    {
        var command = new StoreOS.Application.Sales.Checkout.CheckoutCommand(
            body.Lines,
            body.CustomerId,
            body.WalkInCustomerName,
            body.InvoiceDiscount,
            StoreOS.Domain.Sales.InvoiceType.Estimate,
            body.Payments,
            Finance: null,
            body.Description)
        {
            IdempotencyKey = idempotencyKey,
            IdempotencyRequired = false
        };

        var result = await _mediator.Send(command, ct);

        if (replayFlag.Replayed)
        {
            Response.Headers["Idempotent-Replayed"] = "true";
        }

        return Created($"/api/sales/{result.SaleId}", result);
    }

    public record ConvertEstimateBody(IReadOnlyList<StoreOS.Application.Sales.Checkout.CheckoutPayment>? Payments);

    [HttpPost("estimate/{id:guid}/convert")]
    public async Task<IActionResult> ConvertEstimate(
        Guid id,
        [FromBody] ConvertEstimateBody? body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromServices] StoreOS.Application.Behaviors.IdempotencyReplayFlag replayFlag,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new
            {
                code = "idempotency_key_required",
                message = "POST /pos/estimate/{id}/convert requires an Idempotency-Key header."
            });
        }

        var payments = body?.Payments ?? Array.Empty<StoreOS.Application.Sales.Checkout.CheckoutPayment>();
        var command = new StoreOS.Application.Sales.ConvertEstimate.ConvertEstimateCommand(id, payments)
        {
            IdempotencyKey = idempotencyKey
        };

        var result = await _mediator.Send(command, ct);

        if (replayFlag.Replayed)
        {
            Response.Headers["Idempotent-Replayed"] = "true";
        }

        return Created($"/api/sales/{result.NewSaleId}", new
        {
            saleId = result.NewSaleId,
            invoiceNumber = result.InvoiceNumber,
            grandTotal = result.GrandTotal,
            amountPaid = result.AmountPaid,
            balance = result.Balance
        });
    }
}
