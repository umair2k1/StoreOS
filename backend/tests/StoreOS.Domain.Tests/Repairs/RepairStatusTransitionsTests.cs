using FluentAssertions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Domain.Tests.Repairs;

public class RepairStatusTransitionsTests
{
    [Theory]
    [InlineData(RepairStatus.Received, RepairStatus.Diagnosing)]
    [InlineData(RepairStatus.Diagnosing, RepairStatus.WaitingForApproval)]
    [InlineData(RepairStatus.WaitingForApproval, RepairStatus.WaitingForParts)]
    [InlineData(RepairStatus.WaitingForParts, RepairStatus.Diagnosing)] // parts wrong, go back to diagnosing
    [InlineData(RepairStatus.WaitingForParts, RepairStatus.InRepair)]
    [InlineData(RepairStatus.InRepair, RepairStatus.ReadyForPickup)]
    [InlineData(RepairStatus.ReadyForPickup, RepairStatus.Delivered)]
    [InlineData(RepairStatus.Received, RepairStatus.Cancelled)]
    [InlineData(RepairStatus.Diagnosing, RepairStatus.Cancelled)]
    [InlineData(RepairStatus.WaitingForApproval, RepairStatus.Cancelled)]
    [InlineData(RepairStatus.WaitingForParts, RepairStatus.Cancelled)]
    [InlineData(RepairStatus.InRepair, RepairStatus.Cancelled)]
    [InlineData(RepairStatus.ReadyForPickup, RepairStatus.Cancelled)]
    public void IsLegal_LegalTransitions_ReturnTrue(string from, string to)
    {
        RepairStatusTransitions.IsLegal(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(RepairStatus.Delivered, RepairStatus.InRepair)] // terminal state
    [InlineData(RepairStatus.Delivered, RepairStatus.Received)]
    [InlineData(RepairStatus.Cancelled, RepairStatus.Received)] // terminal state
    [InlineData(RepairStatus.Cancelled, RepairStatus.Diagnosing)]
    [InlineData(RepairStatus.Received, RepairStatus.Delivered)] // Delivered only from ReadyForPickup
    [InlineData(RepairStatus.Diagnosing, RepairStatus.Delivered)]
    [InlineData(RepairStatus.WaitingForApproval, RepairStatus.Delivered)]
    [InlineData(RepairStatus.WaitingForParts, RepairStatus.Delivered)]
    [InlineData(RepairStatus.InRepair, RepairStatus.Delivered)]
    [InlineData(RepairStatus.Received, RepairStatus.Received)] // same status
    [InlineData(RepairStatus.InRepair, RepairStatus.InRepair)]
    public void IsLegal_IllegalTransitions_ReturnFalse(string from, string to)
    {
        RepairStatusTransitions.IsLegal(from, to).Should().BeFalse();
    }
}
