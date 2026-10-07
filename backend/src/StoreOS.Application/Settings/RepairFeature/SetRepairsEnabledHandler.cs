using MediatR;
using StoreOS.Application.Platform;

namespace StoreOS.Application.Settings.RepairFeature;

public class SetRepairsEnabledHandler : IRequestHandler<SetRepairsEnabledCommand>
{
    private readonly IFeatureManager _featureManager;

    public SetRepairsEnabledHandler(IFeatureManager featureManager)
    {
        _featureManager = featureManager;
    }

    public async Task Handle(SetRepairsEnabledCommand request, CancellationToken ct)
    {
        await _featureManager.SetRepairsEnabledAsync(request.Enabled, ct);
    }
}
