using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;

namespace StoreOS.Domain.Tests.Inventory;

public class InventoryMovementTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();

    [Theory]
    [InlineData(MovementType.Purchase)]
    [InlineData(MovementType.Sale)]
    [InlineData(MovementType.SaleVoid)]
    [InlineData(MovementType.SaleRefund)]
    [InlineData(MovementType.SupplierReturn)]
    [InlineData(MovementType.RepairConsumed)]
    [InlineData(MovementType.RepairPartReturned)]
    [InlineData(MovementType.Adjustment)]
    [InlineData(MovementType.TransferOut)]
    [InlineData(MovementType.TransferIn)]
    public void Create_ValidWithEachMovementType_Succeeds(string movementType)
    {
        var movement = InventoryMovement.Create(
            tenantId: TenantId,
            branchId: BranchId,
            productId: ProductId,
            imeiId: null,
            movementType: movementType,
            qtyDelta: 5,
            unitCost: 100m,
            refType: "Invoice",
            refId: "INV-001",
            createdBy: null);

        Assert.Equal(movementType, movement.MovementType);
        Assert.Equal(5, movement.QtyDelta);
        Assert.Equal(100m, movement.UnitCost);
    }

    [Fact]
    public void Create_UnknownMovementType_ThrowsInvalidMovementType()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            InventoryMovement.Create(
                TenantId, BranchId, ProductId, null,
                "UnknownType", 5, 100m, "Invoice", "INV-001", null));

        Assert.Equal("invalid_movement_type", ex.Code);
    }

    [Fact]
    public void Create_ZeroQtyDelta_ThrowsInvalidQtyDelta()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            InventoryMovement.Create(
                TenantId, BranchId, ProductId, null,
                MovementType.Purchase, 0, 100m, "Invoice", "INV-001", null));

        Assert.Equal("invalid_qty_delta", ex.Code);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-1)]
    public void Create_PositiveAndNegativeDeltas_BothAccepted(int qtyDelta)
    {
        var movement = InventoryMovement.Create(
            tenantId: TenantId,
            branchId: BranchId,
            productId: ProductId,
            imeiId: null,
            movementType: qtyDelta > 0 ? MovementType.Purchase : MovementType.Sale,
            qtyDelta: qtyDelta,
            unitCost: 100m,
            refType: "Invoice",
            refId: "INV-001",
            createdBy: null);

        Assert.Equal(qtyDelta, movement.QtyDelta);
    }
}
