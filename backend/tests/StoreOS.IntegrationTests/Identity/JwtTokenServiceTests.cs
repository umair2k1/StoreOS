using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using StoreOS.Application.Abstractions;

namespace StoreOS.IntegrationTests.Identity;

public class JwtTokenServiceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public JwtTokenServiceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void IssueAccessToken_DirectTokenDecoding_AllClaimsAndExpirationMatch()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var branch1 = Guid.NewGuid();
        var branch2 = Guid.NewGuid();
        var permissions = new[] { "pos:checkout", "inventory:read" };
        var tokenVersion = 3;

        var issuedAt = DateTimeOffset.UtcNow;
        var (tokenString, expiresAt) = tokenService.IssueAccessToken(
            userId, tenantId, allBranches: false, new[] { branch1, branch2 }, permissions, tokenVersion);

        tokenString.Should().NotBeNullOrWhiteSpace();
        expiresAt.Should().BeCloseTo(issuedAt.AddMinutes(15), TimeSpan.FromSeconds(5));

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(tokenString);

        jwtToken.Issuer.Should().Be("storeos-cloud");
        jwtToken.Audiences.Should().Contain("storeos-cloud-clients");

        jwtToken.Claims.First(c => c.Type == "sub").Value.Should().Be(userId.ToString());
        jwtToken.Claims.First(c => c.Type == "tenant_id").Value.Should().Be(tenantId.ToString());
        jwtToken.Claims.First(c => c.Type == "all_branches").Value.Should().Be("false");
        jwtToken.Claims.First(c => c.Type == "token_version").Value.Should().Be("3");

        var branchClaims = jwtToken.Claims.Where(c => c.Type == "branch_id").Select(c => Guid.Parse(c.Value)).ToList();
        branchClaims.Should().BeEquivalentTo(new[] { branch1, branch2 });

        var permClaims = jwtToken.Claims.Where(c => c.Type == "perm").Select(c => c.Value).ToList();
        permClaims.Should().BeEquivalentTo(permissions);

        var expClaim = jwtToken.Claims.First(c => c.Type == JwtRegisteredClaimNames.Exp);
        var expSeconds = long.Parse(expClaim.Value);
        var expDateTime = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
        expDateTime.Should().BeCloseTo(issuedAt.AddMinutes(15), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void IssueAccessToken_WhenAllBranchesIsTrue_DoesNotIncludeBranchIdClaims()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var branch = Guid.NewGuid();

        var (tokenString, _) = tokenService.IssueAccessToken(
            userId, tenantId, allBranches: true, new[] { branch }, Array.Empty<string>(), 1);

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(tokenString);

        jwtToken.Claims.First(c => c.Type == "all_branches").Value.Should().Be("true");
        jwtToken.Claims.Should().NotContain(c => c.Type == "branch_id");
    }

    [Fact]
    public async Task RealJwtBearerAuth_WithValidToken_PopulatesAccessScopeCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var permissions = new[] { "pos:checkout", "reports:view" };
        var tokenVersion = 2;

        var (token, _) = tokenService.IssueAccessToken(
            userId, tenantId, allBranches: false, new[] { branchId }, permissions, tokenVersion);

        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        httpContext.Request.Headers.Authorization = $"Bearer {token}";
        httpContextAccessor.HttpContext = httpContext;

        var authResult = await authService.AuthenticateAsync(httpContext, JwtBearerDefaults.AuthenticationScheme);
        authResult.Succeeded.Should().BeTrue();
        authResult.Principal.Should().NotBeNull();
        httpContext.User = authResult.Principal!;

        var accessScope = scope.ServiceProvider.GetRequiredService<IAccessScope>();
        accessScope.IsAuthenticated.Should().BeTrue();
        accessScope.TenantId.Should().Be(tenantId);
        accessScope.UserId.Should().Be(userId);
        accessScope.AllBranches.Should().BeFalse();
        accessScope.BranchIds.Should().Contain(branchId);
        accessScope.HasBranchAccess(branchId).Should().BeTrue();
        accessScope.HasBranchAccess(Guid.NewGuid()).Should().BeFalse();
        accessScope.HasPermission("pos:checkout").Should().BeTrue();
        accessScope.HasPermission("reports:view").Should().BeTrue();
        accessScope.HasPermission("branches:manage").Should().BeFalse();
        accessScope.TokenVersion.Should().Be(tokenVersion);
    }

    [Fact]
    public async Task RealJwtBearerAuth_WithInvalidSignature_FailsAuthentication()
    {
        using var scope = _factory.Services.CreateScope();

        // Sign token with an untrusted key
        var untrustedKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("untrusted-signing-key-not-matching-configuration-32b!!"));
        var creds = new SigningCredentials(untrustedKey, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken("storeos-cloud", "storeos-cloud-clients",
            new[] { new Claim("sub", Guid.NewGuid().ToString()) },
            expires: DateTime.UtcNow.AddMinutes(15), signingCredentials: creds);
        var tamperedToken = new JwtSecurityTokenHandler().WriteToken(jwt);

        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        httpContext.Request.Headers.Authorization = $"Bearer {tamperedToken}";
        httpContextAccessor.HttpContext = httpContext;

        var authResult = await authService.AuthenticateAsync(httpContext, JwtBearerDefaults.AuthenticationScheme);
        authResult.Succeeded.Should().BeFalse();
    }
}
