import { describe, it, expect } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ErrorStateComponent } from './error-state.component';

describe('ErrorStateComponent', () => {
  let fixture: ComponentFixture<ErrorStateComponent>;
  let component: ErrorStateComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ErrorStateComponent]
    });
    fixture = TestBed.createComponent(ErrorStateComponent);
    component = fixture.componentInstance;
  });

  it('renders default message, title, and icon without retry button', () => {
    fixture.detectChanges();

    const titleEl = fixture.debugElement.query(By.css('.error-state-title'));
    expect(titleEl.nativeElement.textContent.trim()).toBe('Error');

    const msgEl = fixture.debugElement.query(By.css('.error-state-message'));
    expect(msgEl.nativeElement.textContent.trim()).toBe('An error occurred while loading data');

    const iconEl = fixture.debugElement.query(By.css('.error-state-icon'));
    expect(iconEl.nativeElement.textContent.trim()).toBe('⚠️');

    const retryBtn = fixture.debugElement.query(By.css('.error-retry-btn'));
    expect(retryBtn).toBeNull();
  });

  it('renders custom inputs and shows retry button when retryable is true', () => {
    component.title = 'Network Connection Lost';
    component.message = 'Unable to reach StoreOS servers. Check your connection.';
    component.icon = '📡';
    component.retryable = true;
    component.retryLabel = 'Reconnect Now';
    fixture.detectChanges();

    const titleEl = fixture.debugElement.query(By.css('.error-state-title'));
    expect(titleEl.nativeElement.textContent.trim()).toBe('Network Connection Lost');

    const msgEl = fixture.debugElement.query(By.css('.error-state-message'));
    expect(msgEl.nativeElement.textContent.trim()).toBe('Unable to reach StoreOS servers. Check your connection.');

    const iconEl = fixture.debugElement.query(By.css('.error-state-icon'));
    expect(iconEl.nativeElement.textContent.trim()).toBe('📡');

    const retryBtn = fixture.debugElement.query(By.css('.error-retry-btn'));
    expect(retryBtn).not.toBeNull();
    expect(retryBtn.nativeElement.textContent.trim()).toBe('Reconnect Now');
  });

  it('emits retry output event when retry button is clicked', () => {
    component.retryable = true;
    fixture.detectChanges();

    let emitted = false;
    component.retry.subscribe(() => {
      emitted = true;
    });

    const retryBtn = fixture.debugElement.query(By.css('.error-retry-btn'));
    retryBtn.nativeElement.click();
    fixture.detectChanges();

    expect(emitted).toBe(true);
  });
});
