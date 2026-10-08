import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ExpensesClient } from '../../core/api/generated-client';
import type {
  CreateExpenseCommand,
  FileResponse
} from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';
import { PzMoneyAliasPipe, PzDateAliasPipe } from '../../shared';

export interface ExpenseItem {
  id?: string;
  description: string;
  amount: number;
  category: string;
  expenseDate: Date | string;
  notes?: string;
}

export interface MonthlyTotalData {
  year?: number;
  month?: number;
  totalAmount?: number;
}

@Component({
  selector: 'app-expenses',
  standalone: true,
  imports: [CommonModule, FormsModule, PzMoneyAliasPipe, PzDateAliasPipe],
  templateUrl: './expenses.component.html',
  styleUrl: './expenses.component.scss'
})
export class ExpensesComponent implements OnInit {
  private readonly expensesClient = inject(ExpensesClient);
  private readonly toastService = inject(ToastService);

  // Suggested categories for the datalist convenience autocomplete
  readonly suggestedCategories: readonly string[] = [
    'Rent',
    'Utilities',
    'Salaries',
    'Miscellaneous'
  ];

  // Documented backend default: first-of-month through today (P-609)
  readonly fromDate = signal<string>(this.computeFirstOfMonth());
  readonly toDate = signal<string>(this.computeToday());
  readonly selectedCategory = signal<string>('');

  readonly expenses = signal<ExpenseItem[]>([]);
  readonly monthlyTotal = signal<number>(0);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Create Expense modal state
  readonly isCreateModalOpen = signal<boolean>(false);
  readonly isCreating = signal<boolean>(false);
  readonly newDescription = signal<string>('');
  readonly newAmount = signal<number>(0);
  readonly newCategory = signal<string>('');
  readonly newExpenseDate = signal<string>(this.computeToday());
  readonly newNotes = signal<string>('');

  ngOnInit(): void {
    this.loadAll();
  }

  computeFirstOfMonth(): string {
    const now = new Date();
    const y = now.getFullYear();
    const m = String(now.getMonth() + 1).padStart(2, '0');
    return `${y}-${m}-01`;
  }

  computeToday(): string {
    const now = new Date();
    const y = now.getFullYear();
    const m = String(now.getMonth() + 1).padStart(2, '0');
    const d = String(now.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }

  async loadAll(): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    try {
      await Promise.all([
        this.loadExpenses(),
        this.loadMonthlyTotal()
      ]);
    } catch {
      this.errorMessage.set('Failed to load expense data.');
    } finally {
      this.isLoading.set(false);
    }
  }

  async loadExpenses(): Promise<void> {
    const fromVal = this.fromDate() ? new Date(this.fromDate()) : undefined;
    const toVal = this.toDate() ? new Date(this.toDate()) : undefined;
    const catVal = this.selectedCategory().trim() || undefined;

    const resp = await firstValueFrom(
      this.expensesClient.list(fromVal, toVal, catVal, undefined, 1, 100)
    );
    const parsed = await this.parseFileResponse<unknown>(resp);
    let list: ExpenseItem[] = [];

    if (Array.isArray(parsed)) {
      list = parsed as ExpenseItem[];
    } else if (parsed && typeof parsed === 'object' && 'items' in parsed) {
      const nested = parsed.items;
      if (Array.isArray(nested)) {
        list = nested as ExpenseItem[];
      }
    }

    this.expenses.set(list);
  }

  async loadMonthlyTotal(): Promise<void> {
    const now = new Date();
    const resp = await firstValueFrom(
      this.expensesClient.getMonthlyTotal(now.getFullYear(), now.getMonth() + 1, undefined)
    );
    const parsed = await this.parseFileResponse<MonthlyTotalData>(resp);
    this.monthlyTotal.set(parsed?.totalAmount ?? 0);
  }

  async onFilterChange(): Promise<void> {
    this.isLoading.set(true);
    try {
      await this.loadExpenses();
    } finally {
      this.isLoading.set(false);
    }
  }

  openCreateModal(): void {
    this.newDescription.set('');
    this.newAmount.set(0);
    this.newCategory.set('');
    this.newExpenseDate.set(this.computeToday());
    this.newNotes.set('');
    this.isCreateModalOpen.set(true);
  }

  closeCreateModal(): void {
    this.isCreateModalOpen.set(false);
  }

  // HARD CONSTRAINT: The category input must accept ANY non-empty text,
  // with suggestions as a convenience datalist only - never restrict submission
  // to the suggested list.
  async submitCreateExpense(): Promise<void> {
    const desc = this.newDescription().trim();
    const cat = this.newCategory().trim();
    const amt = this.newAmount();

    if (!desc || !cat || amt <= 0 || this.isCreating()) {
      return;
    }

    this.isCreating.set(true);

    const command: CreateExpenseCommand = {
      description: desc,
      amount: amt,
      category: cat,
      expenseDate: new Date(this.newExpenseDate()),
      notes: this.newNotes().trim() || undefined
    };

    try {
      await firstValueFrom(this.expensesClient.create(command));
      this.toastService.success(`Expense "${desc}" created successfully.`);
      this.closeCreateModal();
      await this.loadAll();
    } catch {
      this.toastService.error('Failed to create expense.');
    } finally {
      this.isCreating.set(false);
    }
  }

  private async parseFileResponse<T>(resp: FileResponse): Promise<T | null> {
    if (!resp) return null;
    if (resp.data instanceof Blob) {
      const text = await resp.data.text();
      if (!text) return null;
      const parsedUnknown = JSON.parse(text) as unknown;
      return parsedUnknown as T;
    }
    if (typeof resp.data === 'string') {
      const parsedUnknown = JSON.parse(resp.data) as unknown;
      return parsedUnknown as T;
    }
    const rawData = resp.data as unknown;
    return (rawData as T) || null;
  }
}
