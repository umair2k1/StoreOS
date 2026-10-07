using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PzMigrate;
using StoreOS.Infrastructure.Persistence;

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

    public static async Task<int> HandleImportAsync(string[] args, StoreOsDbContext? externalDb = null)
    {
        string? backupPath = null;
        string? tenantName = null;
        string? connectionString = null;
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
            else if (args[i] is "--connection" && i + 1 < args.Length)
            {
                connectionString = args[++i];
            }
            else if (args[i] is "--dry-run")
            {
                dryRun = true;
            }
        }

        if (string.IsNullOrWhiteSpace(backupPath))
        {
            Console.Error.WriteLine("Error: Missing required option --backup <path>");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(tenantName))
        {
            Console.Error.WriteLine("Error: Missing required option --tenant-name <name>");
            return 1;
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
            return 0;
        }

        StoreOsDbContext db;
        bool disposeDb = false;

        if (externalDb != null)
        {
            db = externalDb;
        }
        else
        {
            var connStr = connectionString
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                ?? "Host=localhost;Port=5432;Database=storeos;Username=storeos_owner;Password=storeos_dev_password";

            var options = new DbContextOptionsBuilder<StoreOsDbContext>()
                .UseNpgsql(connStr)
                .Options;

            db = new StoreOsDbContext(options, new NullAccessScope());
            disposeDb = true;
        }

        try
        {
            Console.WriteLine($"[COMMITTING IMPORT] Importing tenant '{tenantName}' from '{backupPath}'...");
            var summary = await DesktopMigrationService.RunImportAsync(backupPath, tenantName, db);
            Console.WriteLine($"Import complete for tenant '{tenantName}' (ID {summary.TenantId}):");
            Console.WriteLine($"  Inserted Products:   {summary.InsertedProducts}");
            Console.WriteLine($"  Inserted Customers:  {summary.InsertedCustomers}");
            Console.WriteLine($"  Updated Customers:   {summary.UpdatedCustomers}");
            Console.WriteLine($"  Inserted Sales:      {summary.InsertedSales}");
            Console.WriteLine($"  Inserted Purchases:  {summary.InsertedPurchases}");
            Console.WriteLine($"  Inserted IMEIs:      {summary.InsertedImeis}");
            Console.WriteLine($"  Skipped Duplicates:  {summary.SkippedDuplicates}");
            Console.WriteLine($"  Conflicts Detected:  {summary.ConflictCount}");
            return 0;
        }
        finally
        {
            if (disposeDb) await db.DisposeAsync();
        }
    }

    public static async Task<int> HandleReconcileCustomersAsync(string[] args, StoreOsDbContext? externalDb = null, TextReader? inputReader = null)
    {
        Guid? tenantId = null;
        string? connectionString = null;
        Guid? survivingId = null;
        Guid? duplicateId = null;
        bool dryRun = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--tenant" && i + 1 < args.Length && Guid.TryParse(args[++i], out var tid))
            {
                tenantId = tid;
            }
            else if (args[i] is "--connection" && i + 1 < args.Length)
            {
                connectionString = args[++i];
            }
            else if (args[i] is "--surviving" && i + 1 < args.Length && Guid.TryParse(args[++i], out var sId))
            {
                survivingId = sId;
            }
            else if (args[i] is "--duplicate" && i + 1 < args.Length && Guid.TryParse(args[++i], out var dId))
            {
                duplicateId = dId;
            }
            else if (args[i] is "--dry-run")
            {
                dryRun = true;
            }
        }

        if (tenantId == null)
        {
            Console.Error.WriteLine("Error: Missing required option --tenant <id>");
            return 1;
        }

        StoreOsDbContext db;
        bool disposeDb = false;

        if (externalDb != null)
        {
            db = externalDb;
        }
        else
        {
            var connStr = connectionString
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                ?? "Host=localhost;Port=5432;Database=storeos;Username=storeos_owner;Password=storeos_dev_password";

            var options = new DbContextOptionsBuilder<StoreOsDbContext>()
                .UseNpgsql(connStr)
                .Options;

            db = new StoreOsDbContext(options, new NullAccessScope());
            disposeDb = true;
        }

        try
        {
            // List customers with duplicate phones or conflicts in this tenant
            var customers = await db.Customers.IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId.Value && c.Phone != null)
                .ToListAsync();

            var duplicates = customers.GroupBy(c => c.Phone)
                .Where(g => g.Count() > 1)
                .ToList();

            Console.WriteLine($"Found {duplicates.Count} phone group(s) with multiple customer records in tenant {tenantId}:");
            foreach (var group in duplicates)
            {
                Console.WriteLine($"  Phone: {group.Key}");
                foreach (var c in group)
                {
                    Console.WriteLine($"    Customer ID: {c.Id}, Name: {c.Name}, DesktopId: {c.DesktopId}");
                }
            }

            if (dryRun)
            {
                Console.WriteLine("[DRY RUN] No customer records modified.");
                return 0;
            }

            if (survivingId == null || duplicateId == null)
            {
                var reader = inputReader ?? Console.In;
                Console.Write("Enter surviving customer ID: ");
                var sInput = reader.ReadLine();
                Console.Write("Enter duplicate customer ID to merge & delete: ");
                var dInput = reader.ReadLine();

                if (Guid.TryParse(sInput, out var parsedSurviving) && Guid.TryParse(dInput, out var parsedDuplicate))
                {
                    survivingId = parsedSurviving;
                    duplicateId = parsedDuplicate;
                }
                else
                {
                    Console.Error.WriteLine("Invalid customer IDs provided.");
                    return 1;
                }
            }

            await DesktopMigrationService.MergeCustomersAsync(db, survivingId.Value, duplicateId.Value);
            Console.WriteLine($"Successfully merged duplicate customer {duplicateId} into surviving customer {survivingId}.");
            return 0;
        }
        finally
        {
            if (disposeDb) await db.DisposeAsync();
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("pz-migrate — Desktop SQLite to StoreOS Cloud Migration Tool");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  pz-migrate import --backup <path> --tenant-name <name> [--dry-run] [--connection <conn>]");
        Console.WriteLine("  pz-migrate reconcile-customers --tenant <id> [--dry-run] [--surviving <id> --duplicate <id>]");
    }
}
