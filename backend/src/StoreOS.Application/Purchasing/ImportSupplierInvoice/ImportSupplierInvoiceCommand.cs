using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Purchasing.ImportSupplierInvoice;

public record SupplierInvoiceImportLine(string ProductNameRaw, string? HsnCode, int Qty,
    decimal Rate, string? Imei);

public record ImportSupplierInvoiceCommand(Guid SupplierId, string SupplierInvoiceNo,
    DateOnly InvoiceDate, string SourceRef, IReadOnlyList<SupplierInvoiceImportLine> Lines)
    : IRequest<ImportResult>, IRequiresPermission
{
    public string RequiredPermission => "purchases:manage";
}

public record ImportResult(Guid ImportBatchId, string Status, int ProductsMatched,
    int ProductsAmbiguous, IReadOnlyList<AmbiguousLine> AmbiguousLines, int PurchasesCreated);

public record AmbiguousLine(string ProductNameRaw, IReadOnlyList<Guid> CandidateProductIds);
