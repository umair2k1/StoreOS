using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Behaviors;

public class RepairsFeatureGateBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IFeatureManager _featureManager;

    public RepairsFeatureGateBehavior(IFeatureManager featureManager)
    {
        _featureManager = featureManager;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is IRequiresRepairsFeature && !await _featureManager.IsRepairsEnabledAsync(ct))
        {
            throw new DomainValidationException("repairs_module_disabled", "The repairs module is not enabled for this tenant.");
        }

        return await next();
    }
}
