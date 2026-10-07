import {
  Component,
  Input,
  Output,
  EventEmitter,
  inject,
  ChangeDetectionStrategy
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { PzDialogRef } from './pz-dialog.service';

@Component({
  selector: 'pz-dialog',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './pz-dialog.component.html',
  styleUrl: './pz-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PzDialogComponent {
  @Input() title = '';
  @Input() showCloseButton = true;
  @Output() closed = new EventEmitter<void>();

  private readonly dialogRef = inject(PzDialogRef, { optional: true });

  onClose(): void {
    this.closed.emit();
    this.dialogRef?.close();
  }
}
