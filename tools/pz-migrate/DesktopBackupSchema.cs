using System.Text.Json.Serialization;

namespace PzMigrate;

public record ReconciliationReport(
    Dictionary<string, int> RecordCountsByTable,
    decimal SumSaleGrandTotals,
    decimal SumPayments,
    int SumStockQty,
    decimal SumActiveCreditBalances
);

public class DesktopBackup
{
    [JsonPropertyName("settings")]
    public List<DesktopSetting> Settings { get; set; } = new();

    [JsonPropertyName("items")]
    public List<DesktopItem> Items { get; set; } = new();

    [JsonPropertyName("purchases")]
    public List<DesktopPurchase> Purchases { get; set; } = new();

    [JsonPropertyName("sales")]
    public List<DesktopSale> Sales { get; set; } = new();

    [JsonPropertyName("sale_items")]
    public List<DesktopSaleItem> SaleItems { get; set; } = new();

    [JsonPropertyName("customers")]
    public List<DesktopCustomer> Customers { get; set; } = new();

    [JsonPropertyName("customer_payments")]
    public List<DesktopCustomerPayment> CustomerPayments { get; set; } = new();

    [JsonPropertyName("expenses")]
    public List<DesktopExpense> Expenses { get; set; } = new();

    [JsonPropertyName("suppliers")]
    public List<DesktopSupplier> Suppliers { get; set; } = new();

    [JsonPropertyName("supplier_transactions")]
    public List<DesktopSupplierTransaction> SupplierTransactions { get; set; } = new();

    [JsonPropertyName("liabilities")]
    public List<DesktopLiability> Liabilities { get; set; } = new();

    [JsonPropertyName("liability_payments")]
    public List<DesktopLiabilityPayment> LiabilityPayments { get; set; } = new();

    [JsonPropertyName("inventory_imeis")]
    public List<DesktopInventoryImei> InventoryImeis { get; set; } = new();

    [JsonPropertyName("supplier_returns")]
    public List<DesktopSupplierReturn> SupplierReturns { get; set; } = new();

    [JsonPropertyName("supplier_return_items")]
    public List<DesktopSupplierReturnItem> SupplierReturnItems { get; set; } = new();

    [JsonPropertyName("repair_services")]
    public List<DesktopRepairService> RepairServices { get; set; } = new();

    [JsonPropertyName("repair_jobs")]
    public List<DesktopRepairJob> RepairJobs { get; set; } = new();

    [JsonPropertyName("repair_job_parts")]
    public List<DesktopRepairJobPart> RepairJobParts { get; set; } = new();

    [JsonPropertyName("repair_job_status_history")]
    public List<DesktopRepairJobStatusHistory> RepairJobStatusHistory { get; set; } = new();
}

public class DesktopSetting
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = default!;

    [JsonPropertyName("value")]
    public string Value { get; set; } = default!;
}

public class DesktopItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("category")]
    public string Category { get; set; } = default!;

    [JsonPropertyName("stock_qty")]
    public int StockQty { get; set; }

    [JsonPropertyName("purchase_price")]
    public decimal PurchasePrice { get; set; }

    [JsonPropertyName("sell_price")]
    public decimal SellPrice { get; set; }

    [JsonPropertyName("gst_rate")]
    public decimal GstRate { get; set; } = 18m;

    [JsonPropertyName("is_margin_scheme")]
    public bool IsMarginScheme { get; set; }

    [JsonPropertyName("hsn_code")]
    public string? HsnCode { get; set; }

    [JsonPropertyName("seller_name")]
    public string? SellerName { get; set; }

    [JsonPropertyName("seller_phone")]
    public string? SellerPhone { get; set; }

    [JsonPropertyName("seller_address")]
    public string? SellerAddress { get; set; }

    [JsonPropertyName("supplier_name")]
    public string? SupplierName { get; set; }

    [JsonPropertyName("supplier_phone")]
    public string? SupplierPhone { get; set; }

    [JsonPropertyName("supplier_address")]
    public string? SupplierAddress { get; set; }
}

public class DesktopPurchase
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = default!;

    [JsonPropertyName("qty")]
    public int Qty { get; set; }

    [JsonPropertyName("purchase_rate")]
    public decimal PurchaseRate { get; set; }

    [JsonPropertyName("supplier_id")]
    public string? SupplierId { get; set; }

    [JsonPropertyName("supplier_name")]
    public string? SupplierName { get; set; }

    [JsonPropertyName("supplier_phone")]
    public string? SupplierPhone { get; set; }

    [JsonPropertyName("supplier_address")]
    public string? SupplierAddress { get; set; }

    [JsonPropertyName("seller_name")]
    public string? SellerName { get; set; }

    [JsonPropertyName("seller_phone")]
    public string? SellerPhone { get; set; }

    [JsonPropertyName("seller_address")]
    public string? SellerAddress { get; set; }

    [JsonPropertyName("supplier_invoice_no")]
    public string? SupplierInvoiceNo { get; set; }

    [JsonPropertyName("purchase_date")]
    public string PurchaseDate { get; set; } = default!;

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public class DesktopSale
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("invoice_number")]
    public string InvoiceNumber { get; set; } = default!;

    [JsonPropertyName("sale_date")]
    public string SaleDate { get; set; } = default!;

    [JsonPropertyName("customer_id")]
    public string? CustomerId { get; set; }

    [JsonPropertyName("customer_name")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("customer_gstin")]
    public string? CustomerGstin { get; set; }

    [JsonPropertyName("total_taxable")]
    public decimal TotalTaxable { get; set; }

    [JsonPropertyName("total_cgst")]
    public decimal TotalCgst { get; set; }

    [JsonPropertyName("total_sgst")]
    public decimal TotalSgst { get; set; }

    [JsonPropertyName("grand_total")]
    public decimal GrandTotal { get; set; }

    [JsonPropertyName("discount")]
    public decimal Discount { get; set; }

    [JsonPropertyName("amount_paid")]
    public decimal AmountPaid { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Completed";

    [JsonPropertyName("payment_mode")]
    public string? PaymentMode { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public class DesktopSaleItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("sale_id")]
    public string SaleId { get; set; } = default!;

    [JsonPropertyName("item_id")]
    public string? ItemId { get; set; }

    [JsonPropertyName("item_name")]
    public string ItemName { get; set; } = default!;

    [JsonPropertyName("category")]
    public string Category { get; set; } = default!;

    [JsonPropertyName("qty")]
    public int Qty { get; set; }

    [JsonPropertyName("price_per_unit")]
    public decimal PricePerUnit { get; set; }

    [JsonPropertyName("taxable")]
    public decimal Taxable { get; set; }

    [JsonPropertyName("cgst")]
    public decimal Cgst { get; set; }

    [JsonPropertyName("sgst")]
    public decimal Sgst { get; set; }

    [JsonPropertyName("imei")]
    public string? Imei { get; set; }

    [JsonPropertyName("is_margin_scheme")]
    public bool IsMarginScheme { get; set; }
}

public class DesktopCustomer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("gstin")]
    public string? Gstin { get; set; }

    [JsonPropertyName("total_purchases")]
    public decimal TotalPurchases { get; set; }

    [JsonPropertyName("credit_balance")]
    public decimal CreditBalance { get; set; }

    [JsonPropertyName("credit_limit")]
    public decimal? CreditLimit { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public class DesktopCustomerPayment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("sale_id")]
    public string? SaleId { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("payment_mode")]
    public string PaymentMode { get; set; } = default!;

    [JsonPropertyName("payment_date")]
    public string PaymentDate { get; set; } = default!;

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public class DesktopExpense
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("description")]
    public string Description { get; set; } = default!;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = default!;

    [JsonPropertyName("expense_date")]
    public string ExpenseDate { get; set; } = default!;

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public class DesktopSupplier
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("brand_or_category")]
    public string? BrandOrCategory { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public class DesktopSupplierTransaction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("supplier_id")]
    public string SupplierId { get; set; } = default!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = default!;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("payment_mode")]
    public string? PaymentMode { get; set; }

    [JsonPropertyName("reference_no")]
    public string? ReferenceNo { get; set; }

    [JsonPropertyName("txn_date")]
    public string TxnDate { get; set; } = default!;

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public class DesktopLiability
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = default!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = default!;

    [JsonPropertyName("lender_name")]
    public string? LenderName { get; set; }

    [JsonPropertyName("total_principal")]
    public decimal TotalPrincipal { get; set; }

    [JsonPropertyName("due_date")]
    public string? DueDate { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public class DesktopLiabilityPayment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("liability_id")]
    public string LiabilityId { get; set; } = default!;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("payment_mode")]
    public string PaymentMode { get; set; } = default!;

    [JsonPropertyName("payment_date")]
    public string PaymentDate { get; set; } = default!;

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public class DesktopInventoryImei
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = default!;

    [JsonPropertyName("purchase_id")]
    public string? PurchaseId { get; set; }

    [JsonPropertyName("imei")]
    public string Imei { get; set; } = default!;

    [JsonPropertyName("status")]
    public string Status { get; set; } = default!;

    [JsonPropertyName("sale_id")]
    public string? SaleId { get; set; }

    [JsonPropertyName("sale_item_id")]
    public string? SaleItemId { get; set; }

    [JsonPropertyName("purchase_date")]
    public string? PurchaseDate { get; set; }

    [JsonPropertyName("purchase_price")]
    public decimal? PurchasePrice { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public class DesktopSupplierReturn
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("return_number")]
    public string ReturnNumber { get; set; } = default!;

    [JsonPropertyName("return_date")]
    public string ReturnDate { get; set; } = default!;

    [JsonPropertyName("supplier_id")]
    public string SupplierId { get; set; } = default!;

    [JsonPropertyName("total_qty")]
    public int TotalQty { get; set; }

    [JsonPropertyName("total_amount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("settlement_type")]
    public string SettlementType { get; set; } = default!;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public class DesktopSupplierReturnItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("return_id")]
    public string ReturnId { get; set; } = default!;

    [JsonPropertyName("item_id")]
    public string? ItemId { get; set; }

    [JsonPropertyName("item_name")]
    public string ItemName { get; set; } = default!;

    [JsonPropertyName("qty")]
    public int Qty { get; set; }

    [JsonPropertyName("purchase_price")]
    public decimal PurchasePrice { get; set; }

    [JsonPropertyName("return_rate")]
    public decimal ReturnRate { get; set; }

    [JsonPropertyName("line_total")]
    public decimal LineTotal { get; set; }

    [JsonPropertyName("imei_numbers")]
    public List<string>? ImeiNumbers { get; set; }
}

public class DesktopRepairService
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("estimated_duration_minutes")]
    public int? EstimatedDurationMinutes { get; set; }

    [JsonPropertyName("warranty_days")]
    public int? WarrantyDays { get; set; }
}

public class DesktopRepairJob
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("job_number")]
    public string JobNumber { get; set; } = default!;

    [JsonPropertyName("customer_id")]
    public string? CustomerId { get; set; }

    [JsonPropertyName("customer_name")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("customer_phone")]
    public string? CustomerPhone { get; set; }

    [JsonPropertyName("device_brand")]
    public string? DeviceBrand { get; set; }

    [JsonPropertyName("device_model")]
    public string? DeviceModel { get; set; }

    [JsonPropertyName("problem_description")]
    public string? ProblemDescription { get; set; }

    [JsonPropertyName("estimated_cost")]
    public decimal? EstimatedCost { get; set; }

    [JsonPropertyName("final_total")]
    public decimal? FinalTotal { get; set; }

    [JsonPropertyName("advance_paid")]
    public decimal? AdvancePaid { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = default!;

    [JsonPropertyName("received_at")]
    public string? ReceivedAt { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public class DesktopRepairJobPart
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("repair_job_id")]
    public string RepairJobId { get; set; } = default!;

    [JsonPropertyName("part_name")]
    public string PartName { get; set; } = default!;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("unit_cost")]
    public decimal UnitCost { get; set; }

    [JsonPropertyName("charge_amount")]
    public decimal ChargeAmount { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = default!;

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public class DesktopRepairJobStatusHistory
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("repair_job_id")]
    public string RepairJobId { get; set; } = default!;

    [JsonPropertyName("from_status")]
    public string? FromStatus { get; set; }

    [JsonPropertyName("to_status")]
    public string ToStatus { get; set; } = default!;

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("changed_at")]
    public string ChangedAt { get; set; } = default!;
}
