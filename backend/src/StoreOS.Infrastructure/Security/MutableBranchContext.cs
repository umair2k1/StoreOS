using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Security;

public class MutableBranchContext : IBranchContext
{
    public Guid? CurrentBranchId { get; set; }
    public bool HasBranchContext => CurrentBranchId.HasValue;
}
