using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Catalog.Products.CreateProduct;
using StoreOS.Application.Catalog.Products.GetProductById;
using StoreOS.Application.Catalog.Products.GetProducts;
using StoreOS.Application.Catalog.Products.UpdateProduct;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GetProductsQuery query, CancellationToken ct) =>
        Ok(await _mediator.Send(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetProductByIdQuery(id), ct));

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateProductCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductBody body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateProductCommand(
            id,
            body.Name,
            body.SellPrice,
            body.PurchasePrice,
            body.GstRate,
            body.IsMarginScheme,
            body.Description,
            body.EstimatedDurationMinutes,
            body.WarrantyDays,
            body.IsActive), ct);
        return NoContent();
    }
}

public record UpdateProductBody(
    string? Name = null,
    decimal? SellPrice = null,
    decimal? PurchasePrice = null,
    decimal? GstRate = null,
    bool? IsMarginScheme = null,
    string? Description = null,
    int? EstimatedDurationMinutes = null,
    int? WarrantyDays = null,
    bool? IsActive = null);
