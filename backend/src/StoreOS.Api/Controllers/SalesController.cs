using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Sales.Search;

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
}
