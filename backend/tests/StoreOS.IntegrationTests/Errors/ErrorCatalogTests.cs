using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using StoreOS.Api.Middleware;

namespace StoreOS.IntegrationTests.Errors;

public class ErrorCatalogTests
{
    [Theory]
    [InlineData("unauthenticated", HttpStatusCode.Unauthorized)]
    [InlineData("invalid_credentials", HttpStatusCode.Unauthorized)]
    [InlineData("token_version_stale", HttpStatusCode.Unauthorized)]
    [InlineData("invalid_refresh_token", HttpStatusCode.Unauthorized)]
    [InlineData("refresh_token_expired", HttpStatusCode.Unauthorized)]
    [InlineData("refresh_token_reuse_detected", HttpStatusCode.Unauthorized)]
    [InlineData("tenant_required", HttpStatusCode.Unauthorized)]
    [InlineData("forbidden", HttpStatusCode.Forbidden)]
    [InlineData("tenant_inactive", HttpStatusCode.Forbidden)]
    [InlineData("branch_context_required", HttpStatusCode.BadRequest)]
    [InlineData("idempotency_key_required", HttpStatusCode.BadRequest)]
    [InlineData("idempotency_key_invalid", HttpStatusCode.BadRequest)]
    [InlineData("branch_code_taken", HttpStatusCode.Conflict)]
    [InlineData("imei_already_active", HttpStatusCode.Conflict)]
    [InlineData("idempotency_key_reuse", HttpStatusCode.Conflict)]
    [InlineData("idempotency_in_progress", HttpStatusCode.Conflict)]
    [InlineData("serialization_conflict", HttpStatusCode.Conflict)]
    public void StatusFor_ExplicitCodes_ReturnsConfiguredStatusCode(string code, HttpStatusCode expected)
    {
        ErrorCatalog.StatusFor(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("tenant_not_found")]
    [InlineData("branch_not_found")]
    [InlineData("user_not_found")]
    [InlineData("order_not_found")]
    public void StatusFor_NotFoundSuffix_Returns404(string code)
    {
        ErrorCatalog.StatusFor(code).Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("invalid_discount")]
    [InlineData("credit_limit_exceeded")]
    [InlineData("insufficient_stock")]
    [InlineData("custom_domain_rule")]
    public void StatusFor_UnmappedCodes_Returns422(string code)
    {
        ErrorCatalog.StatusFor(code).Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public void ArchitectureTest_AllDomainExceptionCodes_MapCorrectly()
    {
        // Scan backend/src/StoreOS.Domain and backend/src/StoreOS.Application for new DomainException("...") or new DomainValidationException("...")
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (baseDir != null && !File.Exists(Path.Combine(baseDir.FullName, "StoreOS.sln")))
        {
            baseDir = baseDir.Parent;
        }

        if (baseDir == null) return;

        var sourceDirs = new[]
        {
            Path.Combine(baseDir.FullName, "backend", "src", "StoreOS.Domain"),
            Path.Combine(baseDir.FullName, "backend", "src", "StoreOS.Application")
        };

        var pattern = new Regex(@"new\s+(?:DomainException|DomainValidationException)\s*\(\s*""([^""]+)""", RegexOptions.Compiled);

        foreach (var dir in sourceDirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(file);
                var matches = pattern.Matches(content);

                foreach (Match match in matches)
                {
                    var code = match.Groups[1].Value;
                    var status = ErrorCatalog.StatusFor(code);

                    if (code.EndsWith("_not_found", StringComparison.Ordinal))
                    {
                        status.Should().Be(HttpStatusCode.NotFound, $"Code '{code}' ending in _not_found must map to 404");
                    }
                    else
                    {
                        // Any code in Domain/Application must resolve to a valid non-500 status code
                        ((int)status).Should().BeInRange(400, 499, $"Code '{code}' must map to a 4xx status");
                    }
                }
            }
        }
    }
}
