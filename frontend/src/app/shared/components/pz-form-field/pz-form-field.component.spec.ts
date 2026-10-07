import { describe, it, expect } from 'vitest';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { By } from '@angular/platform-browser';
import { PzFormFieldComponent } from './pz-form-field.component';

@Component({
  standalone: true,
  imports: [PzFormFieldComponent, ReactiveFormsModule],
  template: `
    <pz-form-field
      [label]="label"
      [control]="emailControl"
      [required]="required"
      [hint]="hint"
      [errorMessages]="errorMessages"
    >
      <input id="email-input" [formControl]="emailControl" type="email" />
    </pz-form-field>
  `
})
class TestHostComponent {
  label = 'Email Address';
  required = true;
  hint = 'We will send receipts here';
  errorMessages: Record<string, string> = {};
  emailControl = new FormControl('', [Validators.required, Validators.email]);
}

describe('PzFormFieldComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let host: TestHostComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TestHostComponent]
    });
    fixture = TestBed.createComponent(TestHostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders label, required indicator, hint and projected input', () => {
    const labelEl = fixture.debugElement.query(By.css('.label-text'));
    expect(labelEl.nativeElement.textContent.trim()).toBe('Email Address');

    const reqIndicator = fixture.debugElement.query(By.css('.required-indicator'));
    expect(reqIndicator).not.toBeNull();

    const inputEl = fixture.debugElement.query(By.css('#email-input'));
    expect(inputEl).not.toBeNull();

    const hintEl = fixture.debugElement.query(By.css('.pz-hint-message'));
    expect(hintEl.nativeElement.textContent.trim()).toBe('We will send receipts here');
  });

  it('does not display error message while control is untouched and pristine', () => {
    expect(host.emailControl.invalid).toBe(true);
    const errorEl = fixture.debugElement.query(By.css('.pz-error-message'));
    expect(errorEl).toBeNull();
  });

  it('shows an error message when the bound control is invalid and touched', () => {
    host.emailControl.markAsTouched();
    fixture.detectChanges();

    const errorEl = fixture.debugElement.query(By.css('.pz-error-message'));
    expect(errorEl).not.toBeNull();
    expect(errorEl.nativeElement.textContent.trim()).toBe('This field is required');
  });

  it('displays custom error message override when configured', () => {
    host.errorMessages = { required: 'Please provide your customer email' };
    host.emailControl.markAsTouched();
    fixture.detectChanges();

    const errorEl = fixture.debugElement.query(By.css('.pz-error-message'));
    expect(errorEl.nativeElement.textContent.trim()).toBe('Please provide your customer email');
  });

  it('displays validation error for invalid format when touched', () => {
    host.emailControl.setValue('invalid-email-string');
    host.emailControl.markAsTouched();
    fixture.detectChanges();

    const errorEl = fixture.debugElement.query(By.css('.pz-error-message'));
    expect(errorEl.nativeElement.textContent.trim()).toBe('Please enter a valid email address');
  });
});
