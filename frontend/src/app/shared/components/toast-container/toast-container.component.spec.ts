import { describe, it, expect, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ToastContainerComponent } from './toast-container.component';
import { ToastService } from '../../services/toast.service';

describe('ToastContainerComponent', () => {
  let fixture: ComponentFixture<ToastContainerComponent>;
  let toastService: ToastService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ToastContainerComponent],
      providers: [ToastService]
    });

    toastService = TestBed.inject(ToastService);
    fixture = TestBed.createComponent(ToastContainerComponent);
    fixture.detectChanges();
  });

  it('renders no toast items initially', () => {
    const toastItems = fixture.debugElement.queryAll(By.css('.pz-toast-item'));
    expect(toastItems.length).toBe(0);
  });

  it('renders toasts when enqueued in ToastService', () => {
    toastService.success('Customer profile saved');
    fixture.detectChanges();

    const toastItems = fixture.debugElement.queryAll(By.css('.pz-toast-item'));
    expect(toastItems.length).toBe(1);

    const msgEl = fixture.debugElement.query(By.css('.toast-message'));
    expect(msgEl.nativeElement.textContent.trim()).toBe('Customer profile saved');

    const itemEl = toastItems[0].nativeElement as HTMLElement;
    expect(itemEl.classList.contains('pz-toast-success')).toBe(true);
  });

  it('renders correlation ID when present on the toast', () => {
    toastService.showWithCorrelationId('Database timeout', 'corr-456-789');
    fixture.detectChanges();

    const corrEl = fixture.debugElement.query(By.css('.toast-correlation-id code'));
    expect(corrEl).not.toBeNull();
    expect(corrEl.nativeElement.textContent.trim()).toBe('corr-456-789');
  });

  it('dismisses toast when dismiss button is clicked', () => {
    toastService.info('Temporary alert', 0);
    fixture.detectChanges();

    expect(fixture.debugElement.queryAll(By.css('.pz-toast-item')).length).toBe(1);

    const dismissBtn = fixture.debugElement.query(By.css('.toast-dismiss-btn'));
    dismissBtn.nativeElement.click();
    fixture.detectChanges();

    expect(fixture.debugElement.queryAll(By.css('.pz-toast-item')).length).toBe(0);
  });
});
