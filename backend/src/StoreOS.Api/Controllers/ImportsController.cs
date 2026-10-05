using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Purchasing.ImportSupplierInvoice;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/imports")]
public class ImportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ImportsController(IMediator mediator) => _mediator = mediator;

    [HttpPost("supplier-invoice")]
    public async Task<IActionResult> ImportSupplierInvoice([FromBody] ImportSupplierInvoiceCommand command, CancellationToken ct) =>
        Ok(await _mediator.Send(command, ct));
}
