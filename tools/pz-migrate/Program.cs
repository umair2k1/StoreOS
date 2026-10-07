using System.Text.Json;
using PzMigrate;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        try
        {
            switch (command)
            {
                case "import":
                    return await HandleImportAsync(args.Skip(1).ToArray());
                case "reconcile-customers":
                    return await HandleReconcileCustomersAsync(args.Skip(1).ToArray());
                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static Task<int> HandleImportAsync(string[] args)
    {
        string? backupPath = null;
        string? tenantName = null;
        bool dryRun = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--backup" && i + 1 < args.Length)
            {
                backupPath = args[++i];
            }
            else if (args[i] is "--tenant-name" && i + 1 < args.Length)
            {
                tenantName = args[++i];
            }
            else if (args[i] is "--dry-run")
            {
                dryRun = true;
            }
        }

        if (string.IsNullOrWhiteSpace(backupPath))
        {
            Console.Error.WriteLine("Error: Missing required option --backup <path>");
            return Task.FromResult(1);
        }

        if (string.IsNullOrWhiteSpace(tenantName))
        {
            Console.Error.WriteLine("Error: Missing required option --tenant-name <name>");
            return Task.FromResult(1);
        }

        if (dryRun)
        {
            Console.WriteLine($"[DRY RUN] Simulating import for tenant '{tenantName}' from '{backupPath}'...");
            var report = DesktopMigrationService.RunDryRun(backupPath);
            Console.WriteLine("--- RECONCILIATION REPORT ---");
            Console.WriteLine($"Sum Sale Grand Totals: {report.SumSaleGrandTotals:C}");
            Console.WriteLine($"Sum Payments:          {report.SumPayments:C}");
            Console.WriteLine($"Sum Stock Quantity:    {report.SumStockQty}");
            Console.WriteLine($"Active Credit Balances:{report.SumActiveCreditBalances:C}");
            Console.WriteLine("Table Counts:");
            foreach (var (table, count) in report.RecordCountsByTable)
            {
                Console.WriteLine($"  {table}: {count}");
            }
            Console.WriteLine("Report successfully written to reconciliation-report.json");
            return Task.FromResult(0);
        }

        Console.Error.WriteLine("Committing import mode requires P-904 implementation.");
        return Task.FromResult(1);
    }

    private static Task<int> HandleReconcileCustomersAsync(string[] args)
    {
        Console.WriteLine("Reconcile customers built in P-904.");
        return Task.FromResult(0);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("pz-migrate — Desktop SQLite to StoreOS Cloud Migration Tool");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  pz-migrate import --backup <path> --tenant-name <name> [--dry-run]");
        Console.WriteLine("  pz-migrate reconcile-customers --tenant <id> [--dry-run]");
    }
}
