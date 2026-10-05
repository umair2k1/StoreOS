using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Identity.Login;
using StoreOS.Application.Identity.Refresh;
using StoreOS.Application.Identity;
using StoreOS.Application.Identity.Logout;
using StoreOS.Application.Identity.Password.ChangePassword;
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
    [EnableRateLimiting("login")]
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

        Response.Cookies.Append("storeos_refresh_token", result.RawRefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });

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

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("storeos_refresh_token", out var rawToken) || string.IsNullOrEmpty(rawToken))
            return Unauthorized(new { code = "missing_refresh_token", message = "No refresh token cookie present." });

        var result = await _mediator.Send(new RefreshCommand(rawToken), ct);

        if (result.ReuseDetected)
        {
            Response.Cookies.Delete("storeos_refresh_token", new CookieOptions { Path = "/api/auth" });
            return Unauthorized(new
            {
                code = "refresh_token_reuse_detected",
                message = "This refresh token has already been used. All sessions have been revoked for security. Please log in again."
            });
        }

        Response.Cookies.Append("storeos_refresh_token", result.NewRawRefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });

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

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue("storeos_refresh_token", out var rawToken)
            && !string.IsNullOrEmpty(rawToken)
            && RefreshTokenFormat.TryParseTenantId(rawToken, out _))
        {
            await _mediator.Send(new LogoutCommand(rawToken), ct);
        }

        Response.Cookies.Delete("storeos_refresh_token", new CookieOptions { Path = "/api/auth" });
        return NoContent();
    }

    [HttpGet("me")]
    public IActionResult Me([FromServices] IAccessScope scope)
    {
        if (!scope.IsAuthenticated) return Unauthorized();
        return Ok(new
        {
            userId = scope.UserId,
            tenantId = scope.TenantId,
            allBranches = scope.AllBranches,
            branchIds = scope.BranchIds,
            permissions = scope.Permissions,
            tokenVersion = scope.TokenVersion
        });
    }

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);
        Response.Cookies.Delete("storeos_refresh_token", new CookieOptions { Path = "/api/auth" });
        return NoContent();
    }
}
