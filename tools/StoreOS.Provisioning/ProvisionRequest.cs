namespace StoreOS.Provisioning;

public record ProvisionRequest(
    string Name,
    string Subdomain,
    string OwnerEmail,
    string OwnerName,
    string BranchCode = "B01",
    string BranchName = "Main Branch",
    string? Gstin = null,
    string Timezone = "Asia/Kolkata",
    bool Repairs = false,
    string? OwnerPassword = null
);
