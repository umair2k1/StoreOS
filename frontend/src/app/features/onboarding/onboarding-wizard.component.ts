import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { forkJoin } from 'rxjs';
import { BranchesClient, SettingsClient } from '../../core/api/generated-client';
import { BrandingSignalStore } from '../../core/branding/branding-signal-store';

@Component({
  selector: 'app-onboarding-wizard',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="wizard-container">
      <h1>Welcome to StoreOS</h1>
      <p class="step-subtitle">Step {{ currentStep() }} of 2: {{ currentStep() === 1 ? 'Shop Profile' : 'Initial Branch' }}</p>

      <form [formGroup]="form" (ngSubmit)="onSubmit()">
        @if (currentStep() === 1) {
          <div class="form-group">
            <label for="shopName">Shop / Business Name *</label>
            <input id="shopName" type="text" formControlName="shopName" placeholder="e.g. Acme Electronics" />
          </div>

          <div class="form-group">
            <label for="gstin">GSTIN (Optional)</label>
            <input id="gstin" type="text" formControlName="gstin" placeholder="e.g. 29ABCDE1234F1Z5" />
          </div>

          <div class="form-group">
            <label for="phone">Phone Number</label>
            <input id="phone" type="text" formControlName="phone" placeholder="e.g. +91 9876543210" />
          </div>

          <div class="form-group">
            <label for="address">Address</label>
            <input id="address" type="text" formControlName="address" placeholder="e.g. 101 MG Road, Bangalore" />
          </div>

          <div class="form-group">
            <label for="receiptFooter">Receipt Footer Message</label>
            <input id="receiptFooter" type="text" formControlName="receiptFooter" placeholder="e.g. Thank you for shopping with us!" />
          </div>

          <div class="actions">
            <button type="button" class="btn-primary" (click)="goToNextStep()" [disabled]="form.get('shopName')?.invalid">
              Next: Branch Setup
            </button>
          </div>
        } @else {
          <div class="form-group">
            <label for="branchName">Branch Name *</label>
            <input id="branchName" type="text" formControlName="branchName" placeholder="e.g. Main Branch" />
          </div>

          <div class="form-group">
            <label for="branchCode">Branch Code *</label>
            <input id="branchCode" type="text" formControlName="branchCode" placeholder="e.g. MAIN" />
          </div>

          <div class="actions">
            <button type="button" class="btn-secondary" (click)="goToPreviousStep()">
              Back
            </button>
            <button type="submit" class="btn-primary" [disabled]="form.invalid || isSubmitting()">
              {{ isSubmitting() ? 'Completing Setup...' : 'Complete Onboarding' }}
            </button>
          </div>
        }
      </form>
    </div>
  `,
  styleUrl: './onboarding-wizard.component.scss'
})
export class OnboardingWizardComponent {
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly branchesClient = inject(BranchesClient);
  private readonly settingsClient = inject(SettingsClient);
  private readonly brandingStore = inject(BrandingSignalStore);

  readonly currentStep = signal<number>(1);
  readonly isSubmitting = signal<boolean>(false);

  readonly form = this.fb.nonNullable.group({
    shopName: ['', [Validators.required, Validators.minLength(2)]],
    gstin: [''],
    phone: [''],
    address: [''],
    receiptFooter: [''],
    branchName: ['', [Validators.required, Validators.minLength(2)]],
    branchCode: ['', [Validators.required, Validators.minLength(2)]]
  });

  goToNextStep(): void {
    if (this.form.get('shopName')?.valid) {
      this.currentStep.set(2);
    }
  }

  goToPreviousStep(): void {
    this.currentStep.set(1);
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      return;
    }

    this.isSubmitting.set(true);
    const val = this.form.getRawValue();

    const branchCmd = {
      code: val.branchCode,
      name: val.branchName,
      address: val.address || undefined,
      phone: val.phone || undefined,
      gstin: val.gstin || undefined
    };

    const shopProfile = {
      name: val.shopName,
      address: val.address,
      phone: val.phone,
      gstin: val.gstin,
      receiptFooter: val.receiptFooter
    };

    forkJoin({
      branch: this.branchesClient.create(branchCmd),
      settings: this.settingsClient.patchSetting('shop_profile', shopProfile)
    }).subscribe({
      next: () => {
        this.brandingStore.setBranding({
          tenantName: val.shopName,
          activeBranchName: val.branchName
        });
        this.isSubmitting.set(false);
        this.router.navigateByUrl('/pos');
      },
      error: () => {
        this.isSubmitting.set(false);
      }
    });
  }
}
