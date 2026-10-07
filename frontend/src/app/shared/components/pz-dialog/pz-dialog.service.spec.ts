import { describe, it, expect, beforeEach } from 'vitest';
import { Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { OverlayModule } from '@angular/cdk/overlay';
import {
  PzDialogService,
  PzDialogRef,
  PZ_DIALOG_DATA
} from './pz-dialog.service';

interface TestDialogData {
  message: string;
}

@Component({
  standalone: true,
  template: `
    <div id="test-dialog-content">
      <p>{{ data.message }}</p>
      <button id="close-btn" (click)="closeDialog()">Close</button>
    </div>
  `
})
class TestModalComponent {
  readonly data = inject<TestDialogData>(PZ_DIALOG_DATA);
  readonly dialogRef = inject<PzDialogRef<TestModalComponent, string>>(PzDialogRef);

  closeDialog(): void {
    this.dialogRef.close('success-result');
  }
}

describe('PzDialogService', () => {
  let service: PzDialogService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OverlayModule],
      providers: [PzDialogService]
    });

    service = TestBed.inject(PzDialogService);
  });

  it('starts with no active dialogs', () => {
    expect(service.activeDialogs().length).toBe(0);
    expect(service.hasOpenDialogs()).toBe(false);
  });

  it('updates state reactively when a dialog is opened', () => {
    const dialogRef = service.open(TestModalComponent, {
      data: { message: 'Are you sure you want to void this sale?' }
    });

    expect(service.activeDialogs().length).toBe(1);
    expect(service.hasOpenDialogs()).toBe(true);
    expect(dialogRef.isOpen()).toBe(true);
    expect(dialogRef.componentInstance).toBeInstanceOf(TestModalComponent);
    expect(dialogRef.componentInstance?.data.message).toBe('Are you sure you want to void this sale?');

    dialogRef.close();
  });

  it('updates state and emits result when dialog is closed', async () => {
    const dialogRef = service.open<TestModalComponent, TestDialogData, string>(
      TestModalComponent,
      { data: { message: 'Confirmation' } }
    );

    let emittedResult: string | undefined;
    dialogRef.afterClosed().subscribe((res) => {
      emittedResult = res;
    });

    dialogRef.close('confirmed-value');

    expect(dialogRef.isOpen()).toBe(false);
    expect(service.activeDialogs().length).toBe(0);
    expect(service.hasOpenDialogs()).toBe(false);
    expect(emittedResult).toBe('confirmed-value');
  });

  it('allows component to close itself via injected PzDialogRef', () => {
    const dialogRef = service.open<TestModalComponent, TestDialogData, string>(
      TestModalComponent,
      { data: { message: 'Self closing' } }
    );

    let emittedResult: string | undefined;
    dialogRef.afterClosed().subscribe((res) => {
      emittedResult = res;
    });

    dialogRef.componentInstance?.closeDialog();

    expect(dialogRef.isOpen()).toBe(false);
    expect(service.activeDialogs().length).toBe(0);
    expect(emittedResult).toBe('success-result');
  });

  it('closeAll closes all active dialogs', () => {
    const ref1 = service.open(TestModalComponent, { data: { message: 'First' } });
    const ref2 = service.open(TestModalComponent, { data: { message: 'Second' } });

    expect(service.activeDialogs().length).toBe(2);

    service.closeAll();

    expect(ref1.isOpen()).toBe(false);
    expect(ref2.isOpen()).toBe(false);
    expect(service.activeDialogs().length).toBe(0);
    expect(service.hasOpenDialogs()).toBe(false);
  });
});
