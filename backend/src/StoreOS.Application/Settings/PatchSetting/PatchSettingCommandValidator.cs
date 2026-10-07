using FluentValidation;
using System.Text.Json;

namespace StoreOS.Application.Settings.PatchSetting;

public class PatchSettingCommandValidator : AbstractValidator<PatchSettingCommand>
{
    public PatchSettingCommandValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ValueJson).NotEmpty().Must(BeValidJson).WithMessage("Value must be valid JSON.");
    }

    private static bool BeValidJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
