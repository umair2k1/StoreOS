namespace StoreOS.Domain.Repairs;

public static class RepairStatusTransitions
{
    private static readonly HashSet<string> Terminal = new() { RepairStatus.Delivered, RepairStatus.Cancelled };
    private static readonly HashSet<string> NonTerminal = new() {
        RepairStatus.Received, RepairStatus.Diagnosing, RepairStatus.WaitingForApproval,
        RepairStatus.WaitingForParts, RepairStatus.InRepair, RepairStatus.ReadyForPickup
    };

    public static bool IsLegal(string from, string to)
    {
        if (Terminal.Contains(from)) return false; // terminal states never transition further
        if (to == RepairStatus.Delivered) return from == RepairStatus.ReadyForPickup;
        if (to == RepairStatus.Cancelled) return NonTerminal.Contains(from);
        return NonTerminal.Contains(from) && NonTerminal.Contains(to) && from != to;
    }
}
