using StoreOS.Provisioning;

namespace StoreOS.Provision;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string? name = null;
        string? subdomain = null;
        string? ownerEmail = null;
        string? ownerName = null;
        string branchCode = "B01";
        string branchName = "Main Branch";
        string? gstin = null;
        string timezone = "Asia/Kolkata";
        bool repairs = false;
        string? ownerPassword = null;
        string? connectionString = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "--help" or "-h")
            {
                PrintUsage();
                return 0;
            }

            if (arg is "--name" or "-n" && i + 1 < args.Length)
            {
                name = args[++i];
            }
            else if (arg is "--subdomain" or "-s" && i + 1 < args.Length)
            {
                subdomain = args[++i];
            }
            else if (arg == "--owner-email" && i + 1 < args.Length)
            {
                ownerEmail = args[++i];
            }
            else if (arg == "--owner-name" && i + 1 < args.Length)
            {
                ownerName = args[++i];
            }
            else if (arg == "--branch-code" && i + 1 < args.Length)
            {
                branchCode = args[++i];
            }
            else if (arg == "--branch-name" && i + 1 < args.Length)
            {
                branchName = args[++i];
            }
            else if (arg == "--gstin" && i + 1 < args.Length)
            {
                gstin = args[++i];
            }
            else if (arg == "--timezone" && i + 1 < args.Length)
            {
                timezone = args[++i];
            }
            else if (arg == "--repairs")
            {
                if (i + 1 < args.Length && bool.TryParse(args[i + 1], out var parsedRepairs))
                {
                    repairs = parsedRepairs;
                    i++;
                }
                else
                {
                    repairs = true;
                }
            }
            else if (arg == "--owner-password" && i + 1 < args.Length)
            {
                ownerPassword = args[++i];
            }
            else if (arg is "--connection" or "-c" && i + 1 < args.Length)
            {
                connectionString = args[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(subdomain) ||
            string.IsNullOrWhiteSpace(ownerEmail) ||
            string.IsNullOrWhiteSpace(ownerName))
        {
            Console.Error.WriteLine("Error: Missing required arguments (--name, --subdomain, --owner-email, --owner-name).");
            PrintUsage();
            return 1;
        }

        var request = new ProvisionRequest(
            Name: name,
            Subdomain: subdomain,
            OwnerEmail: ownerEmail,
            OwnerName: ownerName,
            BranchCode: branchCode,
            BranchName: branchName,
            Gstin: gstin,
            Timezone: timezone,
            Repairs: repairs,
            OwnerPassword: ownerPassword
        );

        try
        {
            var service = connectionString != null
                ? new ProvisioningService(connectionString)
                : new ProvisioningService(Environment.GetEnvironmentVariable("STOREOS_MIGRATIONS_CONNECTION")
                    ?? "Host=localhost;Port=5432;Database=storeos;Username=storeos;Password=storeos_dev_password");

            var result = await service.ProvisionAsync(request);

            if (!result.Success)
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                return result.ExitCode;
            }

            Console.WriteLine("Tenant provisioned successfully!");
            Console.WriteLine($"Temporary Owner Password: {result.OwnerPassword}");
            Console.WriteLine($"Login URL: {result.LoginUrl}");
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Validation Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected Error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run --project tools/provision -- [options]");
        Console.WriteLine("Required options:");
        Console.WriteLine("  --name <name>               Tenant display name (e.g. \"Apex Mobiles\")");
        Console.WriteLine("  --subdomain <subdomain>     Tenant subdomain (e.g. \"apex\")");
        Console.WriteLine("  --owner-email <email>       Owner user email address");
        Console.WriteLine("  --owner-name <name>         Owner user full name");
        Console.WriteLine("Optional options:");
        Console.WriteLine("  --branch-code <code>        Default branch code (default: B01)");
        Console.WriteLine("  --branch-name <name>        Default branch name (default: Main Branch)");
        Console.WriteLine("  --gstin <gstin>             15-character GSTIN (optional)");
        Console.WriteLine("  --timezone <tz>             Tenant timezone (default: Asia/Kolkata)");
        Console.WriteLine("  --repairs <true|false>      Enable repairs module (default: false)");
        Console.WriteLine("  --owner-password <password> Set specific owner password (optional)");
        Console.WriteLine("  --connection <conn_str>     Owner connection string (optional)");
    }
}
