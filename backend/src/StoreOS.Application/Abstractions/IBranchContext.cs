namespace StoreOS.Application.Abstractions;

public interface IBranchContext
{
    Guid? CurrentBranchId { get; }
    bool HasBranchContext { get; }
}
