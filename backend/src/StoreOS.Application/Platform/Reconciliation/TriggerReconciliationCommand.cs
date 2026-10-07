using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Platform.Reconciliation;

public record TriggerReconciliationCommand() : IRequest, IRequiresPermission, IHighImpactOperation
{
    public string RequiredPermission => "settings:manage";
}

public class TriggerReconciliationHandler : IRequestHandler<TriggerReconciliationCommand>
{
    public Task Handle(TriggerReconciliationCommand request, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
