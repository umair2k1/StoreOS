using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers.Crud.CreateCustomer;
using StoreOS.Application.Customers.Crud.GetCustomerById;
using StoreOS.Application.Customers.Crud.GetCustomers;
using StoreOS.Application.Customers.Crud.SetCustomerCreditLimit;
using StoreOS.Application.Customers.Crud.UpdateCustomerProfile;
using StoreOS.Application.Customers.GetCreditBalance;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly IMediator _mediator;

    public CustomersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerListItemDto>>> GetCustomers(
        [FromQuery] GetCustomersQuery query,
        CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> CreateCustomer(
        [FromBody] CreateCustomerCommand command,
        CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetCustomerById), new { id }, new { id });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDetailDto>> GetCustomerById(
        Guid id,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCustomerByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult> UpdateCustomerProfile(
        Guid id,
        [FromBody] UpdateCustomerProfileRequest request,
        CancellationToken ct)
    {
        await _mediator.Send(new UpdateCustomerProfileCommand(id, request.Name, request.Phone, request.Gstin), ct);
        return NoContent();
    }

    [HttpPatch("{id:guid}/credit-limit")]
    public async Task<ActionResult> SetCustomerCreditLimit(
        Guid id,
        [FromBody] SetCustomerCreditLimitRequest request,
        CancellationToken ct)
    {
        await _mediator.Send(new SetCustomerCreditLimitCommand(id, request.CreditLimit), ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/credit-balance")]
    public async Task<ActionResult<CreditBalanceDto>> GetCreditBalance(
        Guid id,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCreditBalanceQuery(id), ct);
        return Ok(result);
    }
}

public record UpdateCustomerProfileRequest(string Name, string? Phone = null, string? Gstin = null);
public record SetCustomerCreditLimitRequest(decimal? CreditLimit);
