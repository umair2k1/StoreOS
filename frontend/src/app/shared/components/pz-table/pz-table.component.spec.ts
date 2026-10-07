import { describe, it, expect } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { PzTableComponent, PzTableColumn, PzTableSortEvent } from './pz-table.component';

interface TestItem {
  id: string;
  name: string;
  price: number;
}

describe('PzTableComponent', () => {
  let fixture: ComponentFixture<PzTableComponent<TestItem>>;
  let component: PzTableComponent<TestItem>;

  const columns: PzTableColumn<TestItem>[] = [
    { key: 'id', label: 'ID', sortable: false },
    { key: 'name', label: 'Item Name', sortable: true },
    {
      key: 'price',
      label: 'Price',
      sortable: true,
      formatter: (v) => `₹${String(v)}`
    }
  ];

  const rows: TestItem[] = [
    { id: '1', name: 'MacBook Pro', price: 199990 },
    { id: '2', name: 'iPhone 16 Pro', price: 119900 }
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PzTableComponent]
    });
    fixture = TestBed.createComponent(PzTableComponent<TestItem>);
    component = fixture.componentInstance;
  });

  it('renders columns and row cells accurately', () => {
    component.columns = columns;
    component.rows = rows;
    fixture.detectChanges();

    const headerCells = fixture.debugElement.queryAll(By.css('thead th'));
    expect(headerCells.length).toBe(3);
    expect(headerCells[0].nativeElement.textContent.trim()).toContain('ID');
    expect(headerCells[1].nativeElement.textContent.trim()).toContain('Item Name');
    expect(headerCells[2].nativeElement.textContent.trim()).toContain('Price');

    const bodyRows = fixture.debugElement.queryAll(By.css('tbody tr'));
    expect(bodyRows.length).toBe(2);

    const firstRowCells = bodyRows[0].queryAll(By.css('td'));
    expect(firstRowCells[0].nativeElement.textContent.trim()).toBe('1');
    expect(firstRowCells[1].nativeElement.textContent.trim()).toBe('MacBook Pro');
    expect(firstRowCells[2].nativeElement.textContent.trim()).toBe('₹199990');
  });

  it('renders emptyMessage when there are no rows', () => {
    component.columns = columns;
    component.rows = [];
    component.emptyMessage = 'No inventory items found';
    fixture.detectChanges();

    const emptyCell = fixture.debugElement.query(By.css('tbody td.empty-cell'));
    expect(emptyCell).not.toBeNull();
    expect(emptyCell.nativeElement.textContent.trim()).toBe('No inventory items found');
  });

  it('emits sort event when sortable header is clicked and toggles direction', () => {
    component.columns = columns;
    component.rows = rows;
    fixture.detectChanges();

    const emitted: PzTableSortEvent[] = [];
    component.sort.subscribe((ev) => emitted.push(ev));

    const nameHeader = fixture.debugElement.queryAll(By.css('thead th'))[1];
    nameHeader.nativeElement.click();
    fixture.detectChanges();

    expect(emitted.length).toBe(1);
    expect(emitted[0]).toEqual({ column: 'name', direction: 'asc' });

    nameHeader.nativeElement.click();
    fixture.detectChanges();

    expect(emitted.length).toBe(2);
    expect(emitted[1]).toEqual({ column: 'name', direction: 'desc' });
  });

  it('does not emit sort event when non-sortable header is clicked', () => {
    component.columns = columns;
    component.rows = rows;
    fixture.detectChanges();

    let emitted = false;
    component.sort.subscribe(() => {
      emitted = true;
    });

    const idHeader = fixture.debugElement.queryAll(By.css('thead th'))[0];
    idHeader.nativeElement.click();
    fixture.detectChanges();

    expect(emitted).toBe(false);
  });

  it('emits rowClick when a row is clicked', () => {
    component.columns = columns;
    component.rows = rows;
    fixture.detectChanges();

    let clickedRow: TestItem | null = null;
    component.rowClick.subscribe((row) => {
      clickedRow = row;
    });

    const firstRow = fixture.debugElement.queryAll(By.css('tbody tr'))[0];
    firstRow.nativeElement.click();
    fixture.detectChanges();

    expect(clickedRow).toEqual(rows[0]);
  });
});
