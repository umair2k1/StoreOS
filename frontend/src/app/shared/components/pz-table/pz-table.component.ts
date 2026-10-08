import {
  Component,
  Input,
  Output,
  EventEmitter,
  ChangeDetectionStrategy
} from '@angular/core';
import { CommonModule } from '@angular/common';

export interface PzTableColumn<T = object> {
  key: string;
  label: string;
  sortable?: boolean;
  align?: 'left' | 'center' | 'right';
  width?: string;
  type?: 'text' | 'badge' | 'stock-status';
  formatter?: (value: unknown, row: T) => string;
}

export interface PzTableSortEvent {
  column: string;
  direction: 'asc' | 'desc';
}

@Component({
  selector: 'pz-table',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './pz-table.component.html',
  styleUrl: './pz-table.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PzTableComponent<T extends object = Record<string, unknown>> {
  @Input() columns: PzTableColumn<T>[] = [];
  @Input() rows: T[] = [];
  @Input() sortColumn?: string;
  @Input() sortDirection: 'asc' | 'desc' | null = null;
  @Input() emptyMessage = 'No records found';
  @Input() trackByKey?: string;
  @Input() loading = false;
  @Input() selectable = false;
  @Input() selectedRows = new Set<unknown>();

  @Output() sort = new EventEmitter<PzTableSortEvent>();
  @Output() rowClick = new EventEmitter<T>();
  @Output() selectAll = new EventEmitter<boolean>();
  @Output() selectRow = new EventEmitter<{ row: T; selected: boolean }>();
  onHeaderClick(col: PzTableColumn<T>): void {
    if (!col.sortable) {
      return;
    }

    let nextDirection: 'asc' | 'desc' = 'asc';
    if (this.sortColumn === col.key) {
      nextDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    }

    this.sortColumn = col.key;
    this.sortDirection = nextDirection;
    this.sort.emit({ column: col.key, direction: nextDirection });
  }

  onRowClick(row: T): void {
    this.rowClick.emit(row);
  }

  getCellValue(row: T, col: PzTableColumn<T>): string {
    const record = row as Record<string, unknown>;
    const raw = record[col.key];
    if (col.formatter) {
      return col.formatter(raw, row);
    }
    return raw !== null && raw !== undefined ? String(raw) : '';
  }

  getRowKey(row: T, index: number): unknown {
    const record = row as Record<string, unknown>;
    if (this.trackByKey && record[this.trackByKey] !== undefined) {
      return record[this.trackByKey];
    }
    if ('id' in record && record['id'] !== undefined) {
      return record['id'];
    }
    return index;
  }
  isRowSelected(row: T, index: number): boolean {
    const key = this.getRowKey(row, index);
    return this.selectedRows.has(key);
  }

  areAllSelected(): boolean {
    if (!this.rows.length) return false;
    return this.rows.every((row, idx) => this.selectedRows.has(this.getRowKey(row, idx)));
  }

  onSelectAllToggle(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selectAll.emit(checked);
  }

  onRowSelectToggle(row: T, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selectRow.emit({ row, selected: checked });
  }

  isStockStatusCol(col: PzTableColumn<T>): boolean {
    return col.type === 'stock-status' || col.key.toLowerCase() === 'stockstatus';
  }

  getStockStatusClass(row: T, col: PzTableColumn<T>): string {
    const record = row as Record<string, unknown>;
    const val = String(record[col.key] || '').toLowerCase().trim();
    if (val.includes('in stock') || val.includes('instock')) return 'in-stock';
    if (val.includes('low stock') || val.includes('lowstock')) return 'low-stock';
    if (val.includes('out of stock') || val.includes('outofstock')) return 'out-of-stock';
    return '';
  }
}
