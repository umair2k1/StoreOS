using MediatR;
using Microsoft.AspNetCore.Mvc;
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

    [HttpGet("{id:guid}/credit-balance")]
    public async Task<ActionResult<CreditBalanceDto>> GetCreditBalance(
        Guid id,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCreditBalanceQuery(id), ct);
        return Ok(result);
    }
}
