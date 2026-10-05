using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StoreOS.Api.Middleware;
using StoreOS.Infrastructure.Persistence;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Rate Limiting (P-312)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User?.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            status = 429,
            title = "Too many requests. Please slow down.",
            code = "rate_limited"
        }, ct);
    };
});

// Database & EF Core (P-101, P-109)
builder.Services.AddScoped<StoreOS.Infrastructure.Persistence.RlsSessionInterceptor>();
builder.Services.AddDbContext<StoreOsDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"));
    options.AddInterceptors(sp.GetRequiredService<StoreOS.Infrastructure.Persistence.RlsSessionInterceptor>());
});
builder.Services.AddDbContext<StoreOS.Infrastructure.Persistence.ReportingDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Reporting"), npgsql => { });
    options.AddInterceptors(sp.GetRequiredService<StoreOS.Infrastructure.Persistence.RlsSessionInterceptor>());
});
builder.Services.AddScoped<StoreOS.Application.Abstractions.IReportingDbContext>(
    sp => sp.GetRequiredService<StoreOS.Infrastructure.Persistence.ReportingDbContext>());
builder.Services.AddScoped<StoreOS.Application.Abstractions.IAppDbContext>(
    sp => sp.GetRequiredService<StoreOsDbContext>());
builder.Services.AddScoped<StoreOS.Application.Abstractions.ITenantResolver, StoreOS.Infrastructure.Tenancy.TenantResolver>();
builder.Services.AddScoped<StoreOS.Application.Abstractions.ITenantJobRunner, StoreOS.Infrastructure.Persistence.TenantJobRunner>();
builder.Services.AddScoped<StoreOS.Application.Platform.IAuditWriter, StoreOS.Infrastructure.Platform.AuditWriter>();
builder.Services.AddScoped<StoreOS.Application.Inventory.IInventoryStockService, StoreOS.Infrastructure.Inventory.InventoryStockService>();
builder.Services.AddScoped<StoreOS.Application.Abstractions.IInvoiceSequenceAllocator, StoreOS.Infrastructure.Sales.InvoiceSequenceAllocator>();
builder.Services.AddScoped<StoreOS.Application.Sales.Checkout.PricingResolver>();
builder.Services.AddHostedService<RlsRoleGuard>();
builder.Services.AddScoped<StoreOS.Application.Customers.ICustomerLedgerService, StoreOS.Infrastructure.Customers.CustomerLedgerService>();

// MediatR Pipeline & Behaviors (P-109, P-114, P-307)
builder.Services.AddMemoryCache();
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(StoreOS.Application.AssemblyMarker).Assembly);
    cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.AuthorizationBehavior<,>));
    cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.ValidationBehavior<,>));
    cfg.AddOpenBehavior(typeof(StoreOS.Infrastructure.Persistence.TransactionBehavior<,>));
    cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.LiveDbPermissionBehavior<,>));
    cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.TokenVersionBehavior<,>));
    cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.IdempotencyBehavior<,>));
});
builder.Services.AddScoped<StoreOS.Application.Behaviors.IdempotencyReplayFlag>();
builder.Services.AddScoped<StoreOS.Application.Abstractions.IIdempotencyStore, StoreOS.Infrastructure.Persistence.IdempotencyStore>();
builder.Services.AddValidatorsFromAssembly(typeof(StoreOS.Application.AssemblyMarker).Assembly);

// Authentication & Authorization (P-106, P-114, P-302)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"]!)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub"
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddSingleton<StoreOS.Application.Identity.IPermissionChecker, StoreOS.Infrastructure.Identity.PermissionChecker>();
builder.Services.AddSingleton<StoreOS.Application.Identity.IPasswordHasher, StoreOS.Infrastructure.Identity.Argon2PasswordHasher>();
builder.Services.AddSingleton<StoreOS.Application.Abstractions.IJwtTokenService, StoreOS.Infrastructure.Identity.JwtTokenService>();
builder.Services.AddSingleton<StoreOS.Infrastructure.Identity.JwtTokenService>(sp => (StoreOS.Infrastructure.Identity.JwtTokenService)sp.GetRequiredService<StoreOS.Application.Abstractions.IJwtTokenService>());
builder.Services.AddScoped<StoreOS.Application.Abstractions.IRefreshTokenService, StoreOS.Infrastructure.Identity.RefreshTokenService>();
// Access Scope & HttpContext (P-106)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<StoreOS.Application.Abstractions.IAccessScope, StoreOS.Infrastructure.Security.HttpAccessScope>();
builder.Services.AddScoped<StoreOS.Infrastructure.Security.MutableBranchContext>();
builder.Services.AddScoped<StoreOS.Application.Abstractions.IBranchContext>(
    sp => sp.GetRequiredService<StoreOS.Infrastructure.Security.MutableBranchContext>());

// Correlation Context Accessor (P-112)
builder.Services.AddScoped<StoreOS.Application.Abstractions.ICorrelationContextAccessor, StoreOS.Infrastructure.Observability.CorrelationContextAccessor>();

// CORS Scaffolding (Plan §13.1: localhost:4200 for local frontend dev)
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Health Checks (Plan §15)
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: builder.Configuration.GetConnectionString("Default")!,
        name: "database",
        tags: ["ready"]);

// Serilog Configuration (P-112)
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] CorrelationId={CorrelationId} {Message:lj}{NewLine}{Exception}"));

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseRateLimiter();
app.UseHttpsRedirection();
app.UseCors("Default");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<BranchContextMiddleware>();
app.MapControllers();

// Health check endpoints: /health/live (always 200), /health/ready (verifies DB)
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

public partial class Program { }
