using Microsoft.EntityFrameworkCore;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Seed;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string tenantName = "Apex Mobiles Demo";
        string subdomain = "apex";
        bool force = false;
        string? connectionString = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "--help" or "-h")
            {
                PrintUsage();
                return 0;
            }

            if (arg == "--tenant-name" && i + 1 < args.Length)
            {
                tenantName = args[++i];
            }
            else if (arg is "--subdomain" or "-s" && i + 1 < args.Length)
            {
                subdomain = args[++i];
            }
            else if (arg == "--force")
            {
                force = true;
            }
            else if (arg is "--connection" or "-c" && i + 1 < args.Length)
            {
                connectionString = args[++i];
            }
        }

        var cs = connectionString
            ?? Environment.GetEnvironmentVariable("STOREOS_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=storeos;Username=storeos;Password=storeos_dev_password";

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(cs)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        try
        {
            var result = await SeedDemoTenant.SeedAsync(db, tenantName, subdomain, force);

            if (result.Skipped)
            {
                Console.WriteLine(result.Message);
                return 0;
            }

            Console.WriteLine("=============================================================");
            Console.WriteLine("                StoreOS Demo Tenant Seeded                   ");
            Console.WriteLine("=============================================================");
            Console.WriteLine($"Tenant ID:    {result.TenantId}");
            Console.WriteLine("Branches:");
            Console.WriteLine($"  - B01: Main Branch ({result.BranchB01Id})");
            Console.WriteLine($"  - B02: Mall Branch ({result.BranchB02Id})");
            Console.WriteLine();
            Console.WriteLine("Users (Password for all: Demo@12345):");
            Console.WriteLine("  - Owner:          owner@apex.example (All Branches)");
            Console.WriteLine("  - Branch Manager: manager@apex.example (Branch B01)");
            Console.WriteLine("  - Cashier:        cashier@apex.example (Branch B01)");
            Console.WriteLine("  - Technician:     technician@apex.example (Branch B01)");
            Console.WriteLine();
            Console.WriteLine("Entity Counts:");
            Console.WriteLine($"  - Products:        {result.ProductCount}");
            Console.WriteLine($"  - Inventories:     {result.InventoryCount}");
            Console.WriteLine($"  - InStock IMEIs:   {result.ImeiCount}");
            Console.WriteLine($"  - Customers:       {result.CustomerCount}");
            Console.WriteLine($"  - Suppliers:       {result.SupplierCount}");
            Console.WriteLine($"  - Sales:           {result.SaleCount}");
            Console.WriteLine($"  - Expenses:        {result.ExpenseCount}");
            Console.WriteLine($"  - Liabilities:     {result.LiabilityCount}");
            Console.WriteLine($"  - Repair Jobs:     {result.RepairJobCount}");
            Console.WriteLine("=============================================================");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Seeding failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run --project tools/seed -- [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  --tenant-name <name>    Tenant display name (default: \"Apex Mobiles Demo\")");
        Console.WriteLine("  --subdomain <subdomain> Tenant subdomain (default: \"apex\")");
        Console.WriteLine("  --force                 Wipe and re-seed if tenant already exists");
        Console.WriteLine("  --connection <conn>     PostgreSQL owner connection string");
    }
}
