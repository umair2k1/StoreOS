using FluentAssertions;
using StoreOS.Domain.Identity;

namespace StoreOS.Domain.Tests.Identity;

public class PermissionCatalogTests
{
    [Fact]
    public void All_Contains_Exactly_17_Permissions()
    {
        PermissionCatalog.All.Should().HaveCount(17);
    }

    [Fact]
    public void All_Permissions_Match_Plan_Spec_Exactly()
    {
        var expected = new[]
        {
            "pos:checkout",
            "sales:discount:apply",
            "sales:void",
            "sales:refund",
            "sales:credit:override",
            "sales:read:any-branch",
            "inventory:cost:view",
            "reports:profit:view",
            "inventory:manage",
            "purchases:manage",
            "inventory:adjustments",
            "expenses:manage",
            "liabilities:manage",
            "repairs:manage",
            "settings:manage",
            "users:manage",
            "branches:manage"
        };

        PermissionCatalog.All.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());

        PermissionCatalog.PosCheckout.Should().Be("pos:checkout");
        PermissionCatalog.SalesDiscountApply.Should().Be("sales:discount:apply");
        PermissionCatalog.SalesVoid.Should().Be("sales:void");
        PermissionCatalog.SalesRefund.Should().Be("sales:refund");
        PermissionCatalog.SalesCreditOverride.Should().Be("sales:credit:override");
        PermissionCatalog.SalesReadAnyBranch.Should().Be("sales:read:any-branch");
        PermissionCatalog.InventoryCostView.Should().Be("inventory:cost:view");
        PermissionCatalog.ReportsProfitView.Should().Be("reports:profit:view");
        PermissionCatalog.InventoryManage.Should().Be("inventory:manage");
        PermissionCatalog.PurchasesManage.Should().Be("purchases:manage");
        PermissionCatalog.InventoryAdjustments.Should().Be("inventory:adjustments");
        PermissionCatalog.ExpensesManage.Should().Be("expenses:manage");
        PermissionCatalog.LiabilitiesManage.Should().Be("liabilities:manage");
        PermissionCatalog.RepairsManage.Should().Be("repairs:manage");
        PermissionCatalog.SettingsManage.Should().Be("settings:manage");
        PermissionCatalog.UsersManage.Should().Be("users:manage");
        PermissionCatalog.BranchesManage.Should().Be("branches:manage");
    }

    [Theory]
    [InlineData("pos:checkout", true)]
    [InlineData("sales:discount:apply", true)]
    [InlineData("sales:void", true)]
    [InlineData("sales:refund", true)]
    [InlineData("sales:credit:override", true)]
    [InlineData("sales:read:any-branch", true)]
    [InlineData("inventory:cost:view", true)]
    [InlineData("reports:profit:view", true)]
    [InlineData("inventory:manage", true)]
    [InlineData("purchases:manage", true)]
    [InlineData("inventory:adjustments", true)]
    [InlineData("expenses:manage", true)]
    [InlineData("liabilities:manage", true)]
    [InlineData("repairs:manage", true)]
    [InlineData("settings:manage", true)]
    [InlineData("users:manage", true)]
    [InlineData("branches:manage", true)]
    [InlineData("not:a:real:permission", false)]
    [InlineData("", false)]
    [InlineData("admin:all", false)]
    public void IsValid_ReturnsExpectedResult(string code, bool expected)
    {
        PermissionCatalog.IsValid(code).Should().Be(expected);
    }
}
