import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl } from '@angular/forms';

@Component({
  selector: 'pz-form-field',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './pz-form-field.component.html',
  styleUrl: './pz-form-field.component.scss'
})
export class PzFormFieldComponent {
  @Input() label = '';
  @Input() control: AbstractControl | null = null;
  @Input() errorMessages: Record<string, string> = {};
  @Input() hint = '';
  @Input() required = false;
  @Input() forId = '';

  hasError(): boolean {
    const ctrl = this.control;
    return Boolean(ctrl && ctrl.invalid && (ctrl.touched || ctrl.dirty));
  }

  get errorMessage(): string {
    if (!this.hasError() || !this.control?.errors) {
      return '';
    }

    const errors = this.control.errors;
    for (const key of Object.keys(errors)) {
      if (this.errorMessages[key]) {
        return this.errorMessages[key];
      }

      const detail = errors[key];
      if (key === 'required') {
        return 'This field is required';
      }
      if (key === 'email') {
        return 'Please enter a valid email address';
      }
      if (key === 'minlength' && typeof detail === 'object' && detail !== null && 'requiredLength' in detail) {
        return `Minimum length is ${String(detail.requiredLength)} characters`;
      }
      if (key === 'maxlength' && typeof detail === 'object' && detail !== null && 'requiredLength' in detail) {
        return `Maximum length is ${String(detail.requiredLength)} characters`;
      }
      if (key === 'min' && typeof detail === 'object' && detail !== null && 'min' in detail) {
        return `Minimum value is ${String(detail.min)}`;
      }
      if (key === 'max' && typeof detail === 'object' && detail !== null && 'max' in detail) {
        return `Maximum value is ${String(detail.max)}`;
      }
      if (key === 'pattern') {
        return 'Invalid format';
      }
      if (typeof detail === 'string') {
        return detail;
      }
      if (typeof detail === 'object' && detail !== null && 'message' in detail && typeof detail.message === 'string') {
        return detail.message;
      }
    }

    return 'Invalid field value';
  }
}
