namespace StoreOS.Application.Abstractions;

public interface IJwtTokenService
{
    (string token, DateTimeOffset expiresAt) IssueAccessToken(
        Guid userId, Guid tenantId, bool allBranches, IEnumerable<Guid> branchIds,
        IEnumerable<string> permissions, int tokenVersion);
}
