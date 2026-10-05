using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using StoreOS.Api.Controllers;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Identity.Login;
using StoreOS.Application.Identity.Password.AdminResetPassword;
using StoreOS.Application.Identity.Password.ChangePassword;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Identity;

[Collection("postgres")]
public class PasswordTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branch1Id = Guid.NewGuid();

    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly Guid _ownerRoleId = Guid.NewGuid();

    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _managerRoleId = Guid.NewGuid();

    private readonly Guid _cashierUserId = Guid.NewGuid();
    private readonly Guid _cashierRoleId = Guid.NewGuid();

    private const string OwnerInitialPassword = "OwnerPassword123!";
    private const string ManagerInitialPassword = "ManagerPassword123!";
    private const string CashierInitialPassword = "CashierPassword123!";

    public PasswordTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _fixture.AppConnectionString);
            builder.UseSetting("ConnectionStrings:Reporting", _fixture.AppConnectionString);
        });

        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Password Test Tenant", subdomain: "pwt-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch1 = Branch.Create(_tenantId, "B01", "HQ Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch1, _branch1Id);
        db.Branches.Add(branch1);

        // 1. Owner Role (with users:manage) & User
        var ownerRole = Role.Create(_tenantId, "Owner");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(ownerRole, _ownerRoleId);
        db.Roles.Add(ownerRole);

        db.RolePermissions.Add(RolePermission.Create(_ownerRoleId, "users:manage"));
        db.RolePermissions.Add(RolePermission.Create(_ownerRoleId, "branches:manage"));

        var ownerUser = User.Create(_tenantId, "owner@pwt.com", _hasher.Hash(OwnerInitialPassword), "Owner User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(ownerUser, _ownerUserId);
        db.Users.Add(ownerUser);

        db.UserBranches.Add(UserBranch.Create(_ownerUserId, _branch1Id, isDefault: true));

        // 2. Branch Manager Role (with users:manage) & User
        var managerRole = Role.Create(_tenantId, "BranchManager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(managerRole, _managerRoleId);
        db.Roles.Add(managerRole);

        db.RolePermissions.Add(RolePermission.Create(_managerRoleId, "users:manage"));

        var managerUser = User.Create(_tenantId, "manager@pwt.com", _hasher.Hash(ManagerInitialPassword), "Manager User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(managerUser, _managerUserId);
        db.Users.Add(managerUser);

        db.UserBranches.Add(UserBranch.Create(_managerUserId, _branch1Id, isDefault: true));

        // 3. Cashier Role (with pos:checkout, no users:manage) & User
        var cashierRole = Role.Create(_tenantId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(cashierRole, _cashierRoleId);
        db.Roles.Add(cashierRole);

        db.RolePermissions.Add(RolePermission.Create(_cashierRoleId, "pos:checkout"));

        var cashierUser = User.Create(_tenantId, "cashier@pwt.com", _hasher.Hash(CashierInitialPassword), "Cashier User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(cashierUser, _cashierUserId);
        db.Users.Add(cashierUser);

        db.UserBranches.Add(UserBranch.Create(_cashierUserId, _branch1Id, isDefault: true));

        await db.SaveChangesAsync();

        // Raw SQL inserts for user_role_assignments:
        // Owner has tenant-wide role assignment (branch_id = null)
        // Manager has branch-scoped assignment (branch_id = _branch1Id)
        // Cashier has branch-scoped assignment (branch_id = _branch1Id)
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES " +
            "(@oId, @orId, NULL), " +
            "(@mId, @mrId, @b1Id), " +
            "(@cId, @crId, @b1Id);", conn);
        cmd.Parameters.AddWithValue("oId", _ownerUserId);
        cmd.Parameters.AddWithValue("orId", _ownerRoleId);
        cmd.Parameters.AddWithValue("mId", _managerUserId);
        cmd.Parameters.AddWithValue("mrId", _managerRoleId);
        cmd.Parameters.AddWithValue("cId", _cashierUserId);
        cmd.Parameters.AddWithValue("crId", _cashierRoleId);
        cmd.Parameters.AddWithValue("b1Id", _branch1Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM audit_log WHERE tenant_id = @tId; " +
            "DELETE FROM user_branches WHERE user_id IN (SELECT id FROM users WHERE tenant_id = @tId); " +
            "DELETE FROM user_role_assignments WHERE user_id IN (SELECT id FROM users WHERE tenant_id = @tId); " +
            "DELETE FROM role_permissions WHERE role_id IN (SELECT id FROM roles WHERE tenant_id = @tId); " +
            "DELETE FROM roles WHERE tenant_id = @tId; " +
            "DELETE FROM refresh_tokens WHERE user_id IN (SELECT id FROM users WHERE tenant_id = @tId); " +
            "DELETE FROM users WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE tenant_id = @tId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<(string AccessToken, string RefreshToken)> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginCommand(email, password, _tenantId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = json.GetProperty("accessToken").GetString()!;

        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var cookieHeader = cookies!.First(c => c.StartsWith("storeos_refresh_token="));
        var refreshToken = cookieHeader.Split(';')[0].Substring("storeos_refresh_token=".Length);

        return (accessToken, refreshToken);
    }

    [Fact]
    public async Task CaseA_ChangePassword_WithCorrectPassword_Succeeds_AndOldPasswordNoLongerLogsIn()
    {
        var (accessToken, refreshToken) = await LoginAsync("cashier@pwt.com", CashierInitialPassword);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordCommand(CashierInitialPassword, "NewCashierPassword456!"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Cookie", $"storeos_refresh_token={refreshToken}");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify Set-Cookie clears the refresh token
        response.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
        var clearedCookie = setCookies!.FirstOrDefault(c => c.StartsWith("storeos_refresh_token="));
        clearedCookie.Should().NotBeNull();

        // Old password no longer logs in
        var oldLoginResp = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginCommand("cashier@pwt.com", CashierInitialPassword, _tenantId));
        oldLoginResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // New password logs in successfully
        var newLoginResp = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginCommand("cashier@pwt.com", "NewCashierPassword456!", _tenantId));
        newLoginResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CaseB_ChangePassword_WithWrongPassword_Returns422CurrentPasswordIncorrect()
    {
        var (accessToken, refreshToken) = await LoginAsync("cashier@pwt.com", CashierInitialPassword);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordCommand("WrongPassword999!", "NewCashierPassword456!"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Cookie", $"storeos_refresh_token={refreshToken}");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("current_password_incorrect");
    }

    [Fact]
    public async Task CaseC_ChangePassword_InvalidatesOldAccessTokenAndOldRefreshCookie()
    {
        var (oldAccessToken, oldRefreshToken) = await LoginAsync("cashier@pwt.com", CashierInitialPassword);

        // Change password
        var changeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordCommand(CashierInitialPassword, "BrandNewPassword789!"))
        };
        changeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oldAccessToken);
        changeRequest.Headers.Add("Cookie", $"storeos_refresh_token={oldRefreshToken}");

        var changeResponse = await _client.SendAsync(changeRequest);
        changeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Directly evict cache to simulate token_version staleness detection
        var cache = _factory.Services.GetRequiredService<IMemoryCache>();
        cache.Remove($"token_version:{_cashierUserId}");

        // Attempting to refresh with the old refresh cookie returns 401
        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", $"storeos_refresh_token={oldRefreshToken}");
        var refreshResponse = await _client.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CaseD_AdminReset_WithoutUsersManage_Returns403Forbidden()
    {
        var (cashierToken, _) = await LoginAsync("cashier@pwt.com", CashierInitialPassword);

        // Cashier attempts to reset manager password
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{_managerUserId}/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordBody("NewTempPassword123!"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branch1Id.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CaseE_AdminReset_BranchManagerCannotResetOwner_Returns403Forbidden()
    {
        var (managerToken, _) = await LoginAsync("manager@pwt.com", ManagerInitialPassword);
        var (ownerToken, _) = await LoginAsync("owner@pwt.com", OwnerInitialPassword);

        // 1. Branch Manager attempts to reset Owner -> 403 Forbidden
        var managerResetOwnerReq = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{_ownerUserId}/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordBody("NewOwnerTemp123!"))
        };
        managerResetOwnerReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        managerResetOwnerReq.Headers.Add(BranchContextMiddleware.HeaderName, _branch1Id.ToString());

        var managerResetResp = await _client.SendAsync(managerResetOwnerReq);
        managerResetResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Owner CAN reset Cashier -> 204 NoContent
        var ownerResetCashierReq = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{_cashierUserId}/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordBody("CashierTempPass123!"))
        };
        ownerResetCashierReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        ownerResetCashierReq.Headers.Add(BranchContextMiddleware.HeaderName, _branch1Id.ToString());

        var ownerResetResp = await _client.SendAsync(ownerResetCashierReq);
        ownerResetResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Cashier can now log in with temporary password
        var cashierLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginCommand("cashier@pwt.com", "CashierTempPass123!", _tenantId));
        cashierLogin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CaseF_BothOperations_WriteAuditRow_WithoutPasswordMaterial()
    {
        var (ownerToken, _) = await LoginAsync("owner@pwt.com", OwnerInitialPassword);

        // 1. Change password
        var changeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordCommand(OwnerInitialPassword, "UpdatedOwnerPass999!"))
        };
        changeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var changeResp = await _client.SendAsync(changeReq);
        changeResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 2. Reset password
        var (newOwnerToken, _) = await LoginAsync("owner@pwt.com", "UpdatedOwnerPass999!");
        var resetReq = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{_cashierUserId}/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordBody("NewCashierTemp123!"))
        };
        resetReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newOwnerToken);
        resetReq.Headers.Add(BranchContextMiddleware.HeaderName, _branch1Id.ToString());
        var resetResp = await _client.SendAsync(resetReq);
        resetResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 3. Inspect audit_log table
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT action, changes FROM audit_log WHERE entity_type = 'user';", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var actions = new List<string>();
        var changes = new List<string>();
        while (await reader.ReadAsync())
        {
            actions.Add(reader.GetString(0));
            changes.Add(reader.GetString(1));
        }

        actions.Should().Contain("user.password.changed");
        actions.Should().Contain("user.password.reset");

        // Confirm NO password material in changes JSON
        foreach (var change in changes)
        {
            change.Should().NotContain("OwnerPassword");
            change.Should().NotContain("UpdatedOwnerPass");
            change.Should().NotContain("NewCashierTemp");
            change.Should().NotContain("CashierPassword");
        }
    }

    [Fact]
    public async Task CaseG_WeakPasswords_RejectedWithValidationErrors400()
    {
        var (ownerToken, _) = await LoginAsync("owner@pwt.com", OwnerInitialPassword);

        // 1. Short password (< 10 chars)
        var shortReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordCommand(OwnerInitialPassword, "Short1!"))
        };
        shortReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var shortResp = await _client.SendAsync(shortReq);
        shortResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. No digit
        var noDigitReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordCommand(OwnerInitialPassword, "NoDigitsPasswordHere!"))
        };
        noDigitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var noDigitResp = await _client.SendAsync(noDigitReq);
        noDigitResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 3. No letter
        var noLetterReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordCommand(OwnerInitialPassword, "1234567890123!"))
        };
        noLetterReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var noLetterResp = await _client.SendAsync(noLetterReq);
        noLetterResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 4. Admin reset with weak password
        var adminWeakReq = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{_cashierUserId}/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordBody("weak"))
        };
        adminWeakReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        adminWeakReq.Headers.Add(BranchContextMiddleware.HeaderName, _branch1Id.ToString());
        var adminWeakResp = await _client.SendAsync(adminWeakReq);
        adminWeakResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
