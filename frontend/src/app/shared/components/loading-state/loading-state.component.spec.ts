import { describe, it, expect } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { LoadingStateComponent } from './loading-state.component';

describe('LoadingStateComponent', () => {
  let fixture: ComponentFixture<LoadingStateComponent>;
  let component: LoadingStateComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LoadingStateComponent]
    });
    fixture = TestBed.createComponent(LoadingStateComponent);
    component = fixture.componentInstance;
  });

  it('renders default message and spinner', () => {
    fixture.detectChanges();
    const msgEl = fixture.debugElement.query(By.css('.loading-state-message'));
    expect(msgEl.nativeElement.textContent.trim()).toBe('Loading...');

    const spinnerEl = fixture.debugElement.query(By.css('.loading-spinner'));
    expect(spinnerEl).not.toBeNull();
  });

  it('renders custom message and custom icon', () => {
    component.message = 'Processing payment transaction...';
    component.icon = '💳';
    fixture.detectChanges();

    const msgEl = fixture.debugElement.query(By.css('.loading-state-message'));
    expect(msgEl.nativeElement.textContent.trim()).toBe('Processing payment transaction...');

    const iconEl = fixture.debugElement.query(By.css('.loading-custom-icon'));
    expect(iconEl.nativeElement.textContent.trim()).toBe('💳');

    const spinnerEl = fixture.debugElement.query(By.css('.loading-spinner'));
    expect(spinnerEl).toBeNull();
  });
});
