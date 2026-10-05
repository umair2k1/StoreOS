using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StoreOS.IntegrationTests.Security;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";
    public static readonly AsyncLocal<ClaimsPrincipal?> CurrentPrincipal = new();

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var principal = CurrentPrincipal.Value;

        if (principal == null && Request.Headers.TryGetValue("X-Test-Auth", out var testAuthHeader) && testAuthHeader == "true")
        {
            var claims = new List<Claim>();
            if (Request.Headers.TryGetValue("X-Test-Tenant-Id", out var tenantId))
                claims.Add(new Claim("tenant_id", tenantId.ToString()));
            if (Request.Headers.TryGetValue("X-Test-User-Id", out var userId))
                claims.Add(new Claim("sub", userId.ToString()));
            if (Request.Headers.TryGetValue("X-Test-Perms", out var perms))
            {
                foreach (var p in perms.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    claims.Add(new Claim("perm", p));
            }
            if (Request.Headers.TryGetValue("X-Test-Token-Version", out var tv))
                claims.Add(new Claim("token_version", tv.ToString()));
            if (Request.Headers.TryGetValue("X-Test-All-Branches", out var allBranches))
                claims.Add(new Claim("all_branches", allBranches.ToString()));
            else
                claims.Add(new Claim("all_branches", "true"));

            principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        }

        if (principal == null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
