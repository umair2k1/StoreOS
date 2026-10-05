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
}
