import { Injectable, signal } from '@angular/core';

export type ToastSeverity = 'info' | 'success' | 'warning' | 'error';

export interface ToastMessage {
  id: string;
  message: string;
  severity: ToastSeverity;
  duration: number;
  correlationId?: string;
  timestamp: number;
}

@Injectable({
  providedIn: 'root'
})
export class ToastService {
  private readonly _toasts = signal<ToastMessage[]>([]);
  readonly toasts = this._toasts.asReadonly();
  private nextId = 1;

  show(
    message: string,
    severity: ToastSeverity = 'info',
    duration = 4000
  ): string {
    const id = `toast-${this.nextId++}`;
    const toast: ToastMessage = {
      id,
      message,
      severity,
      duration,
      timestamp: Date.now()
    };

    this._toasts.update((current) => [...current, toast]);

    if (duration > 0) {
      setTimeout(() => {
        this.dismiss(id);
      }, duration);
    }

    return id;
  }

  info(message: string, duration = 4000): string {
    return this.show(message, 'info', duration);
  }

  success(message: string, duration = 4000): string {
    return this.show(message, 'success', duration);
  }

  warning(message: string, duration = 4000): string {
    return this.show(message, 'warning', duration);
  }

  error(message: string, duration = 6000): string {
    return this.show(message, 'error', duration);
  }

  showWithCorrelationId(
    message: string,
    correlationId: string,
    severity: ToastSeverity = 'error',
    duration = 8000
  ): string {
    const id = `toast-${this.nextId++}`;
    const toast: ToastMessage = {
      id,
      message,
      severity,
      duration,
      correlationId,
      timestamp: Date.now()
    };

    this._toasts.update((current) => [...current, toast]);

    if (duration > 0) {
      setTimeout(() => {
        this.dismiss(id);
      }, duration);
    }

    return id;
  }

  dismiss(id: string): void {
    this._toasts.update((current) => current.filter((t) => t.id !== id));
  }

  clear(): void {
    this._toasts.set([]);
  }
}
