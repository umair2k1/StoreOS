using FluentAssertions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Domain.Tests.Tenancy;

public class SettingTests
{
    [Fact]
    public void Create_TenantLevelSetting_SucceedsWithNullBranchId()
    {
        var tenantId = Guid.NewGuid();
        var setting = Setting.Create(tenantId, null, "default_gst_rate", "18");

        setting.TenantId.Should().Be(tenantId);
        setting.BranchId.Should().BeNull();
        setting.Key.Should().Be("default_gst_rate");
        setting.ValueJson.Should().Be("18");
    }

    [Fact]
    public void Create_BranchLevelSetting_SucceedsWithBranchId()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var setting = Setting.Create(tenantId, branchId, "invoice_series_format", "\"FY_CONSECUTIVE\"");

        setting.TenantId.Should().Be(tenantId);
        setting.BranchId.Should().Be(branchId);
        setting.Key.Should().Be("invoice_series_format");
        setting.ValueJson.Should().Be("\"FY_CONSECUTIVE\"");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrWhitespaceKey_ThrowsArgumentException(string? invalidKey)
    {
        var act = () => Setting.Create(Guid.NewGuid(), null, invalidKey!, "{}");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateValue_ValidJson_UpdatesValueJson()
    {
        var setting = Setting.Create(Guid.NewGuid(), null, "default_gst_rate", "18");
        setting.UpdateValue("12");

        setting.ValueJson.Should().Be("12");
    }
}
