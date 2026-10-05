using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Behaviors;

public class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IAccessScope _scope;

    public AuthorizationBehavior(IAccessScope scope) => _scope = scope;

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is IRequiresPermission permissioned)
        {
            if (!_scope.IsAuthenticated)
                throw new DomainException("unauthenticated", "Authentication is required.");
            if (!_scope.HasPermission(permissioned.RequiredPermission))
                throw new DomainException("forbidden", $"Missing required permission: {permissioned.RequiredPermission}");
        }

        return next();
    }
}
