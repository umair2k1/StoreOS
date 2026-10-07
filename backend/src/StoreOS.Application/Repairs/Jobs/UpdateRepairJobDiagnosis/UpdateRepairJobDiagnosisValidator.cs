using FluentValidation;

namespace StoreOS.Application.Repairs.Jobs.UpdateRepairJobDiagnosis;

public class UpdateRepairJobDiagnosisValidator : AbstractValidator<UpdateRepairJobDiagnosisCommand>
{
    public UpdateRepairJobDiagnosisValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Repair job ID is required.");
    }
}
