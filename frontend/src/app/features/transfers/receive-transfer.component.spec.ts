import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { ReceiveTransferComponent } from './receive-transfer.component';
import type { PendingTransferItem } from './receive-transfer.component';
import { TransfersClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { BranchContextService } from '../../core/branch/branch-context.service';
import { ToastService } from '../../shared/services/toast.service';

describe('ReceiveTransferComponent', () => {
  let fixture: ComponentFixture<ReceiveTransferComponent>;
  let component: ReceiveTransferComponent;
  let transfersClient: TransfersClient;
  let branchContext: BranchContextService;
  let http: HttpClient;

  const mockFileResponse: FileResponse = {
    data: new Blob([JSON.stringify({ success: true })], { type: 'application/json' }),
    status: 200,
    fileName: 'receive.json',
    headers: {}
  };

  const sampleTransfers: PendingTransferItem[] = [
    {
      id: 'trans-001',
      fromBranchId: 'branch-a',
      toBranchId: 'branch-b', // Addressed to current branch (Branch B)
      productId: 'prod-iphone-15',
      productName: 'Apple iPhone 15',
      qty: 2,
      status: 'Pending',
      createdAt: new Date().toISOString()
    },
    {
      id: 'trans-002',
      fromBranchId: 'branch-b', // Current branch is merely the SOURCE!
      toBranchId: 'branch-a',   // Destined for Branch A
      productId: 'prod-samsung-s24',
      productName: 'Samsung Galaxy S24',
      qty: 1,
      status: 'Pending',
      createdAt: new Date().toISOString()
    },
    {
      id: 'trans-003',
      fromBranchId: 'branch-c',
      toBranchId: 'branch-a',   // Unrelated branch
      productId: 'prod-case-pro',
      productName: 'Silicone Case',
      qty: 5,
      status: 'Pending',
      createdAt: new Date().toISOString()
    }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReceiveTransferComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        TransfersClient,
        BranchContextService,
        ToastService
      ]
    }).compileComponents();

    http = TestBed.inject(HttpClient);
    transfersClient = TestBed.inject(TransfersClient);
    branchContext = TestBed.inject(BranchContextService);

    // Default: http.get returns empty list
    vi.spyOn(http, 'get').mockReturnValue(of([]));

    // Set current branch to Branch B
    branchContext.setCurrentBranchId('branch-b', 'Downtown Branch (Branch B)');

    vi.spyOn(transfersClient, 'receive').mockReturnValue(of(mockFileResponse));

    fixture = TestBed.createComponent(ReceiveTransferComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('pending-transfers list only shows transfers where ToBranchId matches current branch context, never transfers it is merely the source of', () => {
    component.setTransfers(sampleTransfers);
    fixture.detectChanges();

    const displayed = component.pendingTransfers();
    // Only trans-001 has toBranchId === 'branch-b'
    expect(displayed.length).toBe(1);
    expect(displayed[0].id).toBe('trans-001');
    expect(displayed[0].toBranchId).toBe('branch-b');

    // trans-002 (where branch-b is source) and trans-003 (unrelated) must be excluded
    expect(displayed.find(t => t.id === 'trans-002')).toBeUndefined();
    expect(displayed.find(t => t.id === 'trans-003')).toBeUndefined();

    const nativeEl = fixture.nativeElement as HTMLElement;
    const rows = nativeEl.querySelectorAll('.transfer-row');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('Apple iPhone 15');
  });

  it('receiving a transfer calls the endpoint and removes it from the pending list on success', async () => {
    const receiveSpy = vi.spyOn(transfersClient, 'receive').mockReturnValue(of(mockFileResponse));

    component.setTransfers(sampleTransfers);
    fixture.detectChanges();

    expect(component.pendingTransfers().length).toBe(1);

    const targetTransfer = component.pendingTransfers()[0];
    await component.receiveTransfer(targetTransfer);
    fixture.detectChanges();

    expect(receiveSpy).toHaveBeenCalledTimes(1);
    expect(receiveSpy).toHaveBeenCalledWith('trans-001');

    // Upon successful receipt, trans-001 is removed from pendingTransfers
    expect(component.pendingTransfers().length).toBe(0);

    const nativeEl = fixture.nativeElement as HTMLElement;
    expect(nativeEl.querySelector('.empty-state')).toBeTruthy();
  });
});
