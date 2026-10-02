using FluentAssertions;
using StoreOS.Domain.Identity;

namespace StoreOS.Domain.Tests.Identity;

public class IdentityEntityTests
{
    [Fact]
    public void User_Create_ValidParameters_NormalizesEmailAndSetsDefaults()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "  Admin@StoreOS.io  ", "argon2-hash-sample", "Store Admin");

        user.Id.Should().NotBeEmpty();
        user.TenantId.Should().Be(tenantId);
        user.Email.Should().Be("admin@storeos.io");
        user.PasswordHash.Should().Be("argon2-hash-sample");
        user.DisplayName.Should().Be("Store Admin");
        user.TokenVersion.Should().Be(1);
        user.IsActive.Should().BeTrue();
        user.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(null, "hash", "name")]
    [InlineData("", "hash", "name")]
    [InlineData("admin@test.com", null, "name")]
    [InlineData("admin@test.com", "", "name")]
    [InlineData("admin@test.com", "hash", null)]
    [InlineData("admin@test.com", "hash", "")]
    public void User_Create_MissingRequiredFields_ThrowsArgumentException(string? email, string? hash, string? name)
    {
        var act = () => User.Create(Guid.NewGuid(), email!, hash!, name!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void User_ChangePasswordHash_Valid_UpdatesHash()
    {
        var user = User.Create(Guid.NewGuid(), "user@test.com", "old-hash", "User");
        user.ChangePasswordHash("new-hash");

        user.PasswordHash.Should().Be("new-hash");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void User_ChangePasswordHash_Invalid_ThrowsArgumentException(string? invalidHash)
    {
        var user = User.Create(Guid.NewGuid(), "user@test.com", "old-hash", "User");
        var act = () => user.ChangePasswordHash(invalidHash!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void User_BumpTokenVersion_IncrementsVersion()
    {
        var user = User.Create(Guid.NewGuid(), "user@test.com", "hash", "User");
        user.BumpTokenVersion();

        user.TokenVersion.Should().Be(2);
    }

    [Fact]
    public void User_Deactivate_SetsInactiveAndBumpsTokenVersion()
    {
        var user = User.Create(Guid.NewGuid(), "user@test.com", "hash", "User");
        user.Deactivate();

        user.IsActive.Should().BeFalse();
        user.TokenVersion.Should().Be(2);
    }

    [Fact]
    public void User_Activate_SetsActive()
    {
        var user = User.Create(Guid.NewGuid(), "user@test.com", "hash", "User");
        user.Deactivate();
        user.Activate();

        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Role_Create_ValidParameters_CreatesRole()
    {
        var tenantId = Guid.NewGuid();
        var role = Role.Create(tenantId, "Manager");

        role.Id.Should().NotBeEmpty();
        role.TenantId.Should().Be(tenantId);
        role.Name.Should().Be("Manager");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Role_Create_InvalidName_ThrowsArgumentException(string? invalidName)
    {
        var act = () => Role.Create(Guid.NewGuid(), invalidName!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Role_Rename_Valid_UpdatesName()
    {
        var role = Role.Create(Guid.NewGuid(), "Old Role");
        role.Rename("New Role");

        role.Name.Should().Be("New Role");
    }

    [Fact]
    public void RolePermission_Create_ValidParameters_CreatesRolePermission()
    {
        var roleId = Guid.NewGuid();
        var rp = RolePermission.Create(roleId, "pos:checkout");

        rp.RoleId.Should().Be(roleId);
        rp.PermissionCode.Should().Be("pos:checkout");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RolePermission_Create_InvalidCode_ThrowsArgumentException(string? invalidCode)
    {
        var act = () => RolePermission.Create(Guid.NewGuid(), invalidCode!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UserBranch_Create_ValidParameters_SetsProperties()
    {
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var ub = UserBranch.Create(userId, branchId, isDefault: true);

        ub.UserId.Should().Be(userId);
        ub.BranchId.Should().Be(branchId);
        ub.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void UserBranch_SetDefault_TogglesFlag()
    {
        var ub = UserBranch.Create(Guid.NewGuid(), Guid.NewGuid(), isDefault: false);
        ub.SetDefault(true);

        ub.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void UserRoleAssignment_Create_TenantWide_BranchIdIsNull()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var ura = UserRoleAssignment.Create(userId, roleId, branchId: null);

        ura.UserId.Should().Be(userId);
        ura.RoleId.Should().Be(roleId);
        ura.BranchId.Should().BeNull();
    }

    [Fact]
    public void UserRoleAssignment_Create_BranchSpecific_BranchIdIsSet()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var ura = UserRoleAssignment.Create(userId, roleId, branchId: branchId);

        ura.UserId.Should().Be(userId);
        ura.RoleId.Should().Be(roleId);
        ura.BranchId.Should().Be(branchId);
    }
}
