using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Platform;

[Collection("postgres")]
public class UploadLogoTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _adminRoleId = Guid.NewGuid();
    private readonly Guid _plainUserId = Guid.NewGuid();
    private readonly Guid _plainRoleId = Guid.NewGuid();

    private string _adminToken = string.Empty;
    private string _plainToken = string.Empty;

    public UploadLogoTests(PostgresTestFixture fixture)
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
        var passwordHash = _hasher.Hash("SecretPassword123!");

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Logo Test Tenant", subdomain: "logo-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "LB1", "Logo Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Admin role with settings:manage
        var adminRole = Role.Create(_tenantId, "Tenant Admin");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(adminRole, _adminRoleId);
        db.Roles.Add(adminRole);
        db.RolePermissions.Add(RolePermission.Create(_adminRoleId, PermissionCatalog.SettingsManage));

        var adminUser = User.Create(_tenantId, "admin@logotest.com", passwordHash, "Admin User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(adminUser, _adminUserId);
        db.Users.Add(adminUser);
        db.UserBranches.Add(UserBranch.Create(_adminUserId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_adminUserId, _adminRoleId, _branchId));

        // Plain user without settings:manage
        var plainRole = Role.Create(_tenantId, "Plain Viewer");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(plainRole, _plainRoleId);
        db.Roles.Add(plainRole);

        var plainUser = User.Create(_tenantId, "plain@logotest.com", passwordHash, "Plain User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(plainUser, _plainUserId);
        db.Users.Add(plainUser);
        db.UserBranches.Add(UserBranch.Create(_plainUserId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_plainUserId, _plainRoleId, _branchId));

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();

        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var (adminTok, _) = jwtService.IssueAccessToken(
            _adminUserId, _tenantId, allBranches: true, new[] { _branchId },
            new[] { PermissionCatalog.SettingsManage }, tokenVersion: 1);
        _adminToken = adminTok;

        var (plainTok, _) = jwtService.IssueAccessToken(
            _plainUserId, _tenantId, allBranches: true, new[] { _branchId },
            Array.Empty<string>(), tokenVersion: 1);
        _plainToken = plainTok;
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM settings WHERE tenant_id = @t;
            DELETE FROM user_role_assignments WHERE user_id = @u OR user_id = @pu;
            DELETE FROM user_branches WHERE user_id = @u OR user_id = @pu;
            DELETE FROM users WHERE tenant_id = @t;
            DELETE FROM role_permissions WHERE role_id = @r;
            DELETE FROM roles WHERE tenant_id = @t;
            DELETE FROM branches WHERE tenant_id = @t;
            DELETE FROM tenants WHERE id = @t;";
        cmd.Parameters.AddWithValue("t", _tenantId);
        cmd.Parameters.AddWithValue("u", _adminUserId);
        cmd.Parameters.AddWithValue("pu", _plainUserId);
        cmd.Parameters.AddWithValue("r", _adminRoleId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private byte[] CreateSamplePngBytes(int width = 64, int height = 64)
    {
        using var img = new Image<Rgba32>(width, height, Color.CornflowerBlue);
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task UploadLogo_ValidSmallPng_SucceedsAndPersistsWebp()
    {
        var pngBytes = CreateSamplePngBytes(120, 120);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "logo.png");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/tenants/me/logo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        req.Headers.Add("X-Branch-Id", _branchId.ToString());
        req.Content = content;

        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var url = doc.RootElement.GetProperty("logoUrl").GetString();
        url.Should().NotBeNullOrWhiteSpace();

        // Check settings table has branding.logo_key
        using var scope = _factory.Services.CreateScope();
        var s3Client = scope.ServiceProvider.GetRequiredService<IAmazonS3>();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value::text FROM settings WHERE tenant_id = @t AND branch_id IS NULL AND key = 'branding.logo_key';";
        cmd.Parameters.AddWithValue("t", _tenantId);
        var logoKeyJson = (string?)await cmd.ExecuteScalarAsync();
        logoKeyJson.Should().NotBeNull();
        var logoKey = JsonSerializer.Deserialize<string>(logoKeyJson!);
        logoKey.Should().EndWith(".webp");

        // Verify stored object in MinIO
        var s3Obj = await s3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = "storeos-media",
            Key = logoKey
        });
        s3Obj.Headers.ContentType.Should().Be("image/webp");

        using var memory = new MemoryStream();
        await s3Obj.ResponseStream.CopyToAsync(memory);
        var storedBytes = memory.ToArray();

        using var loaded = Image.Load(storedBytes);
        loaded.Width.Should().BeLessOrEqualTo(512);
        loaded.Height.Should().BeLessOrEqualTo(512);
        loaded.Metadata.ExifProfile.Should().BeNull();

        // Also test GET /api/tenant/logo
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/tenant/logo?size=512&tenantId={_tenantId}");
        var getRes = await _client.SendAsync(getReq);
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        getRes.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        getRes.Headers.ETag.Should().NotBeNull();

        var getBytes = await getRes.Content.ReadAsByteArrayAsync();
        using var loadedPng = Image.Load(getBytes);
        loadedPng.Width.Should().Be(512);
        loadedPng.Height.Should().Be(512);
    }

    [Fact]
    public async Task UploadLogo_FileOver2MB_Returns422LogoTooLarge()
    {
        // 2.5 MB fake PNG (valid magic bytes + padding)
        var bytes = new byte[2500 * 1024];
        bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47;

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "large.png");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/tenants/me/logo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        req.Headers.Add("X-Branch-Id", _branchId.ToString());
        req.Content = content;

        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("logo_too_large");
    }

    [Fact]
    public async Task UploadLogo_MismatchedMagicBytes_Returns422MagicBytesMismatch()
    {
        // Declared image/png, but starts with JPEG magic bytes (FF D8 FF)
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "fake.png");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/tenants/me/logo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        req.Headers.Add("X-Branch-Id", _branchId.ToString());
        req.Content = content;

        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("logo_magic_bytes_mismatch");
    }

    [Fact]
    public async Task UploadLogo_SvgContentType_Returns422LogoInvalidType()
    {
        var svgBytes = System.Text.Encoding.UTF8.GetBytes("<svg><circle r='10'/></svg>");

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(svgBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/svg+xml");
        content.Add(fileContent, "file", "image.svg");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/tenants/me/logo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        req.Headers.Add("X-Branch-Id", _branchId.ToString());
        req.Content = content;

        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("logo_invalid_type");
    }

    [Fact]
    public async Task UploadLogo_WithoutSettingsManage_Returns403Forbidden()
    {
        var pngBytes = CreateSamplePngBytes();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "logo.png");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/tenants/me/logo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _plainToken);
        req.Headers.Add("X-Branch-Id", _branchId.ToString());
        req.Content = content;

        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
