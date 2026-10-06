using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.FinanceOps.Expenses.CreateExpense;
using StoreOS.Application.FinanceOps.Expenses.GetExpenses;
using StoreOS.Application.FinanceOps.Expenses.GetMonthlyTotal;
using StoreOS.Domain.FinanceOps;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.FinanceOps;

[Collection("postgres")]
public class ExpensesTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ExpensesTests(PostgresTestFixture fixture)
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
        var passwordHash = _hasher.Hash("Password123!");

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Expenses Test Tenant", subdomain: "exp-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "EX1", "Expenses Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "ExpenseManager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, "expenses:manage");
        db.RolePermissions.Add(perm);

        var user = User.Create(_tenantId, "expmgr@test.com", passwordHash, "Expense Mgr");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var ub = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(ub);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@uId, @rId, NULL);", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        await cmd.ExecuteNonQueryAsync();

        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_role_assignments WHERE user_id = @uId; " +
            "DELETE FROM user_branches WHERE user_id = @uId; " +
            "DELETE FROM users WHERE id = @uId; " +
            "DELETE FROM role_permissions WHERE role_id = @rId; " +
            "DELETE FROM roles WHERE id = @rId; " +
            "DELETE FROM expenses WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE id = @bId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("bId", _branchId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken()
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId, _tenantId, allBranches: true, Array.Empty<Guid>(), new[] { "expenses:manage" }, tokenVersion: 1);
        return token;
    }

    [Fact]
    public async Task CreateExpense_WithArbitraryNovelCategory_SucceedsProvingExtensibility()
    {
        var token = IssueToken();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        const string novelCategory = "Store Renovation & Custom Neon Lighting";
        ExpenseCategory.Suggested.Should().NotContain(novelCategory);

        var cmd = new CreateExpenseCommand(
            Description: "New store signage installation",
            Amount: 18500m,
            Category: novelCategory,
            ExpenseDate: DateOnly.FromDateTime(DateTime.UtcNow),
            Notes: "Vendor: Acme Signs");

        var resp = await client.PostAsJsonAsync("/api/expenses", cmd);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var expenseId = result.GetProperty("id").GetGuid();

        // Verify row in DB via superuser
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var row = await db.Expenses.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == expenseId && e.TenantId == _tenantId);

        row.Should().NotBeNull();
        row!.Category.Should().Be(novelCategory);
        row.Amount.Should().Be(18500m);
        row.Description.Should().Be("New store signage installation");
    }

    [Fact]
    public async Task CreateExpense_InvalidFields_ThrowSpecificValidationCodes()
    {
        var token = IssueToken();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 1. Empty description
        var emptyDesc = new CreateExpenseCommand("", 100m, "Rent", today);
        var resp1 = await client.PostAsJsonAsync("/api/expenses", emptyDesc);
        resp1.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body1 = await resp1.Content.ReadAsStringAsync();
        body1.Should().Contain("expense_description_required");

        // 2. Zero / negative amount
        var zeroAmount = new CreateExpenseCommand("Monthly rent", 0m, "Rent", today);
        var resp2 = await client.PostAsJsonAsync("/api/expenses", zeroAmount);
        resp2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body2 = await resp2.Content.ReadAsStringAsync();
        body2.Should().Contain("expense_invalid_amount");

        // 3. Missing date
        var missingDate = new CreateExpenseCommand("Internet bill", 1500m, "Utilities", default);
        var resp3 = await client.PostAsJsonAsync("/api/expenses", missingDate);
        resp3.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body3 = await resp3.Content.ReadAsStringAsync();
        body3.Should().Contain("expense_date_required");

        // 4. Empty category
        var emptyCat = new CreateExpenseCommand("Office supplies", 500m, "", today);
        var resp4 = await client.PostAsJsonAsync("/api/expenses", emptyCat);
        resp4.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 5. Direct domain entity validation checks
        Action act1 = () => Expense.Create(_tenantId, _branchId, "", 100m, "Rent", today, null, _userId);
        act1.Should().Throw<StoreOS.Domain.Exceptions.DomainValidationException>().Where(ex => ex.Code == "expense_description_required");

        Action act2 = () => Expense.Create(_tenantId, _branchId, "Monthly rent", 0m, "Rent", today, null, _userId);
        act2.Should().Throw<StoreOS.Domain.Exceptions.DomainValidationException>().Where(ex => ex.Code == "expense_invalid_amount");

        Action act3 = () => Expense.Create(_tenantId, _branchId, "Internet bill", 1500m, "Utilities", default, null, _userId);
        act3.Should().Throw<StoreOS.Domain.Exceptions.DomainValidationException>().Where(ex => ex.Code == "expense_date_required");

        Action act4 = () => Expense.Create(_tenantId, _branchId, "Office supplies", 500m, "", today, null, _userId);
        act4.Should().Throw<StoreOS.Domain.Exceptions.DomainValidationException>().Where(ex => ex.Code == "expense_category_required");
        var body4 = await resp4.Content.ReadAsStringAsync();
        body4.Should().Contain("expense_category_required");
    }

    [Fact]
    public async Task GetExpenses_NoDateRange_DefaultsToFirstOfMonthThroughToday()
    {
        var token = IssueToken();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        var prevMonthDate = firstOfMonth.AddDays(-10); // outside range (before)
        var nextMonthDate = firstOfMonth.AddMonths(1).AddDays(5); // outside range (after)

        // Seed 2 inside range
        await client.PostAsJsonAsync("/api/expenses", new CreateExpenseCommand("In Range 1", 1000m, "Utilities", firstOfMonth));
        await client.PostAsJsonAsync("/api/expenses", new CreateExpenseCommand("In Range 2", 2000m, "Rent", today));

        // Seed 2 outside range
        await client.PostAsJsonAsync("/api/expenses", new CreateExpenseCommand("Out Range Past", 3000m, "Salaries", prevMonthDate));
        await client.PostAsJsonAsync("/api/expenses", new CreateExpenseCommand("Out Range Future", 4000m, "Miscellaneous", nextMonthDate));

        // Query with NO date range
        var resp = await client.GetAsync("/api/expenses");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var paged = await resp.Content.ReadFromJsonAsync<PagedResult<ExpenseDto>>(JsonOpts);
        paged.Should().NotBeNull();
        paged!.TotalCount.Should().Be(2);
        paged.Items.Should().HaveCount(2);
        paged.Items.Select(x => x.Description).Should().BeEquivalentTo(new[] { "In Range 1", "In Range 2" });
    }

    [Fact]
    public async Task GetMonthlyTotal_CorrectlySumsOnlyCurrentMonthExpenses()
    {
        var token = IssueToken();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        var prevMonthDate = firstOfMonth.AddDays(-5);
        var nextMonthDate = firstOfMonth.AddMonths(1).AddDays(5);

        // Seed current month: 3500 + 1500 = 5000
        await client.PostAsJsonAsync("/api/expenses", new CreateExpenseCommand("Cur 1", 3500m, "Utilities", firstOfMonth));
        await client.PostAsJsonAsync("/api/expenses", new CreateExpenseCommand("Cur 2", 1500m, "Miscellaneous", today));

        // Seed other months
        await client.PostAsJsonAsync("/api/expenses", new CreateExpenseCommand("Prev Month", 8000m, "Rent", prevMonthDate));
        await client.PostAsJsonAsync("/api/expenses", new CreateExpenseCommand("Next Month", 6000m, "Salaries", nextMonthDate));

        // Request monthly total without query params (defaults to current month)
        var resp = await client.GetAsync("/api/expenses/monthly-total");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await resp.Content.ReadFromJsonAsync<MonthlyExpenseTotalDto>(JsonOpts);
        result.Should().NotBeNull();
        result!.Year.Should().Be(today.Year);
        result.Month.Should().Be(today.Month);
        result.TotalAmount.Should().Be(5000m);
    }
}
