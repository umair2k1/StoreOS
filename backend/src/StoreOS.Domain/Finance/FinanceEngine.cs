using StoreOS.Domain.Common;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Finance;

public static class FinanceEngine
{
    public static decimal CalculatePrincipal(decimal grandTotal, decimal downPayment)
    {
        if (downPayment < 0)
            throw new DomainValidationException("finance_down_payment_negative", "Down payment cannot be negative.");
        if (downPayment > grandTotal)
            throw new DomainValidationException("finance_down_payment_exceeds_total", "Down payment cannot exceed grand total.");
        var principal = grandTotal - downPayment;
        if (principal <= 0)
            throw new DomainValidationException("finance_principal_not_positive", "Financed principal must be greater than zero.");
        return principal;
    }

    public static decimal CalculateEmi(decimal principal, decimal annualRatePercent, int months)
    {
        if (principal <= 0)
            throw new DomainValidationException("finance_principal_not_positive", "Financed principal must be greater than zero.");
        if (months <= 0)
            throw new DomainValidationException("finance_invalid_tenure", "Tenure in months must be greater than zero.");
        if (annualRatePercent < 0)
            throw new DomainValidationException("finance_invalid_rate", "Annual interest rate cannot be negative.");

        if (annualRatePercent == 0m)
            return Money.Round2(principal / months);

        var r = annualRatePercent / 12m / 100m;
        var pow = DecimalPow(1m + r, months);
        var emi = principal * r * pow / (pow - 1m);
        return Money.Round2(emi);
    }

    private static decimal DecimalPow(decimal baseVal, int exp)
    {
        decimal result = 1m;
        for (int i = 0; i < exp; i++)
        {
            result *= baseVal;
        }
        return result;
    }
}
