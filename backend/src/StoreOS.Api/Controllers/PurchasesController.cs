using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Purchasing.GetPurchases;
using StoreOS.Application.Purchasing.StockIn;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/purchases")]
public class PurchasesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PurchasesController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> StockIn([FromBody] StockInCommand command, CancellationToken ct) =>
        Ok(await _mediator.Send(command, ct));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GetPurchasesQuery query, CancellationToken ct) =>
        Ok(await _mediator.Send(query, ct));
}
