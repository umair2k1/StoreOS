namespace StoreOS.Domain.Tenancy;

public class Setting
{
    public Guid TenantId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string Key { get; private set; } = default!;
    public string ValueJson { get; private set; } = default!;

    private Setting() { }

    public static Setting Create(Guid tenantId, Guid? branchId, string key, string valueJson)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Setting key is required.", nameof(key));
        return new Setting
        {
            TenantId = tenantId,
            BranchId = branchId,
            Key = key.Trim(),
            ValueJson = valueJson
        };
    }

    public void UpdateValue(string valueJson) => ValueJson = valueJson;
}
