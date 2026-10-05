using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Inventory.Transfers.ReceiveTransfer;
using StoreOS.Application.Inventory.Transfers.TransferOut;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/inventory/transfers")]
public class TransfersController : ControllerBase
{
    private readonly IMediator _mediator;

    public TransfersController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> TransferOut([FromBody] TransferOutCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Created($"/api/inventory/transfers/{id}", new { id });
    }

    [HttpPost("{id:guid}/receive")]
    public async Task<IActionResult> Receive(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new ReceiveTransferCommand(id), ct);
        return Ok(new { success = true });
    }
}
