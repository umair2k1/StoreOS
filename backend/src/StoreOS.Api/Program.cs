using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using StoreOS.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Database & EF Core
builder.Services.AddDbContext<StoreOsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<StoreOS.Application.Abstractions.IAppDbContext>(
    sp => sp.GetRequiredService<StoreOsDbContext>());
builder.Services.AddScoped<StoreOS.Application.Abstractions.ITenantResolver, StoreOS.Infrastructure.Tenancy.TenantResolver>();
builder.Services.AddHostedService<RlsRoleGuard>();

// Access Scope & HttpContext (P-106)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<StoreOS.Application.Abstractions.IAccessScope, StoreOS.Infrastructure.Security.HttpAccessScope>();

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

var app = builder.Build();

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
