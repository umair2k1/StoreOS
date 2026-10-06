using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.FinanceOps.Expenses.CreateExpense;
using StoreOS.Application.FinanceOps.Expenses.GetExpenses;
using StoreOS.Application.FinanceOps.Expenses.GetMonthlyTotal;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/expenses")]
public class ExpensesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ExpensesController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateExpenseCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Ok(new { id });
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? category,
        [FromQuery] Guid? branchId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetExpensesQuery(from, to, category, branchId, page, pageSize), ct);
        return Ok(result);
    }

    [HttpGet("monthly-total")]
    public async Task<IActionResult> GetMonthlyTotal(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] Guid? branchId,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMonthlyTotalQuery(year, month, branchId), ct);
        return Ok(result);
    }
}
