namespace StoreOS.Domain.Inventory;

public readonly record struct ValuationResult(decimal CostValuation, decimal RetailValuation, decimal PotentialGrossValue);
