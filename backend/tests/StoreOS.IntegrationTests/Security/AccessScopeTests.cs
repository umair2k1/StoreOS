using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StoreOS.Application.Abstractions;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Security;

public class AccessScopeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AccessScopeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private IAccessScope ResolveScope(IServiceScope scope, ClaimsPrincipal? principal)
    {
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext
        {
            User = principal ?? new ClaimsPrincipal(new ClaimsIdentity())
        };
        httpContextAccessor.HttpContext = httpContext;

        return scope.ServiceProvider.GetRequiredService<IAccessScope>();
    }

    [Fact]
    public void UnauthenticatedRequest_ReturnsUnauthenticatedDefaults()
    {
        using var scope = _factory.Services.CreateScope();
        var accessScope = ResolveScope(scope, null);

        accessScope.IsAuthenticated.Should().BeFalse();
        accessScope.HasTenantContext.Should().BeFalse();
        accessScope.TenantId.Should().Be(Guid.Empty);
        accessScope.UserId.Should().Be(Guid.Empty);
        accessScope.AllBranches.Should().BeFalse();
        accessScope.BranchIds.Should().BeEmpty();
        accessScope.Permissions.Should().BeEmpty();
        accessScope.TokenVersion.Should().Be(0);
        accessScope.HasBranchAccess(Guid.NewGuid()).Should().BeFalse();
        accessScope.HasPermission("pos:checkout").Should().BeFalse();
    }

    [Fact]
    public void Authenticated_SpecificBranches_HasBranchAccessTrueForClaimedOnly()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var branch1 = Guid.NewGuid();
        var branch2 = Guid.NewGuid();
        var unassignedBranch = Guid.NewGuid();

        var claims = new List<Claim>
        {
            new("tenant_id", tenantId.ToString()),
            new("sub", userId.ToString()),
            new("all_branches", "false"),
            new("branch_id", branch1.ToString()),
            new("branch_id", branch2.ToString()),
            new("perm", "pos:checkout"),
            new("perm", "inventory:cost:view"),
            new("token_version", "2")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        using var scope = _factory.Services.CreateScope();
        var accessScope = ResolveScope(scope, principal);

        accessScope.IsAuthenticated.Should().BeTrue();
        accessScope.HasTenantContext.Should().BeTrue();
        accessScope.TenantId.Should().Be(tenantId);
        accessScope.UserId.Should().Be(userId);
        accessScope.AllBranches.Should().BeFalse();
        accessScope.BranchIds.Should().HaveCount(2).And.Contain([branch1, branch2]);
        accessScope.TokenVersion.Should().Be(2);

        accessScope.HasBranchAccess(branch1).Should().BeTrue();
        accessScope.HasBranchAccess(branch2).Should().BeTrue();
        accessScope.HasBranchAccess(unassignedBranch).Should().BeFalse();

        accessScope.Permissions.Should().HaveCount(2).And.Contain(["pos:checkout", "inventory:cost:view"]);
        accessScope.HasPermission("pos:checkout").Should().BeTrue();
        accessScope.HasPermission("inventory:cost:view").Should().BeTrue();
        accessScope.HasPermission("sales:void").Should().BeFalse();
    }

    [Fact]
    public void Authenticated_AllBranchesTrue_HasBranchAccessTrueForAnyBranch()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var claims = new List<Claim>
        {
            new("tenant_id", tenantId.ToString()),
            new("sub", userId.ToString()),
            new("all_branches", "true"),
            new("token_version", "1")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        using var scope = _factory.Services.CreateScope();
        var accessScope = ResolveScope(scope, principal);

        accessScope.IsAuthenticated.Should().BeTrue();
        accessScope.HasTenantContext.Should().BeTrue();
        accessScope.TenantId.Should().Be(tenantId);
        accessScope.UserId.Should().Be(userId);
        accessScope.AllBranches.Should().BeTrue();
        accessScope.BranchIds.Should().BeEmpty();

        accessScope.HasBranchAccess(Guid.NewGuid()).Should().BeTrue();
        accessScope.HasBranchAccess(Guid.NewGuid()).Should().BeTrue();
    }

    [Fact]
    public void Unauthenticated_UseAnonymousTenant_SetsTenantIdWithoutAuthenticating()
    {
        using var scope = _factory.Services.CreateScope();
        var accessScope = ResolveScope(scope, null);
        var explicitTenantId = Guid.NewGuid();

        accessScope.UseAnonymousTenant(explicitTenantId);

        accessScope.TenantId.Should().Be(explicitTenantId);
        accessScope.HasTenantContext.Should().BeTrue();
        accessScope.IsAuthenticated.Should().BeFalse();
        accessScope.AllBranches.Should().BeFalse();
        accessScope.BranchIds.Should().BeEmpty();
    }

    [Fact]
    public void Unauthenticated_UseSystemContext_SetsTenantIdAndAllBranches()
    {
        using var scope = _factory.Services.CreateScope();
        var accessScope = ResolveScope(scope, null);
        var explicitTenantId = Guid.NewGuid();

        accessScope.UseSystemContext(explicitTenantId);

        accessScope.TenantId.Should().Be(explicitTenantId);
        accessScope.HasTenantContext.Should().BeTrue();
        accessScope.AllBranches.Should().BeTrue();
        accessScope.IsAuthenticated.Should().BeFalse();
        accessScope.HasBranchAccess(Guid.NewGuid()).Should().BeTrue();
    }

    [Fact]
    public void Authenticated_SwitchTenant_ThrowsInvalidOperationException()
    {
        var claims = new List<Claim>
        {
            new("tenant_id", Guid.NewGuid().ToString()),
            new("sub", Guid.NewGuid().ToString()),
            new("all_branches", "false")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        using var scope = _factory.Services.CreateScope();
        var accessScope = ResolveScope(scope, principal);

        var actAnon = () => accessScope.UseAnonymousTenant(Guid.NewGuid());
        actAnon.Should().Throw<InvalidOperationException>()
            .WithMessage("Authenticated scopes cannot switch tenant.");

        var actSys = () => accessScope.UseSystemContext(Guid.NewGuid());
        actSys.Should().Throw<InvalidOperationException>()
            .WithMessage("Authenticated scopes cannot switch tenant.");
    }

    [Fact]
    public void NullAccessScope_BehavesAsExpected()
    {
        var nullScope = new NullAccessScope();

        nullScope.TenantId.Should().Be(Guid.Empty);
        nullScope.UserId.Should().Be(Guid.Empty);
        nullScope.AllBranches.Should().BeFalse();
        nullScope.IsAuthenticated.Should().BeFalse();
        nullScope.HasTenantContext.Should().BeFalse();
        nullScope.BranchIds.Should().BeEmpty();
        nullScope.Permissions.Should().BeEmpty();
        nullScope.TokenVersion.Should().Be(0);
        nullScope.HasBranchAccess(Guid.NewGuid()).Should().BeFalse();
        nullScope.HasPermission("any").Should().BeFalse();

        var actAnon = () => nullScope.UseAnonymousTenant(Guid.NewGuid());
        actAnon.Should().Throw<InvalidOperationException>();

        var actSys = () => nullScope.UseSystemContext(Guid.NewGuid());
        actSys.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task StoreOsWebApplicationFactory_WithTestAuthHandler_AuthenticatesPrincipal()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var claims = new List<Claim>
        {
            new("tenant_id", tenantId.ToString()),
            new("sub", userId.ToString()),
            new("all_branches", "false"),
            new("branch_id", branchId.ToString()),
            new("perm", "pos:checkout"),
            new("token_version", "1")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, TestAuthHandler.SchemeName));
        TestAuthHandler.CurrentPrincipal.Value = principal;

        using var customFactory = new StoreOS.IntegrationTests.Fixtures.StoreOsWebApplicationFactory();
        using var scope = customFactory.Services.CreateScope();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var authService = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationService>();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        httpContextAccessor.HttpContext = httpContext;

        var authResult = await authService.AuthenticateAsync(httpContext, TestAuthHandler.SchemeName);
        authResult.Succeeded.Should().BeTrue();
        authResult.Principal.Should().NotBeNull();
        httpContext.User = authResult.Principal!;

        var accessScope = scope.ServiceProvider.GetRequiredService<IAccessScope>();
        accessScope.IsAuthenticated.Should().BeTrue();
        accessScope.TenantId.Should().Be(tenantId);
        accessScope.UserId.Should().Be(userId);
        accessScope.HasBranchAccess(branchId).Should().BeTrue();
        accessScope.HasPermission("pos:checkout").Should().BeTrue();
    }
}
