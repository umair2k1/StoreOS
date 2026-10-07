using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Repairs.Jobs.CreateRepairJob;

public class CreateRepairJobHandler : IRequestHandler<CreateRepairJobCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;
    private readonly IInvoiceSequenceAllocator _sequenceAllocator;

    public CreateRepairJobHandler(
        IAppDbContext db,
        IAccessScope scope,
        IBranchContext branchContext,
        IInvoiceSequenceAllocator sequenceAllocator)
    {
        _db = db;
        _scope = scope;
        _branchContext = branchContext;
        _sequenceAllocator = sequenceAllocator;
    }

    public async Task<Guid> Handle(CreateRepairJobCommand request, CancellationToken ct)
    {
        var branchId = _branchContext.CurrentBranchId
            ?? throw new DomainValidationException("branch_context_required", "Branch context is required for creating a repair job.");

        var branch = await _db.Set<Branch>()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == branchId, ct);

        if (branch == null)
        {
            throw new DomainException("branch_not_found", $"Branch '{branchId}' was not found.");
        }

        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var seriesKey = $"REP-{branch.Code}-{date:yyyyMMdd}";
        var seq = await _sequenceAllocator.AllocateNextAsync(_scope.TenantId, branchId, seriesKey, ct);
        var jobNumber = RepairJobNumberFormatter.Format(branch.Code, date, seq);

        var job = RepairJob.Intake(
            _scope.TenantId,
            branchId,
            jobNumber,
            request.CustomerId,
            request.CustomerName,
            request.CustomerPhone,
            request.CustomerAddress,
            request.DeviceBrand,
            request.DeviceModel,
            request.DeviceType,
            request.SerialNumber,
            request.Imei1,
            request.Imei2,
            request.DeviceCondition,
            request.AccessoriesReceived,
            request.ProblemDescription,
            request.CustomerNotes,
            request.EstimatedCost,
            request.AdvancePaid,
            request.ExpectedCompletion);

        await _db.Set<RepairJob>().AddAsync(job, ct);
        await _db.SaveChangesAsync(ct);

        return job.Id;
    }
}
