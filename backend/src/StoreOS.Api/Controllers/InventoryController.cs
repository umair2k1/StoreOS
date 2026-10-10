using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Inventory.Queries;
using StoreOS.Application.Inventory.Adjustments;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/inventory")]
public class InventoryController : ControllerBase
{
    private readonly IMediator _mediator;

    public InventoryController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GetInventoryQuery query, CancellationToken ct) =>
        Ok(await _mediator.Send(query, ct));

    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Categories(CancellationToken ct) =>
        Ok(await _mediator.Send(new GetInventoryCategoriesQuery(), ct));

    [HttpGet("low-stock")]
    public async Task<IActionResult> LowStock([FromQuery] GetLowStockQuery query, CancellationToken ct) =>
        Ok(await _mediator.Send(query, ct));

    [HttpGet("valuation")]
    public async Task<IActionResult> Valuation([FromQuery] GetValuationQuery query, CancellationToken ct) =>
        Ok(await _mediator.Send(query, ct));

    [HttpPost("adjustments")]
    public async Task<IActionResult> AdjustStock([FromBody] AdjustStockCommand command, CancellationToken ct) =>
        Ok(await _mediator.Send(command, ct));
}
