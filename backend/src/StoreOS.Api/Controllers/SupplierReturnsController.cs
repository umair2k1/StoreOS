using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Suppliers.CreateReturn;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/supplier-returns")]
public class SupplierReturnsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SupplierReturnsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupplierReturnCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }
}
