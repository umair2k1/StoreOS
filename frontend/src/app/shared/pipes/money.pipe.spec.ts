import { describe, it, expect } from 'vitest';
import { PzMoneyPipe, PzMoneyAliasPipe } from './money.pipe';

describe('PzMoneyPipe', () => {
  const pipe = new PzMoneyPipe();
  const aliasPipe = new PzMoneyAliasPipe();

  it('formats zero correctly', () => {
    expect(pipe.transform(0)).toBe('₹0.00');
    expect(aliasPipe.transform(0)).toBe('₹0.00');
  });

  it('formats small and medium positive numbers with two decimal places', () => {
    expect(pipe.transform(12)).toBe('₹12.00');
    expect(pipe.transform(99.5)).toBe('₹99.50');
    expect(pipe.transform(1000)).toBe('₹1,000.00');
  });

  it('formats negative numbers correctly', () => {
    expect(pipe.transform(-50.5)).toBe('-₹50.50');
    expect(pipe.transform(-1000)).toBe('-₹1,000.00');
  });

  it('formats large numbers according to Indian numbering system (lakhs and crores)', () => {
    expect(pipe.transform(100000)).toBe('₹1,00,000.00');
    expect(pipe.transform(1234567.89)).toBe('₹12,34,567.89');
    expect(pipe.transform(10000000)).toBe('₹1,00,00,000.00');
  });

  it('formats numeric strings correctly', () => {
    expect(pipe.transform('499.99')).toBe('₹499.99');
    expect(pipe.transform('100000')).toBe('₹1,00,000.00');
  });

  it('returns empty string for null, undefined, empty string, and NaN', () => {
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform(undefined)).toBe('');
    expect(pipe.transform('')).toBe('');
    expect(pipe.transform('invalid-number')).toBe('');
    expect(pipe.transform(NaN)).toBe('');
  });
});
