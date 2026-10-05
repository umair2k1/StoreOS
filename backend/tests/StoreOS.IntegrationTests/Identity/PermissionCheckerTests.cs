using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StoreOS.Application.Identity;

namespace StoreOS.IntegrationTests.Identity;

public class PermissionCheckerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PermissionCheckerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void IsValidPermissionCode_ValidCode_ReturnsTrue()
    {
        using var scope = _factory.Services.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        checker.IsValidPermissionCode("pos:checkout").Should().BeTrue();
    }

    [Fact]
    public void IsValidPermissionCode_InvalidCode_ReturnsFalse()
    {
        using var scope = _factory.Services.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        checker.IsValidPermissionCode("not:a:real:permission").Should().BeFalse();
    }

    [Fact]
    public void AllCodes_Contains_17_Items()
    {
        using var scope = _factory.Services.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        checker.AllCodes.Should().HaveCount(17);
        checker.AllCodes.Should().Contain("pos:checkout");
        checker.AllCodes.Should().Contain("branches:manage");
    }
}
