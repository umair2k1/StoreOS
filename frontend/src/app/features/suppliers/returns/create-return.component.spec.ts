import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { CreateReturnComponent } from './create-return.component';
import { SupplierReturnsClient } from '../../../core/api/generated-client';
import type {
  CreateSupplierReturnCommand,
  FileResponse
} from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';

describe('CreateReturnComponent', () => {
  let fixture: ComponentFixture<CreateReturnComponent>;
  let component: CreateReturnComponent;
  let returnsClient: SupplierReturnsClient;
  let router: Router;

  const mockFileResponse: FileResponse = {
    data: new Blob([JSON.stringify({ returnId: 'ret-101', returnNumber: 'RET-2026-001' })], { type: 'application/json' }),
    status: 200,
    fileName: 'return.json',
    headers: {}
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CreateReturnComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', component: CreateReturnComponent }]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => (key === 'id' ? 'sup-101' : null)
              }
            }
          }
        },
        SupplierReturnsClient,
        ToastService
      ]
    }).compileComponents();

    returnsClient = TestBed.inject(SupplierReturnsClient);
    router = TestBed.inject(Router);

    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    vi.spyOn(returnsClient, 'create').mockReturnValue(of(mockFileResponse));

    fixture = TestBed.createComponent(CreateReturnComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('submits correctly-shaped line items including IMEI arrays', async () => {
    const createSpy = vi.spyOn(returnsClient, 'create').mockReturnValue(of(mockFileResponse));

    component.returnDate.set('2026-03-10');
    component.settlementType.set('CreditNote');
    component.reason.set('Damaged during transit');
    component.notes.set('Vendor authorized return via RMA #4512');

    // Setup line items with IMEI array
    component.rows.set([
      {
        id: 'row-1',
        productId: 'prod-px9',
        itemName: 'Google Pixel 9 Pro',
        qty: 2,
        purchasePrice: 65000,
        returnRate: 65000,
        imeiInputs: ['359999999999991', '359999999999992']
      }
    ]);
    fixture.detectChanges();

    expect(component.isValid()).toBe(true);

    await component.submitReturn();
    fixture.detectChanges();

    expect(createSpy).toHaveBeenCalledTimes(1);
    const sentCommand = createSpy.mock.calls[0][0] as CreateSupplierReturnCommand;

    expect(sentCommand.supplierId).toBe('sup-101');
    expect(sentCommand.settlementType).toBe('CreditNote');
    expect(sentCommand.reason).toBe('Damaged during transit');
    expect(sentCommand.notes).toBe('Vendor authorized return via RMA #4512');

    expect(sentCommand.lines?.length).toBe(1);
    const line = sentCommand.lines![0];
    expect(line.itemName).toBe('Google Pixel 9 Pro');
    expect(line.qty).toBe(2);
    expect(line.purchasePrice).toBe(65000);
    expect(line.returnRate).toBe(65000);
    expect(line.imeiNumbers).toEqual(['359999999999991', '359999999999992']);
  });
});
