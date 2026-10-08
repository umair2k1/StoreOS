import {
  calculateEmi,
  calculatePrincipal,
  decimalPow,
  round2AwayFromZero
} from './emi-calculator';

describe('emi-calculator', () => {
  it('Case 1: zero interest computes exact principal divided by months', () => {
    // principal = 12000, annualRatePercent = 0, months = 12
    const emi = calculateEmi(12000, 0, 12);
    expect(emi).toBe(1000.00);
  });

  it('Case 2: positive interest computes exact EMI with decimalPow loop matching C# FinanceEngine to 2 decimal places', () => {
    // principal = 12000, annualRatePercent = 12, months = 12
    // C# FinanceEngine result: 1066.19m
    const emi = calculateEmi(12000, 12, 12);
    expect(emi).toBe(1066.19);
  });

  it('decimalPow computes integer powers correctly without Math.pow', () => {
    expect(decimalPow(2, 3)).toBe(8);
    expect(decimalPow(1.01, 0)).toBe(1);
    expect(decimalPow(1.01, 1)).toBe(1.01);
    expect(round2AwayFromZero(decimalPow(1.01, 12))).toBe(1.13);
  });

  it('Case 3: throws error on invalid tenure (zero or negative)', () => {
    expect(() => calculateEmi(12000, 10, 0)).toThrow('Tenure in months must be greater than zero.');
    expect(() => calculateEmi(12000, 10, -5)).toThrow('Tenure in months must be greater than zero.');
  });

  it('Case 4: throws error on zero or negative principal', () => {
    expect(() => calculateEmi(0, 10, 12)).toThrow('Financed principal must be greater than zero.');
    expect(() => calculateEmi(-500, 10, 12)).toThrow('Financed principal must be greater than zero.');
  });

  it('Case 5: throws error on negative interest rate', () => {
    expect(() => calculateEmi(10000, -2, 12)).toThrow('Annual interest rate cannot be negative.');
  });

  it('calculatePrincipal validates down payment properly', () => {
    // Valid
    expect(calculatePrincipal(10000, 2000)).toBe(8000);

    // Negative down payment
    expect(() => calculatePrincipal(10000, -100)).toThrow('Down payment cannot be negative.');

    // Down payment exceeds grand total
    expect(() => calculatePrincipal(10000, 15000)).toThrow('Down payment cannot exceed grand total.');

    // Down payment equals grand total (principal = 0)
    expect(() => calculatePrincipal(10000, 10000)).toThrow('Financed principal must be greater than zero.');
  });
});
