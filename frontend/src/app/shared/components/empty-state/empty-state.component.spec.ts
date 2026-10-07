import { describe, it, expect } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { EmptyStateComponent } from './empty-state.component';

describe('EmptyStateComponent', () => {
  let fixture: ComponentFixture<EmptyStateComponent>;
  let component: EmptyStateComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EmptyStateComponent]
    });
    fixture = TestBed.createComponent(EmptyStateComponent);
    component = fixture.componentInstance;
  });

  it('renders default message and icon', () => {
    fixture.detectChanges();
    const msgEl = fixture.debugElement.query(By.css('.empty-state-message'));
    expect(msgEl.nativeElement.textContent.trim()).toBe('No data available');

    const iconEl = fixture.debugElement.query(By.css('.empty-state-icon'));
    expect(iconEl.nativeElement.textContent.trim()).toBe('📭');
  });

  it('renders custom inputs accurately', () => {
    component.title = 'No Invoices Yet';
    component.message = 'Create your first invoice to view billing records';
    component.icon = '📄';
    fixture.detectChanges();

    const titleEl = fixture.debugElement.query(By.css('.empty-state-title'));
    expect(titleEl.nativeElement.textContent.trim()).toBe('No Invoices Yet');

    const msgEl = fixture.debugElement.query(By.css('.empty-state-message'));
    expect(msgEl.nativeElement.textContent.trim()).toBe('Create your first invoice to view billing records');

    const iconEl = fixture.debugElement.query(By.css('.empty-state-icon'));
    expect(iconEl.nativeElement.textContent.trim()).toBe('📄');
  });
});
