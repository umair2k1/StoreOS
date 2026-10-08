import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { ExpensesComponent } from './expenses.component';
import { ExpensesClient } from '../../core/api/generated-client';
import type {
  CreateExpenseCommand,
  FileResponse
} from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

describe('ExpensesComponent', () => {
  let fixture: ComponentFixture<ExpensesComponent>;
  let component: ExpensesComponent;
  let expensesClient: ExpensesClient;

  function createFileResponse(payload: unknown): FileResponse {
    return {
      data: new Blob([JSON.stringify(payload)], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    };
  }

  const mockExpenses = [
    {
      id: 'exp-1',
      description: 'Office Electricity Bill',
      amount: 4500,
      category: 'Utilities',
      expenseDate: new Date().toISOString(),
      notes: 'October cycle'
    }
  ];

  const mockMonthlyTotal = {
    year: new Date().getFullYear(),
    month: new Date().getMonth() + 1,
    totalAmount: 42500
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExpensesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ExpensesClient,
        ToastService
      ]
    }).compileComponents();

    expensesClient = TestBed.inject(ExpensesClient);

    vi.spyOn(expensesClient, 'list').mockReturnValue(of(createFileResponse(mockExpenses)));
    vi.spyOn(expensesClient, 'getMonthlyTotal').mockReturnValue(of(createFileResponse(mockMonthlyTotal)));
    vi.spyOn(expensesClient, 'create').mockReturnValue(of(createFileResponse({ id: 'exp-new' })));

    fixture = TestBed.createComponent(ExpensesComponent);
    component = fixture.componentInstance;
  });

  it('default date range matches the backend documented default (first-of-month through today)', () => {
    const now = new Date();
    const expectedYear = now.getFullYear();
    const expectedMonth = String(now.getMonth() + 1).padStart(2, '0');
    const expectedDay = String(now.getDate()).padStart(2, '0');

    const expectedFrom = `${expectedYear}-${expectedMonth}-01`;
    const expectedTo = `${expectedYear}-${expectedMonth}-${expectedDay}`;

    expect(component.fromDate()).toBe(expectedFrom);
    expect(component.toDate()).toBe(expectedTo);
  });

  it('entering a novel category not in the suggestion list still submits successfully', async () => {
    fixture.detectChanges();
    await component.loadAll();
    fixture.detectChanges();

    const createSpy = vi.spyOn(expensesClient, 'create').mockReturnValue(of(createFileResponse({ id: 'exp-novel' })));

    component.openCreateModal();
    fixture.detectChanges();

    // The suggested categories list only contains Rent, Utilities, Salaries, Miscellaneous
    const novelCategory = 'Specialty Espresso Machine Maintenance';
    expect(component.suggestedCategories.includes(novelCategory)).toBe(false);

    component.newDescription.set('Espresso repair');
    component.newAmount.set(1200);
    component.newCategory.set(novelCategory);
    fixture.detectChanges();

    await component.submitCreateExpense();
    fixture.detectChanges();

    expect(createSpy).toHaveBeenCalledTimes(1);
    const sentCommand = createSpy.mock.calls[0][0] as CreateExpenseCommand;
    expect(sentCommand.category).toBe(novelCategory);
    expect(sentCommand.amount).toBe(1200);
    expect(sentCommand.description).toBe('Espresso repair');

    expect(component.isCreateModalOpen()).toBe(false);
  });

  it('monthly total display matches the fetched value', async () => {
    fixture.detectChanges();
    await component.loadAll();
    fixture.detectChanges();

    expect(component.monthlyTotal()).toBe(42500);

    const nativeEl = fixture.nativeElement as HTMLElement;
    const totalEl = nativeEl.querySelector('.monthly-total-value');
    expect(totalEl).toBeTruthy();
    expect(totalEl?.textContent).toContain('42,500');
  });
});
