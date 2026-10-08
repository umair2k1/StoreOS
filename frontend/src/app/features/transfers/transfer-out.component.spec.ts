import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { TransferOutComponent } from './transfer-out.component';
import { TransfersClient } from '../../core/api/generated-client';
import type { FileResponse, TransferOutCommand } from '../../core/api/generated-client';
import { BranchContextService } from '../../core/branch/branch-context.service';
import { ToastService } from '../../shared/services/toast.service';

describe('TransferOutComponent', () => {
  let fixture: ComponentFixture<TransferOutComponent>;
  let component: TransferOutComponent;
  let transfersClient: TransfersClient;
  let branchContext: BranchContextService;

  const mockFileResponse: FileResponse = {
    data: new Blob([JSON.stringify({ id: 'trans-001' })], { type: 'application/json' }),
    status: 201,
    fileName: 'transfer.json',
    headers: {}
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TransferOutComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        TransfersClient,
        BranchContextService,
        ToastService
      ]
    }).compileComponents();

    transfersClient = TestBed.inject(TransfersClient);
    branchContext = TestBed.inject(BranchContextService);

    // Current branch is Branch A; available branches are A, B, C
    branchContext.setBranches([
      { id: 'branch-a', name: 'Main Branch (Branch A)' },
      { id: 'branch-b', name: 'Downtown Branch (Branch B)' },
      { id: 'branch-c', name: 'Uptown Branch (Branch C)' }
    ]);
    branchContext.setCurrentBranchId('branch-a', 'Main Branch (Branch A)');

    vi.spyOn(transfersClient, 'transferOut').mockReturnValue(of(mockFileResponse));

    fixture = TestBed.createComponent(TransferOutComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('destination branches exclude the current branch context', () => {
    const dests = component.destinationBranches();
    expect(dests.length).toBe(2);
    expect(dests.find(b => b.id === 'branch-a')).toBeUndefined();
    expect(dests.find(b => b.id === 'branch-b')).toBeDefined();
    expect(dests.find(b => b.id === 'branch-c')).toBeDefined();
  });

  it('transfer-out submits correctly with the required command payload shape', async () => {
    const transferSpy = vi.spyOn(transfersClient, 'transferOut').mockReturnValue(of(mockFileResponse));

    component.toBranchId.set('branch-b');
    component.productId.set('prod-pixel-9');
    component.qty.set(2);
    component.imeiList.set(['351111111111111', '352222222222222']);
    fixture.detectChanges();

    expect(component.isValid()).toBe(true);

    await component.submitTransfer();
    fixture.detectChanges();

    expect(transferSpy).toHaveBeenCalledTimes(1);
    const sentCommand = transferSpy.mock.calls[0][0] as TransferOutCommand;
    expect(sentCommand.toBranchId).toBe('branch-b');
    expect(sentCommand.productId).toBe('prod-pixel-9');
    expect(sentCommand.qty).toBe(2);
    expect(sentCommand.imeiStrings).toEqual(['351111111111111', '352222222222222']);

    expect(component.successMessage()).toContain('Successfully initiated transfer');
  });
});
