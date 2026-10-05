using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Identity;

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _config;
    public JwtTokenService(IConfiguration config) => _config = config;

    public (string token, DateTimeOffset expiresAt) IssueAccessToken(
        Guid userId, Guid tenantId, bool allBranches, IEnumerable<Guid> branchIds,
        IEnumerable<string> permissions, int tokenVersion)
    {
        // Note: Production deployment must source Jwt:SigningKey from a cloud secret manager (Plan §14.4).
        var signingKey = _config["Jwt:SigningKey"]!;
        var issuer = _config["Jwt:Issuer"]!;
        var audience = _config["Jwt:Audience"]!;
        var minutes = int.Parse(_config["Jwt:AccessTokenMinutes"] ?? "15");

        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("all_branches", allBranches ? "true" : "false"),
            new("token_version", tokenVersion.ToString())
        };
        if (!allBranches)
            claims.AddRange(branchIds.Select(id => new Claim("branch_id", id.ToString())));
        claims.AddRange(permissions.Select(p => new Claim("perm", p)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTimeOffset.UtcNow.AddMinutes(minutes);

        var token = new JwtSecurityToken(issuer, audience, claims,
            expires: expires.UtcDateTime, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
