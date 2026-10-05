using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Suppliers.CreateSupplier;
using StoreOS.Application.Suppliers.GetSupplierById;
using StoreOS.Application.Suppliers.GetSuppliers;
using StoreOS.Application.Suppliers.UpdateSupplier;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
public class SuppliersController : ControllerBase
{
    private readonly IMediator _mediator;

    public SuppliersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GetSuppliersQuery query, CancellationToken ct) =>
        Ok(await _mediator.Send(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetSupplierByIdQuery(id), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupplierCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSupplierBody body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateSupplierCommand(
            id,
            body.Name,
            body.Phone,
            body.Address,
            body.BrandOrCategory,
            body.Notes), ct);
        return NoContent();
    }
}

public record UpdateSupplierBody(
    string Name,
    string? Phone = null,
    string? Address = null,
    string? BrandOrCategory = null,
    string? Notes = null);
