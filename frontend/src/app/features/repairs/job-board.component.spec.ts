import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import type { CdkDragDrop } from '@angular/cdk/drag-drop';
import { JobBoardComponent } from './job-board.component';
import type { RepairJobItem } from './job-board.component';
import { RepairJobsClient, SettingsClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

describe('JobBoardComponent', () => {
  let fixture: ComponentFixture<JobBoardComponent>;
  let component: JobBoardComponent;
  let repairJobsClient: RepairJobsClient;
  let settingsClient: SettingsClient;
  let toastService: ToastService;

  function createFileResponse(payload: unknown): FileResponse {
    const json = JSON.stringify(payload);
    return {
      data: new Blob([json], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    } as unknown as FileResponse;
  }

  const mockJobs: RepairJobItem[] = [
    {
      id: 'job-1111-1111-1111-1111',
      jobNumber: 'REP-2026-0001',
      customerName: 'Aarav Patel',
      customerPhone: '9876543210',
      deviceBrand: 'Apple',
      deviceModel: 'iPhone 13',
      problemDescription: 'Cracked front OLED screen',
      estimatedCost: 3500,
      finalTotal: 3500,
      status: 'Received'
    },
    {
      id: 'job-2222-2222-2222-2222',
      jobNumber: 'REP-2026-0002',
      customerName: 'Priya Sharma',
      customerPhone: '9123456780',
      deviceBrand: 'Samsung',
      deviceModel: 'Galaxy S22',
      problemDescription: 'Battery drains fast',
      estimatedCost: 2000,
      finalTotal: 2000,
      status: 'Delivered'
    }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [JobBoardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        RepairJobsClient,
        SettingsClient,
        {
          provide: ToastService,
          useValue: {
            success: vi.fn(),
            error: vi.fn(),
            info: vi.fn(),
            warning: vi.fn()
          }
        }
      ]
    }).compileComponents();

    repairJobsClient = TestBed.inject(RepairJobsClient);
    settingsClient = TestBed.inject(SettingsClient);
    toastService = TestBed.inject(ToastService);

    vi.spyOn(settingsClient, 'getRepairsEnabled').mockReturnValue(
      of(createFileResponse({ enabled: true }))
    );
    vi.spyOn(repairJobsClient, 'list').mockReturnValue(
      of(createFileResponse({ items: mockJobs, totalCount: 2, page: 1, pageSize: 50 }))
    );
    vi.spyOn(repairJobsClient, 'changeStatus').mockReturnValue(
      of(createFileResponse({}))
    );

    fixture = TestBed.createComponent(JobBoardComponent);
    component = fixture.componentInstance;
  });

  it('renders Kanban board with active status columns and separates closed jobs', async () => {
    fixture.detectChanges();
    await component.initComponent();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const columns = compiled.querySelectorAll('.kanban-column');
    expect(columns.length).toBe(6);

    const receivedCol = component.columnsMap()['Received'];
    expect(receivedCol.length).toBe(1);
    expect(receivedCol[0].jobNumber).toBe('REP-2026-0001');

    expect(component.closedJobs().length).toBe(1);
    expect(component.closedJobs()[0].status).toBe('Delivered');
  });

  it('legal drag-drop calls endpoint and card stays in new column', async () => {
    fixture.detectChanges();
    await component.initComponent();
    fixture.detectChanges();

    const receivedJobs = component.columnsMap()['Received'];
    const diagnosingJobs = component.columnsMap()['Diagnosing'];
    const jobToMove = receivedJobs[0];

    const fakeDropEvent = {
      previousContainer: { data: receivedJobs },
      container: { data: diagnosingJobs },
      previousIndex: 0,
      currentIndex: 0
    } as unknown as CdkDragDrop<RepairJobItem[]>;

    await component.onDrop(fakeDropEvent, 'Diagnosing');
    fixture.detectChanges();

    expect(repairJobsClient.changeStatus).toHaveBeenCalledWith(
      jobToMove.id,
      { status: 'Diagnosing' }
    );
    expect(jobToMove.status).toBe('Diagnosing');
    expect(diagnosingJobs).toContain(jobToMove);
    expect(receivedJobs).not.toContain(jobToMove);
    expect(toastService.success).toHaveBeenCalled();
  });

  it('dragging a card to an illegal target column triggers revert-and-error-toast behavior on 422 rejection', async () => {
    fixture.detectChanges();
    await component.initComponent();
    fixture.detectChanges();

    // Mock 422 DomainException on illegal transition
    vi.spyOn(repairJobsClient, 'changeStatus').mockReturnValue(
      throwError(() => new Error('Illegal transition: Cannot transition directly from Received to ReadyForPickup'))
    );

    const receivedJobs = component.columnsMap()['Received'];
    const readyJobs = component.columnsMap()['ReadyForPickup'];
    const jobToMove = receivedJobs[0];
    const originalStatus = jobToMove.status;

    const fakeDropEvent = {
      previousContainer: { data: receivedJobs },
      container: { data: readyJobs },
      previousIndex: 0,
      currentIndex: 0
    } as unknown as CdkDragDrop<RepairJobItem[]>;

    await component.onDrop(fakeDropEvent, 'ReadyForPickup');
    fixture.detectChanges();

    // Verify error toast
    expect(toastService.error).toHaveBeenCalled();

    // Verify REVERT HARD CONSTRAINT: card is returned to original list and status restored
    expect(jobToMove.status).toBe(originalStatus);
    expect(receivedJobs).toContain(jobToMove);
    expect(readyJobs).not.toContain(jobToMove);
  });

  it('shows disabled message instead of Kanban board when repairs feature is disabled', async () => {
    vi.spyOn(settingsClient, 'getRepairsEnabled').mockReturnValue(
      of(createFileResponse({ enabled: false }))
    );

    fixture.detectChanges();
    await component.initComponent();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(component.isRepairsEnabled()).toBe(false);
    expect(compiled.querySelector('.module-disabled-card')).toBeTruthy();
    expect(compiled.querySelector('.disabled-title')?.textContent).toContain('Repairs Module Is Not Enabled');
    expect(compiled.querySelector('.kanban-wrapper')).toBeFalsy();
  });
});
