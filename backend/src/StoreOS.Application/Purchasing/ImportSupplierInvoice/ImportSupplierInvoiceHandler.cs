using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Purchasing.StockIn;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Platform;

namespace StoreOS.Application.Purchasing.ImportSupplierInvoice;

public class ImportSupplierInvoiceHandler : IRequestHandler<ImportSupplierInvoiceCommand, ImportResult>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;
    private readonly IMediator _mediator;

    public ImportSupplierInvoiceHandler(IAppDbContext db, IAccessScope scope, IBranchContext branchContext, IMediator mediator)
    {
        _db = db;
        _scope = scope;
        _branchContext = branchContext;
        _mediator = mediator;
    }

    public async Task<ImportResult> Handle(ImportSupplierInvoiceCommand request, CancellationToken ct)
    {
        var branchId = _branchContext.CurrentBranchId;

        // Step 1: Check import_batches for EXISTING row with SAME (tenant_id, source_ref)
        var existingBatch = await _db.Set<ImportBatch>()
            .FirstOrDefaultAsync(b => b.TenantId == _scope.TenantId && b.SourceRef == request.SourceRef, ct);

        if (existingBatch != null && existingBatch.Status == ImportStatus.Imported)
        {
            if (!string.IsNullOrWhiteSpace(existingBatch.RecordCounts))
            {
                var cached = JsonSerializer.Deserialize<ImportResultPayload>(existingBatch.RecordCounts);
                if (cached != null)
                {
                    return new ImportResult(
                        existingBatch.Id,
                        existingBatch.Status,
                        cached.ProductsMatched,
                        cached.ProductsAmbiguous,
                        cached.AmbiguousLines ?? Array.Empty<AmbiguousLine>(),
                        cached.PurchasesCreated);
                }
            }

            return new ImportResult(existingBatch.Id, existingBatch.Status, 0, 0, Array.Empty<AmbiguousLine>(), 0);
        }

        // Step 2: Fetch all tenant products to perform exact case-insensitive name matching
        var allProducts = await _db.Set<Product>()
            .Where(p => p.TenantId == _scope.TenantId)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(ct);

        var ambiguousLines = new List<AmbiguousLine>();
        var matchedLines = new List<(Guid ProductId, SupplierInvoiceImportLine Line)>();

        foreach (var line in request.Lines)
        {
            var rawName = line.ProductNameRaw.Trim();
            var matches = allProducts
                .Where(p => string.Equals(p.Name, rawName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 1)
            {
                matchedLines.Add((matches[0].Id, line));
            }
            else if (matches.Count == 0)
            {
                ambiguousLines.Add(new AmbiguousLine(line.ProductNameRaw, Array.Empty<Guid>()));
            }
            else
            {
                ambiguousLines.Add(new AmbiguousLine(line.ProductNameRaw, matches.Select(m => m.Id).ToList()));
            }
        }

        // Step 3: Process unambiguous lines
        var purchasesCreated = 0;
        foreach (var (productId, line) in matchedLines)
        {
            IReadOnlyList<string>? imeis = !string.IsNullOrWhiteSpace(line.Imei)
                ? new[] { line.Imei }
                : null;

            var stockInCmd = new StockInCommand(
                ProductId: productId,
                Qty: line.Qty,
                PurchaseRate: line.Rate,
                SupplierId: request.SupplierId,
                SupplierName: null,
                SupplierPhone: null,
                SupplierAddress: null,
                PurchaseDate: request.InvoiceDate,
                SupplierInvoiceNo: request.SupplierInvoiceNo,
                Imeis: imeis);

            await _mediator.Send(stockInCmd, ct);
            purchasesCreated++;
        }

        // Step 4: Create or update import_batches row
        var batch = existingBatch ?? ImportBatch.Create(
            _scope.TenantId,
            branchId,
            ImportSourceType.SupplierInvoice,
            request.SourceRef,
            _scope.UserId);

        var status = ambiguousLines.Count == 0 ? ImportStatus.Imported : ImportStatus.Pending;

        var payload = new ImportResultPayload(
            matchedLines.Count,
            ambiguousLines.Count,
            ambiguousLines,
            purchasesCreated);

        var countsJson = JsonSerializer.Serialize(payload);

        if (status == ImportStatus.Imported)
        {
            batch.MarkImported(countsJson);
        }
        else
        {
            batch.UpdateRecordCounts(countsJson);
        }

        if (existingBatch == null)
        {
            await _db.Set<ImportBatch>().AddAsync(batch, ct);
        }

        await _db.SaveChangesAsync(ct);

        return new ImportResult(
            batch.Id,
            status,
            matchedLines.Count,
            ambiguousLines.Count,
            ambiguousLines,
            purchasesCreated);
    }

    private record ImportResultPayload(
        int ProductsMatched,
        int ProductsAmbiguous,
        IReadOnlyList<AmbiguousLine> AmbiguousLines,
        int PurchasesCreated);
}
