/**
 * PREVIEW ONLY:
 * Computes client-side estimated EMI for responsive UI preview without server round-trips.
 * Re-implements the pure zero-interest and positive-interest formulas from FinanceEngine.CalculateEmi (P-204),
 * including the DecimalPow-equivalent integer-exponent loop rather than Math.pow.
 *
 * KNOWN, ACCEPTABLE MINOR PRECISION DIFFERENCE:
 * JavaScript numbers are IEEE 754 64-bit floating point, whereas the C# backend uses 128-bit decimal.
 * This client preview is UX ONLY. The server recomputes the authoritative, persisted EMI at checkout
 * time (P-515) via FinanceEngine.
 */

export function decimalPow(baseVal: number, exp: number): number {
  let result = 1;
  for (let i = 0; i < exp; i++) {
    result *= baseVal;
  }
  return result;
}

export function round2AwayFromZero(val: number): number {
  const factor = 100;
  return Math.sign(val) * Math.round(Math.abs(val) * factor) / factor;
}

export function calculatePrincipal(grandTotal: number, downPayment: number): number {
  if (downPayment < 0) {
    throw new Error('Down payment cannot be negative.');
  }
  if (downPayment > grandTotal) {
    throw new Error('Down payment cannot exceed grand total.');
  }
  const principal = grandTotal - downPayment;
  if (principal <= 0) {
    throw new Error('Financed principal must be greater than zero.');
  }
  return round2AwayFromZero(principal);
}

export function calculateEmi(principal: number, annualRatePercent: number, months: number): number {
  if (principal <= 0) {
    throw new Error('Financed principal must be greater than zero.');
  }
  if (months <= 0) {
    throw new Error('Tenure in months must be greater than zero.');
  }
  if (annualRatePercent < 0) {
    throw new Error('Annual interest rate cannot be negative.');
  }

  if (annualRatePercent === 0) {
    return round2AwayFromZero(principal / months);
  }

  const r = annualRatePercent / 12 / 100;
  const pow = decimalPow(1 + r, months);
  const emi = (principal * r * pow) / (pow - 1);
  return round2AwayFromZero(emi);
}
