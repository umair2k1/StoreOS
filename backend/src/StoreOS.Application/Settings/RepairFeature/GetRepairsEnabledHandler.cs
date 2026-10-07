using MediatR;
using StoreOS.Application.Platform;

namespace StoreOS.Application.Settings.RepairFeature;

public class GetRepairsEnabledHandler : IRequestHandler<GetRepairsEnabledQuery, bool>
{
    private readonly IFeatureManager _featureManager;

    public GetRepairsEnabledHandler(IFeatureManager featureManager)
    {
        _featureManager = featureManager;
    }

    public Task<bool> Handle(GetRepairsEnabledQuery request, CancellationToken ct) =>
        _featureManager.IsRepairsEnabledAsync(ct);
}
