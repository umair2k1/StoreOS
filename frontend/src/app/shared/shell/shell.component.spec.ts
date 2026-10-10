import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { ShellComponent } from './shell.component';
import { BrandingSignalStore } from '../../core/branding/branding-signal-store';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';

describe('ShellComponent', () => {
  let fixture: ComponentFixture<ShellComponent>;
  let brandingStore: BrandingSignalStore;
  let titleService: Title;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [
        BrandingSignalStore,
        AuthSignalStore,
        Title,
        provideRouter([])
      ]
    });

    brandingStore = TestBed.inject(BrandingSignalStore);
    brandingStore.reset();
    titleService = TestBed.inject(Title);
    fixture = TestBed.createComponent(ShellComponent);
  });

  it('updates document title reactively when tenant or branch branding signals change', () => {
    // Initial state: default store values
    fixture.detectChanges();
    expect(titleService.getTitle()).toBe('StoreOS');

    // Update tenant name
    brandingStore.setBranding({ tenantName: 'Apex Electronics' });
    fixture.detectChanges();
    expect(titleService.getTitle()).toBe('Apex Electronics');

    // Update active branch name
    brandingStore.setBranding({ activeBranchName: 'North Gate' });
    fixture.detectChanges();
    expect(titleService.getTitle()).toBe('North Gate | Apex Electronics');

    // Clear active branch name
    brandingStore.setBranding({ activeBranchName: null });
    fixture.detectChanges();
    expect(titleService.getTitle()).toBe('Apex Electronics');
  });

  it('renders shop logo instead of shop name when logoUrl is present', () => {
    // When no logo is present, render brand-name text
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.brand-name')).toBeTruthy();
    expect(el.querySelector('.brand-logo')).toBeFalsy();

    // When logoUrl is provided, render brand-logo img instead of brand-name
    brandingStore.setBranding({ logoUrl: 'https://cdn.example.com/shop-logo.png' });
    fixture.detectChanges();
    expect(el.querySelector('.brand-logo')).toBeTruthy();
    expect(el.querySelector('.brand-name')).toBeFalsy();
    const img = el.querySelector('.brand-logo') as HTMLImageElement;
    expect(img.src).toBe('https://cdn.example.com/shop-logo.png');
  });
});
