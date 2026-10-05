using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StoreOS.Api.Middleware;
using StoreOS.Infrastructure.Persistence;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Database & EF Core (P-101, P-109)
builder.Services.AddScoped<StoreOS.Infrastructure.Persistence.RlsSessionInterceptor>();
builder.Services.AddDbContext<StoreOsDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"));
    options.AddInterceptors(sp.GetRequiredService<StoreOS.Infrastructure.Persistence.RlsSessionInterceptor>());
});
builder.Services.AddScoped<StoreOS.Application.Abstractions.IAppDbContext>(
    sp => sp.GetRequiredService<StoreOsDbContext>());
builder.Services.AddScoped<StoreOS.Application.Abstractions.ITenantResolver, StoreOS.Infrastructure.Tenancy.TenantResolver>();
builder.Services.AddScoped<StoreOS.Application.Abstractions.ITenantJobRunner, StoreOS.Infrastructure.Persistence.TenantJobRunner>();
builder.Services.AddHostedService<RlsRoleGuard>();

// MediatR Pipeline & Behaviors (P-109)
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(StoreOS.Application.Abstractions.IAppDbContext).Assembly);
    cfg.AddOpenBehavior(typeof(StoreOS.Infrastructure.Persistence.TransactionBehavior<,>));
});

// Access Scope & HttpContext (P-106)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<StoreOS.Application.Abstractions.IAccessScope, StoreOS.Infrastructure.Security.HttpAccessScope>();

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
app.UseHttpsRedirection();

app.UseCors("Default");

app.UseAuthorization();

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
