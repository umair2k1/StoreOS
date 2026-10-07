import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ToastService } from './toast.service';

describe('ToastService', () => {
  let service: ToastService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [ToastService]
    });
    service = TestBed.inject(ToastService);
  });

  it('starts with an empty toast queue', () => {
    expect(service.toasts().length).toBe(0);
  });

  it('enqueueing a toast makes it appear in the queue with correct severity', () => {
    const id = service.success('Sale completed successfully');

    const current = service.toasts();
    expect(current.length).toBe(1);
    expect(current[0].id).toBe(id);
    expect(current[0].message).toBe('Sale completed successfully');
    expect(current[0].severity).toBe('success');
  });

  it('auto-dismiss removes toast after configured timeout', () => {
    vi.useFakeTimers();
    try {
      service.info('Short notification', 2000);
      expect(service.toasts().length).toBe(1);

      vi.advanceTimersByTime(1000);
      expect(service.toasts().length).toBe(1);

      vi.advanceTimersByTime(1000);
      expect(service.toasts().length).toBe(0);
    } finally {
      vi.useRealTimers();
    }
  });

  it('dismiss removes a specific toast immediately', () => {
    const id1 = service.info('First toast', 0);
    const id2 = service.error('Second toast', 0);

    expect(service.toasts().length).toBe(2);

    service.dismiss(id1);

    const remaining = service.toasts();
    expect(remaining.length).toBe(1);
    expect(remaining[0].id).toBe(id2);
  });

  it('showWithCorrelationId enqueues error with correlationId property', () => {
    const correlationId = 'corr-abc-123-xyz';
    service.showWithCorrelationId('Failed to sync invoice', correlationId);

    const current = service.toasts();
    expect(current.length).toBe(1);
    expect(current[0].message).toBe('Failed to sync invoice');
    expect(current[0].correlationId).toBe(correlationId);
    expect(current[0].severity).toBe('error');
  });

  it('clear removes all toasts immediately', () => {
    service.info('A', 0);
    service.warning('B', 0);
    expect(service.toasts().length).toBe(2);

    service.clear();
    expect(service.toasts().length).toBe(0);
  });
});
