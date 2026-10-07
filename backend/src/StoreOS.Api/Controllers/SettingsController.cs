using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Settings.FinanceProviders;
using StoreOS.Application.Settings.FinanceProviders.AddFinanceProvider;
using StoreOS.Application.Settings.FinanceProviders.GetFinanceProviders;
using StoreOS.Application.Settings.FinanceProviders.RemoveFinanceProvider;
using StoreOS.Application.Settings.RepairFeature;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SettingsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("finance-providers")]
    public async Task<ActionResult<IReadOnlyList<FinanceProviderDto>>> GetFinanceProviders(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetFinanceProvidersQuery(), ct);
        return Ok(result);
    }

    public record AddFinanceProviderRequest(string DisplayName);

    [HttpPost("finance-providers")]
    public async Task<IActionResult> AddFinanceProvider([FromBody] AddFinanceProviderRequest request, CancellationToken ct)
    {
        await _mediator.Send(new AddFinanceProviderCommand(request.DisplayName), ct);
        return Ok();
    }

    [HttpDelete("{normalizedName}")]
    public async Task<IActionResult> RemoveFinanceProvider(string normalizedName, CancellationToken ct)
    {
        await _mediator.Send(new RemoveFinanceProviderCommand(normalizedName), ct);
        return NoContent();
    }

    [HttpGet("repairs-enabled")]
    public async Task<IActionResult> GetRepairsEnabled(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetRepairsEnabledQuery(), ct);
        return Ok(new { enabled = result });
    }

    [HttpPatch("repairs-enabled")]
    public async Task<IActionResult> SetRepairsEnabled([FromBody] SetRepairsEnabledBody body, CancellationToken ct)
    {
        await _mediator.Send(new SetRepairsEnabledCommand(body.Enabled), ct);
        return NoContent();
    }
}

public record SetRepairsEnabledBody(bool Enabled);
