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
using StoreOS.Application.Sales.RecordPayment;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class RecordPaymentTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();

    private string _token = string.Empty;

    public RecordPaymentTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Payment Test Tenant", subdomain: "pay-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "PB1", "Payment Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);
        db.RolePermissions.Add(RolePermission.Create(_roleId, "pos:checkout"));

        var user = User.Create(_tenantId, $"cashier@{_tenantId:N}.test", passwordHash, "Payment User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        db.UserBranches.Add(UserBranch.Create(_userId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_userId, _roleId, _branchId));

        var customer = Customer.Create(_tenantId, "Credit Customer", "9876543210", null);
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(customer, _customerId);
        customer.SetCreditLimit(100000m);
        db.Customers.Add(customer);

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();

        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId, _tenantId, allBranches: false, new[] { _branchId }, new[] { "pos:checkout" }, tokenVersion: 1);
        _token = token;
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM customer_payments WHERE tenant_id = @t; " +
            "DELETE FROM customer_ledger_entries WHERE tenant_id = @t; " +
            "DELETE FROM sale_items WHERE tenant_id = @t; " +
            "DELETE FROM sales WHERE tenant_id = @t; " +
            "DELETE FROM customers WHERE tenant_id = @t; " +
            "DELETE FROM idempotency_keys WHERE tenant_id = @t; " +
            "DELETE FROM user_role_assignments WHERE user_id = @u; " +
            "DELETE FROM user_branches WHERE user_id = @u; " +
            "DELETE FROM users WHERE tenant_id = @t; " +
            "DELETE FROM role_permissions WHERE role_id = @r; " +
            "DELETE FROM roles WHERE tenant_id = @t; " +
            "DELETE FROM branches WHERE tenant_id = @t; " +
            "DELETE FROM tenants WHERE id = @t;", conn);
        cmd.Parameters.AddWithValue("t", _tenantId);
        cmd.Parameters.AddWithValue("u", _userId);
        cmd.Parameters.AddWithValue("r", _roleId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private HttpRequestMessage CreatePaymentRequest(Guid saleId, decimal amount, string paymentMode, string? idempotencyKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/payments")
        {
            Content = JsonContent.Create(new { amount, paymentMode })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        req.Headers.Add("X-Branch-Id", _branchId.ToString());
        if (idempotencyKey != null)
        {
            req.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        return req;
    }

    [Fact]
    public async Task TwoSeparatePartialPayments_CreateDistinctRows_AndAccumulateTotals()
    {
        // 1. Seed credit sale: GrandTotal 5000, AmountPaid 0
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var sale = Sale.CreateTaxInvoice(
                _tenantId, _branchId, "INV-PAY-001", DateTimeOffset.UtcNow,
                _customerId, "Credit Customer", null,
                4237.29m, 381.36m, 381.35m, 5000m, 0m, 0m,
                PaymentMode.Credit, null, null, null, null, null, "Credit Sale", null, _userId);
            db.Sales.Add(sale);
            await db.SaveChangesAsync();
        }

        Guid saleId;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            saleId = (await db.Sales.IgnoreQueryFilters().FirstAsync(s => s.InvoiceNumber == "INV-PAY-001")).Id;
        }

        // 2. First payment: 2000m
        var key1 = Guid.NewGuid().ToString();
        using (var req1 = CreatePaymentRequest(saleId, 2000m, PaymentMode.Cash, key1))
        {
            var res1 = await _client.SendAsync(req1);
            res1.StatusCode.Should().Be(HttpStatusCode.OK);
            var result1 = await res1.Content.ReadFromJsonAsync<RecordPaymentResult>();
            result1.Should().NotBeNull();
            result1!.NewAmountPaid.Should().Be(2000m);
            result1.NewBalance.Should().Be(3000m);
        }

        // Verify after payment 1: exactly 1 payment row
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var payments = await db.CustomerPayments.IgnoreQueryFilters().Where(p => p.SaleId == saleId).ToListAsync();
            payments.Should().HaveCount(1);
            payments[0].Amount.Should().Be(2000m);
            payments[0].PaymentMode.Should().Be(PaymentMode.Cash);

            var sale = await db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == saleId);
            sale.AmountPaid.Should().Be(2000m);
        }

        // 3. Second payment: 1500m
        var key2 = Guid.NewGuid().ToString();
        using (var req2 = CreatePaymentRequest(saleId, 1500m, PaymentMode.UPI, key2))
        {
            var res2 = await _client.SendAsync(req2);
            res2.StatusCode.Should().Be(HttpStatusCode.OK);
            var result2 = await res2.Content.ReadFromJsonAsync<RecordPaymentResult>();
            result2.Should().NotBeNull();
            result2!.NewAmountPaid.Should().Be(3500m);
            result2.NewBalance.Should().Be(1500m);
        }

        // Verify after payment 2: exactly 2 distinct rows, first was never mutated!
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var payments = await db.CustomerPayments.IgnoreQueryFilters().Where(p => p.SaleId == saleId).OrderBy(p => p.CreatedAt).ToListAsync();
            payments.Should().HaveCount(2);

            payments[0].Amount.Should().Be(2000m);
            payments[0].PaymentMode.Should().Be(PaymentMode.Cash);

            payments[1].Amount.Should().Be(1500m);
            payments[1].PaymentMode.Should().Be(PaymentMode.UPI);

            var sale = await db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == saleId);
            sale.AmountPaid.Should().Be(3500m);
            (sale.GrandTotal - sale.AmountPaid).Should().Be(1500m);
        }
    }

    [Fact]
    public async Task PaymentExceedingBalance_Returns422PaymentExceedsBalance()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        Guid saleId;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var sale = Sale.CreateTaxInvoice(
                _tenantId, _branchId, "INV-PAY-EXCEED", DateTimeOffset.UtcNow,
                _customerId, "Credit Customer", null,
                1000m, 90m, 90m, 1180m, 0m, 500m,
                PaymentMode.Credit, null, null, null, null, null, "Partial Sale", null, _userId);
            db.Sales.Add(sale);
            await db.SaveChangesAsync();
            saleId = sale.Id;
        }

        // Remaining balance is 1180 - 500 = 680. Attempt to pay 700.
        using var req = CreatePaymentRequest(saleId, 700m, PaymentMode.Cash, Guid.NewGuid().ToString());
        var res = await _client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await res.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("payment_exceeds_balance");
    }

    [Fact]
    public async Task PaymentAgainstVoidedSale_Returns422SaleNotActive()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        Guid saleId;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var sale = Sale.CreateTaxInvoice(
                _tenantId, _branchId, "INV-PAY-VOID", DateTimeOffset.UtcNow,
                _customerId, "Credit Customer", null,
                1000m, 90m, 90m, 1180m, 0m, 0m,
                PaymentMode.Credit, null, null, null, null, null, "Voided Sale", null, _userId);
            sale.MarkVoided();
            db.Sales.Add(sale);
            await db.SaveChangesAsync();
            saleId = sale.Id;
        }

        using var req = CreatePaymentRequest(saleId, 500m, PaymentMode.Cash, Guid.NewGuid().ToString());
        var res = await _client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await res.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("sale_not_active");
    }

    [Fact]
    public async Task PaymentOnTaxInvoiceWithCustomer_CreatesLedgerCredit()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        Guid saleId;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var sale = Sale.CreateTaxInvoice(
                _tenantId, _branchId, "INV-PAY-LEDGER", DateTimeOffset.UtcNow,
                _customerId, "Credit Customer", null,
                2000m, 180m, 180m, 2360m, 0m, 0m,
                PaymentMode.Credit, null, null, null, null, null, "Credit Sale for Ledger", null, _userId);
            db.Sales.Add(sale);
            await db.SaveChangesAsync();
            saleId = sale.Id;
        }

        using var req = CreatePaymentRequest(saleId, 1000m, PaymentMode.Cash, Guid.NewGuid().ToString());
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify customer_ledger_entries credit entry created
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var ledgerEntry = await db.CustomerLedgerEntries.IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.SaleId == saleId && e.CustomerId == _customerId);

            ledgerEntry.Should().NotBeNull();
            ledgerEntry!.EntryType.Should().Be(CustomerLedgerEntryType.Payment);
            ledgerEntry.Credit.Should().Be(1000m);
            ledgerEntry.Debit.Should().Be(0m);
        }
    }

    [Fact]
    public async Task PaymentOnFinancedSale_DoesNotCreateLedgerEntry()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        Guid saleId;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var sale = Sale.CreateTaxInvoice(
                _tenantId, _branchId, "INV-PAY-FINANCE", DateTimeOffset.UtcNow,
                _customerId, "Credit Customer", null,
                10000m, 900m, 900m, 11800m, 0m, 2000m,
                PaymentMode.Finance, "Bajaj", "Bajaj", 2000m,
                "{}", null, "Financed Sale", null, _userId);
            db.Sales.Add(sale);
            await db.SaveChangesAsync();
            saleId = sale.Id;
        }

        using var req = CreatePaymentRequest(saleId, 5000m, PaymentMode.Cash, Guid.NewGuid().ToString());
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify customer_payments row exists
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var payment = await db.CustomerPayments.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.SaleId == saleId && p.Amount == 5000m);
            payment.Should().NotBeNull();

            // Guard verification: NO customer_ledger_entries created for financed sale payment!
            var ledgerEntry = await db.CustomerLedgerEntries.IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.SaleId == saleId && e.CustomerId == _customerId);
            ledgerEntry.Should().BeNull();
        }
    }

    [Fact]
    public async Task ReplayingIdempotencyKey_DoesNotDoubleRecordPayment()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        Guid saleId;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var sale = Sale.CreateTaxInvoice(
                _tenantId, _branchId, "INV-PAY-IDEM", DateTimeOffset.UtcNow,
                _customerId, "Credit Customer", null,
                2000m, 180m, 180m, 2360m, 0m, 0m,
                PaymentMode.Credit, null, null, null, null, null, "Idempotency Sale", null, _userId);
            db.Sales.Add(sale);
            await db.SaveChangesAsync();
            saleId = sale.Id;
        }

        var key = Guid.NewGuid().ToString();

        // Call 1
        using (var req1 = CreatePaymentRequest(saleId, 1000m, PaymentMode.Cash, key))
        {
            var res1 = await _client.SendAsync(req1);
            res1.StatusCode.Should().Be(HttpStatusCode.OK);
            var result1 = await res1.Content.ReadFromJsonAsync<RecordPaymentResult>();
            result1!.NewAmountPaid.Should().Be(1000m);
        }

        // Call 2 (identical request and key)
        using (var req2 = CreatePaymentRequest(saleId, 1000m, PaymentMode.Cash, key))
        {
            var res2 = await _client.SendAsync(req2);
            res2.StatusCode.Should().Be(HttpStatusCode.OK);
            res2.Headers.Contains("Idempotent-Replayed").Should().BeTrue();
            var result2 = await res2.Content.ReadFromJsonAsync<RecordPaymentResult>();
            result2!.NewAmountPaid.Should().Be(1000m);
        }

        // Verify exactly 1 payment was recorded in DB
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var payments = await db.CustomerPayments.IgnoreQueryFilters().Where(p => p.SaleId == saleId).ToListAsync();
            payments.Should().HaveCount(1);
            payments[0].Amount.Should().Be(1000m);

            var sale = await db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == saleId);
            sale.AmountPaid.Should().Be(1000m);
        }
    }
}
