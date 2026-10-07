import { describe, it, expect } from 'vitest';
import { PzDatePipe, PzDateAliasPipe } from './ist-date.pipe';

describe('PzDatePipe', () => {
  const pipe = new PzDatePipe();
  const aliasPipe = new PzDateAliasPipe();

  it('formats Date object to DD-MMM-YYYY by default', () => {
    const d = new Date(2025, 0, 15);
    expect(pipe.transform(d)).toBe('15-Jan-2025');
    expect(aliasPipe.transform(d)).toBe('15-Jan-2025');
  });

  it('formats date-only ISO string accurately without day shift', () => {
    expect(pipe.transform('2024-08-15')).toBe('15-Aug-2024');
    expect(pipe.transform('2026-10-08')).toBe('08-Oct-2026');
  });

  it('formats date with custom format including time', () => {
    const d = new Date(2025, 4, 3, 14, 30);
    expect(pipe.transform(d, 'DD-MMM-YYYY HH:mm')).toBe('03-May-2025 14:30');
  });

  it('formats numeric timestamp correctly', () => {
    const d = new Date(2025, 11, 25);
    expect(pipe.transform(d.getTime())).toBe('25-Dec-2025');
  });

  it('returns empty string for null, undefined, empty string, and invalid date', () => {
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform(undefined)).toBe('');
    expect(pipe.transform('')).toBe('');
    expect(pipe.transform('not-a-valid-date')).toBe('');
  });
});
