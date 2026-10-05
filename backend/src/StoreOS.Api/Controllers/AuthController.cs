using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Identity.Login;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantResolver _tenantResolver;

    public AuthController(IMediator mediator, ITenantResolver tenantResolver)
    {
        _mediator = mediator;
        _tenantResolver = tenantResolver;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken ct)
    {
        if (command.TenantId == Guid.Empty)
        {
            var profile = await _tenantResolver.ResolveFromHostAsync(Request.Host.Value ?? string.Empty, ct);
            if (profile is null || profile.TenantId == Guid.Empty)
            {
                throw new DomainException("tenant_not_found", "Tenant could not be resolved from host or request.");
            }

            command = command with { TenantId = profile.TenantId };
        }

        var result = await _mediator.Send(command, ct);
        return Ok(new
        {
            accessToken = result.AccessToken,
            expiresAt = result.ExpiresAt,
            userId = result.UserId,
            displayName = result.DisplayName,
            tenantId = result.TenantId,
            allBranches = result.AllBranches,
            branchIds = result.BranchIds,
            permissions = result.Permissions
        });
    }
}
