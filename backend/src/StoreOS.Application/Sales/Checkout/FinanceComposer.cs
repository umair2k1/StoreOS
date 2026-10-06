using StoreOS.Domain.Finance;

namespace StoreOS.Application.Sales.Checkout;

public record ComposedFinance(string FinanceProvider, string? FinancerName, decimal DownPayment,
    int TenureMonths, decimal AnnualInterestRate, decimal Principal, decimal MonthlyEmi, decimal TotalPayable);

public static class FinanceComposer
{
    public static ComposedFinance Compose(CheckoutFinanceDetails details, decimal grandTotal)
    {
        var principal = FinanceEngine.CalculatePrincipal(grandTotal, details.DownPayment);
        var emi = FinanceEngine.CalculateEmi(principal, details.AnnualInterestRate, details.TenureMonths);
        var totalPayable = StoreOS.Domain.Common.Money.Round2(details.DownPayment + emi * details.TenureMonths);
        return new ComposedFinance(details.FinanceProvider, details.FinancerName, details.DownPayment,
            details.TenureMonths, details.AnnualInterestRate, principal, emi, totalPayable);
    }
}
