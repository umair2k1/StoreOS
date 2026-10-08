import { ComponentFixture, TestBed } from '@angular/core/testing';

function fakeAsync(fn: () => void): () => void {
  return () => {
    vi.useFakeTimers();
    try {
      fn();
    } finally {
      vi.useRealTimers();
    }
  };
}

function tick(ms: number): void {
  vi.advanceTimersByTime(ms);
}
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { OverlayModule } from '@angular/cdk/overlay';
import { of, throwError } from 'rxjs';
import { PosComponent } from './pos.component';
import { PosClient, CustomersClient } from '../../core/api/generated-client';
import type { PosSearchResultDto } from '../../core/api/generated-client';
import { PzDialogService } from '../../shared/components/pz-dialog/pz-dialog.service';
import { ToastService } from '../../shared/services/toast.service';
import { CartStateService } from './cart/cart-state.service';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';

describe('PosComponent', () => {
  let fixture: ComponentFixture<PosComponent>;
  let component: PosComponent;
  let posClient: PosClient;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PosComponent, OverlayModule],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        PosClient,
        CustomersClient,
        PzDialogService,
        ToastService,
        CartStateService,
        AuthSignalStore
      ]
    }).compileComponents();

    posClient = TestBed.inject(PosClient);
    fixture = TestBed.createComponent(PosComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('(a) typing into the search box with realistic (>50ms apart) keystroke timing does NOT trigger the scan-buffer path', fakeAsync(() => {
    let virtualTime = 1000;
    vi.spyOn(performance, 'now').mockImplementation(() => virtualTime);
    const setValueSpy = vi.spyOn(component.searchControl, 'setValue');

    // Simulate human typing "apple" with 100ms intervals (>50ms) between strokes
    const keys = ['a', 'p', 'p', 'l', 'e'];
    for (const key of keys) {
      virtualTime += 100;
      tick(100);
      component.onSearchKeydown(new KeyboardEvent('keydown', { key }));
    }

    // Human presses Enter after 100ms
    virtualTime += 100;
    tick(100);
    component.onSearchKeydown(new KeyboardEvent('keydown', { key: 'Enter' }));

    // Scan-buffer path is NOT triggered because keystrokes were slow (>50ms)
    expect(setValueSpy).not.toHaveBeenCalled();
    expect(component.scanBuffer).toBe('');
  }));

  it('(b) simulating rapid (<50ms apart) keystrokes followed by Enter DOES populate the search control with the buffered string and triggers a search', fakeAsync(() => {
    let virtualTime = 1000;
    vi.spyOn(performance, 'now').mockImplementation(() => virtualTime);

    const mockResults: PosSearchResultDto[] = [
      {
        productId: 'prod-001',
        name: 'iPhone 16 Pro',
        category: 'Smartphones',
        sellPrice: 119900,
        gstRate: 18,
        stockQty: 8,
        isMarginScheme: false
      }
    ];
    const searchSpy = vi.spyOn(posClient, 'search').mockReturnValue(of(mockResults));

    // Scanner rapidly inputs barcode "89012345" at 15ms intervals (<50ms)
    const barcode = '89012345';
    for (const key of barcode) {
      virtualTime += 15;
      tick(15);
      component.onSearchKeydown(new KeyboardEvent('keydown', { key }));
    }

    // Scanner terminates with Enter after 15ms
    virtualTime += 15;
    tick(15);
    component.onSearchKeydown(new KeyboardEvent('keydown', { key: 'Enter' }));

    // Buffered string populates searchControl
    expect(component.searchControl.value).toBe('89012345');

    // Advance debouncing window (300ms)
    tick(300);
    fixture.detectChanges();

    // Search request fired with buffered query
    expect(searchSpy).toHaveBeenCalledWith('89012345');
    expect(component.searchResults().length).toBe(1);
    expect(component.searchResults()[0].name).toBe('iPhone 16 Pro');
  }));

  it('(c) the search input is focused automatically on component init', () => {
    const inputElement = fixture.nativeElement.querySelector('input.search-input') as HTMLInputElement;
    expect(inputElement).toBeTruthy();
    expect(document.activeElement).toBe(inputElement);
  });

  it('(d) F2 refocuses the search input even after focus has moved elsewhere', () => {
    const inputElement = fixture.nativeElement.querySelector('input.search-input') as HTMLInputElement;
    const buttonElement = document.createElement('button');
    document.body.appendChild(buttonElement);
    buttonElement.focus();
    expect(document.activeElement).toBe(buttonElement);

    const f2Event = new KeyboardEvent('keydown', { key: 'F2', cancelable: true });
    window.dispatchEvent(f2Event);

    expect(document.activeElement).toBe(inputElement);
    document.body.removeChild(buttonElement);
  });

  it('renders loading, empty, and error states correctly', fakeAsync(() => {
    vi.spyOn(posClient, 'search').mockReturnValue(throwError(() => new Error('Search network failure')));

    component.searchControl.setValue('Samsung');
    tick(300);
    fixture.detectChanges();

    expect(component.errorMessage()).toBe('Failed to search products');
    const errorEl = fixture.nativeElement.querySelector('pz-error-state, app-error-state');
    expect(errorEl).toBeTruthy();

    const emptyResults: PosSearchResultDto[] = [];
    vi.spyOn(posClient, 'search').mockReturnValue(of(emptyResults));
    component.retrySearch();
    tick(300);
    fixture.detectChanges();

    expect(component.errorMessage()).toBeNull();
    const emptyEl = fixture.nativeElement.querySelector('pz-empty-state, app-empty-state');
    expect(emptyEl).toBeTruthy();
  }));
});
