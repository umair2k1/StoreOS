import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PzDialogComponent } from '../../../shared/components/pz-dialog/pz-dialog.component';
import { PzDialogRef, PZ_DIALOG_DATA } from '../../../shared/components/pz-dialog/pz-dialog.service';
import { PzMoneyAliasPipe } from '../../../shared/pipes/money.pipe';
import { PermissionDirective } from '../../../core/directives/permission.directive';

export interface CreditLimitDialogData {
  customerName: string;
  currentBalance: number;
  cartTotal: number;
  creditLimit: number;
}

@Component({
  selector: 'app-credit-limit-warning-dialog',
  standalone: true,
  imports: [
    CommonModule,
    PzDialogComponent,
    PzMoneyAliasPipe,
    PermissionDirective
  ],
  templateUrl: './credit-limit-warning-dialog.component.html',
  styleUrl: './credit-limit-warning-dialog.component.scss'
})
export class CreditLimitWarningDialogComponent {
  readonly dialogRef = inject(PzDialogRef);
  readonly data = inject<CreditLimitDialogData>(PZ_DIALOG_DATA);

  onCancel(): void {
    this.dialogRef.close(false);
  }

  onOverride(): void {
    this.dialogRef.close(true);
  }
}
