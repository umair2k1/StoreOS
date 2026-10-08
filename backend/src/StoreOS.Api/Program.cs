using StoreOS.Api.Jobs;
using Amazon.S3;
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
using System.Text.Json;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform.BackupExport;
using StoreOS.Application.Platform.BackupImport;
using StoreOS.Api.Serialization;
var builder = WebApplication.CreateBuilder(args);
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;


// Add services to the container.
builder.Services.AddControllers(options =>
{
    options.ModelBinderProviders.Insert(0, new DateOnlyModelBinderProvider());
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new FlexibleDateOnlyJsonConverter());
    options.JsonSerializerOptions.Converters.Add(new FlexibleNullableDateOnlyJsonConverter());
});

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
builder.Services.AddScoped<StoreOS.Application.Sales.Checkout.CreditLimitChecker>();
builder.Services.AddHostedService<RlsRoleGuard>();
builder.Services.AddScoped<StoreOS.Application.Customers.ICustomerLedgerService, StoreOS.Infrastructure.Customers.CustomerLedgerService>();
builder.Services.AddScoped<StoreOS.Application.Suppliers.ISupplierLedgerService, StoreOS.Infrastructure.Suppliers.SupplierLedgerService>();
builder.Services.AddScoped<StoreOS.Application.FinanceOps.ILiabilityBalanceService, StoreOS.Infrastructure.FinanceOps.LiabilityBalanceService>();
builder.Services.AddScoped<StoreOS.Application.Repairs.IRepairBillingService, StoreOS.Infrastructure.Repairs.RepairBillingService>();
builder.Services.AddScoped<StoreOS.Application.Platform.IFeatureManager, StoreOS.Infrastructure.Platform.TenantFeatureManager>();

builder.Services.AddSingleton<StoreOS.Infrastructure.Printing.InvoicePdfRenderer>();
builder.Services.AddSingleton<StoreOS.Infrastructure.Printing.RepairPdfRenderer>();
builder.Services.AddSingleton<StoreOS.Infrastructure.Printing.SupplierReturnPdfRenderer>();
builder.Services.AddSingleton<StoreOS.Infrastructure.Printing.ReportPdfRenderer>();

// Object Storage (P-901)
var s3Config = new AmazonS3Config
{
    ServiceURL = builder.Configuration["ObjectStorage:ServiceUrl"] ?? "http://localhost:9000",
    ForcePathStyle = true,
    Timeout = TimeSpan.FromSeconds(3),
    MaxErrorRetry = 1
};
var s3Client = new AmazonS3Client(
    builder.Configuration["ObjectStorage:AccessKey"] ?? "storeos_minio",
    builder.Configuration["ObjectStorage:SecretKey"] ?? "storeos_minio_password",
    s3Config);
builder.Services.AddSingleton<IAmazonS3>(s3Client);
builder.Services.AddScoped<StoreOS.Application.Abstractions.IObjectStorageService, StoreOS.Infrastructure.Platform.MinioObjectStorageService>();
builder.Services.AddHostedService<StoreOS.Infrastructure.Platform.ObjectStorageBucketInitializer>();

// Hangfire Background Jobs (P-905, P-906)
builder.Services.AddHangfireConfiguration(builder.Configuration);
// MediatR Pipeline & Behaviors (P-109, P-114, P-307)
builder.Services.AddMemoryCache();
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(StoreOS.Application.AssemblyMarker).Assembly);
    cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.ValidationBehavior<,>));
    cfg.AddOpenBehavior(typeof(StoreOS.Infrastructure.Persistence.SerializationRetryBehavior<,>));
    cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.AuthorizationBehavior<,>));
    cfg.AddOpenBehavior(typeof(StoreOS.Infrastructure.Persistence.TransactionBehavior<,>));
    cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.TokenVersionBehavior<,>));
    cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.LiveDbPermissionBehavior<,>));
    cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.IdempotencyBehavior<,>));
    cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.RepairsFeatureGateBehavior<,>));
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

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "StoreOS API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddOpenApiDocument();

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
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "StoreOS API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<BranchContextMiddleware>();
app.MapControllers();

// Health check endpoints: /health/live (always 200), /health/ready (verifies DB)
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

// Recurring Jobs (P-905, P-906)
app.UseHangfireConfiguration();

if (args.Contains("--export-backup-and-exit") || args.Contains("--import-backup-and-exit"))
{
    var exitCode = await Program.ExecuteCliModeAsync(args, app.Services);
    return;
}

app.Run();

public partial class Program
{
    public static async Task<int> ExecuteCliModeAsync(string[] args, IServiceProvider services)
    {
        string backupPath = "restore-drill-backup.json";
        string? connectionString = null;
        Guid? targetTenantId = null;
        string? targetSubdomain = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--backup-path" && i + 1 < args.Length)
                backupPath = args[++i];
            else if (args[i] is "--connection" or "-c" && i + 1 < args.Length)
                connectionString = args[++i];
            else if (args[i] == "--tenant-id" && i + 1 < args.Length && Guid.TryParse(args[i + 1], out var parsedId))
                targetTenantId = parsedId;
            else if (args[i] == "--subdomain" && i + 1 < args.Length)
                targetSubdomain = args[++i];
        }

        var cs = connectionString
            ?? Environment.GetEnvironmentVariable("STOREOS_MIGRATIONS_CONNECTION");

        using var scope = services.CreateScope();
        StoreOsDbContext db;
        bool disposeDb = false;

        if (!string.IsNullOrWhiteSpace(cs))
        {
            var opts = new DbContextOptionsBuilder<StoreOsDbContext>()
                .UseNpgsql(cs)
                .Options;
            db = new StoreOsDbContext(opts, new NullAccessScope());
            disposeDb = true;
        }
        else
        {
            db = scope.ServiceProvider.GetRequiredService<StoreOsDbContext>();
        }

        try
        {
            if (args.Contains("--export-backup-and-exit"))
            {
                StoreOS.Domain.Tenancy.Tenant? tenant = null;
                if (targetTenantId.HasValue)
                {
                    tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == targetTenantId.Value);
                }
                else if (!string.IsNullOrWhiteSpace(targetSubdomain))
                {
                    tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Subdomain == targetSubdomain);
                }
                else
                {
                    tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync();
                }

                if (tenant == null)
                {
                    Console.Error.WriteLine("No tenant found to export.");
                    return 1;
                }

                var accessScope = new DrillAccessScope(tenant.Id);
                var storage = scope.ServiceProvider.GetRequiredService<IObjectStorageService>();
                var handler = new ExportTenantDataHandler(db, accessScope, storage);

                await using var tx = await db.Database.BeginTransactionAsync();
                var backup = await handler.Handle(new ExportTenantDataCommand(), CancellationToken.None);
                await tx.CommitAsync();

                var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(backupPath, json);
                Console.WriteLine($"Exported backup for tenant {tenant.Id} to {backupPath}");
                return 0;
            }

            if (args.Contains("--import-backup-and-exit"))
            {
                if (!File.Exists(backupPath))
                {
                    Console.Error.WriteLine($"Backup file '{backupPath}' not found.");
                    return 1;
                }

                var json = await File.ReadAllTextAsync(backupPath);
                var backup = JsonSerializer.Deserialize<TenantBackupDto>(json);
                if (backup == null)
                {
                    Console.Error.WriteLine("Failed to deserialize backup file.");
                    return 1;
                }

                var accessScope = new DrillAccessScope(backup.TenantId);
                var handler = new ImportTenantDataHandler(db, accessScope);

                await using var tx = await db.Database.BeginTransactionAsync();
                var result = await handler.Handle(new ImportTenantDataCommand(backup), CancellationToken.None);
                if (!result.Success)
                {
                    await tx.RollbackAsync();
                    Console.Error.WriteLine($"Restore failed: {result.ErrorMessage}");
                    return 1;
                }
                await tx.CommitAsync();

                Console.WriteLine($"Restored backup for tenant {backup.TenantId}. Tables restored: {result.RecordCounts.Count}");
                return 0;
            }

            return 0;
        }
        finally
        {
            if (disposeDb)
            {
                await db.DisposeAsync();
            }
        }
    }
}

internal sealed class DrillAccessScope : IAccessScope
{
    public DrillAccessScope(Guid tenantId) => TenantId = tenantId;
    public Guid TenantId { get; }
    public bool HasTenantContext => true;
    public Guid UserId { get; } = Guid.NewGuid();
    public IReadOnlySet<Guid> BranchIds { get; } = new HashSet<Guid>();
    public bool AllBranches => true;
    public IReadOnlySet<string> Permissions { get; } = new HashSet<string> { "settings:manage" };
    public int TokenVersion => 1;
    public bool IsAuthenticated => true;
    public bool HasBranchAccess(Guid branchId) => true;
    public bool HasPermission(string permissionCode) => true;
    public void UseAnonymousTenant(Guid tenantId) { }
    public void UseSystemContext(Guid tenantId) { }
}
