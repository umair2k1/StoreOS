namespace StoreOS.Application.Platform;

public interface IFeatureManager
{
    Task<bool> IsRepairsEnabledAsync(CancellationToken ct);
    Task SetRepairsEnabledAsync(bool enabled, CancellationToken ct);
}
