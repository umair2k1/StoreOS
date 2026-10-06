using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Suppliers.CreateSupplier;
using StoreOS.Application.Suppliers.GetSupplierById;
using StoreOS.Application.Suppliers.GetSupplierPayable;
using StoreOS.Application.Suppliers.GetSuppliers;
using StoreOS.Application.Suppliers.GetSupplierTransactions;
using StoreOS.Application.Suppliers.RecordTransaction;
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

    [HttpPost("{id:guid}/transactions")]
    public async Task<IActionResult> RecordTransaction(Guid id, [FromBody] RecordSupplierTransactionBody body, CancellationToken ct)
    {
        var txnId = await _mediator.Send(new RecordSupplierTransactionCommand(
            id,
            body.Type,
            body.Amount,
            body.PaymentMode,
            body.ReferenceNo,
            body.TxnDate,
            body.Notes), ct);
        return Ok(new { id = txnId });
    }

    [HttpGet("{id:guid}/ledger")]
    [HttpGet("{id:guid}/payable")]
    public async Task<IActionResult> GetPayable(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetSupplierPayableQuery(id), ct));

    [HttpGet("{id:guid}/transactions")]
    public async Task<IActionResult> GetTransactions(
        Guid id,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetSupplierTransactionsQuery(
            id,
            fromDate,
            toDate,
            type,
            page,
            pageSize), ct);
        return Ok(result);
    }
}

public record UpdateSupplierBody(
    string Name,
    string? Phone = null,
    string? Address = null,
    string? BrandOrCategory = null,
    string? Notes = null);

public record RecordSupplierTransactionBody(
    string Type,
    decimal Amount,
    string? PaymentMode = null,
    string? ReferenceNo = null,
    DateOnly TxnDate = default,
    string? Notes = null);
