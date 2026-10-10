import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { SettingsComponent } from './settings.component';
import { SettingsClient, AuthClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';
import { ToastService } from '../../shared/services/toast.service';

describe('SettingsComponent', () => {
  let fixture: ComponentFixture<SettingsComponent>;
  let component: SettingsComponent;
  let settingsClient: SettingsClient;
  let authClient: AuthClient;
  let authStore: AuthSignalStore;
  let router: Router;

  function createFileResponse(payload: unknown): FileResponse {
    const json = JSON.stringify(payload);
    return {
      data: new Blob([json], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    } as unknown as FileResponse;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        SettingsClient,
        AuthClient,
        AuthSignalStore,
        {
          provide: Router,
          useValue: {
            navigate: vi.fn()
          }
        },
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

    settingsClient = TestBed.inject(SettingsClient);
    authClient = TestBed.inject(AuthClient);
    authStore = TestBed.inject(AuthSignalStore);
    router = TestBed.inject(Router);

    vi.spyOn(settingsClient, 'getFinanceProviders').mockReturnValue(
      of([{ displayName: 'Bajaj Finserv' }])
    );
    vi.spyOn(settingsClient, 'getRepairsEnabled').mockReturnValue(
      of(createFileResponse({ enabled: true }))
    );
    vi.spyOn(settingsClient, 'setRepairsEnabled').mockReturnValue(
      of(createFileResponse({}))
    );
    vi.spyOn(settingsClient, 'patchSetting').mockReturnValue(
      of(createFileResponse({}))
    );
    vi.spyOn(settingsClient, 'getSetting').mockImplementation((key: string) => {
      if (key === 'shop_profile' || key === 'shop_info') {
        return of(createFileResponse({
          name: 'Saved Shop',
          address: '123 Market St',
          phone: '9998887776',
          email: 'saved@shop.com',
          gstin: '29ABCDE1234F1Z5',
          logoUrl: 'https://cdn.example.com/saved-logo.png'
        }));
      }
      if (key === 'tax_settings') {
        return of(createFileResponse({ defaultGstRate: 18 }));
      }
      if (key === 'bank_details') {
        return of(createFileResponse({
          bankName: 'HDFC Bank',
          accountName: 'Saved Account',
          accountNumber: '1122334455',
          ifsc: 'HDFC0001234',
          branch: 'Main'
        }));
      }
      return of(createFileResponse(null));
    });
    vi.spyOn(authClient, 'changePassword').mockReturnValue(
      of(createFileResponse({}))
    );

    fixture = TestBed.createComponent(SettingsComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    document.documentElement.removeAttribute('data-theme');
  });

  it('loads and populates existing settings on ngOnInit', async () => {
    fixture.detectChanges();
    await component.loadSettings();
    fixture.detectChanges();

    expect(settingsClient.getSetting).toHaveBeenCalledWith('shop_profile');
    expect(settingsClient.getSetting).toHaveBeenCalledWith('tax_settings');
    expect(settingsClient.getSetting).toHaveBeenCalledWith('bank_details');

    expect(component.shopName()).toBe('Saved Shop');
    expect(component.shopAddress()).toBe('123 Market St');
    expect(component.shopPhone()).toBe('9998887776');
    expect(component.shopEmail()).toBe('saved@shop.com');
    expect(component.shopGstin()).toBe('29ABCDE1234F1Z5');
    expect(component.shopLogoUrl()).toBe('https://cdn.example.com/saved-logo.png');
    expect(component.defaultGstRate()).toBe(18);
    expect(component.bankName()).toBe('HDFC Bank');
  });

  it('each tab save action calls correct PATCH /api/settings/{key} with correct shape', async () => {
    fixture.detectChanges();

    // 1. Shop Info Tab
    component.setTab('shop');
    component.shopName.set('PhoneZone Prime');
    component.shopAddress.set('42 Commercial St');
    component.shopPhone.set('9876543210');
    component.shopEmail.set('store@phonezone.in');
    component.shopGstin.set('29AAAAA0000A1Z5');
    component.shopLogoUrl.set('https://cdn.example.com/logo.png');

    await component.saveShopInfo();
    expect(settingsClient.patchSetting).toHaveBeenCalledWith(
      'shop_profile',
      expect.objectContaining({
        name: 'PhoneZone Prime',
        address: '42 Commercial St',
        phone: '9876543210',
        email: 'store@phonezone.in',
        gstin: '29AAAAA0000A1Z5',
        logoUrl: 'https://cdn.example.com/logo.png'
      })
    );

    // 2. Tax Settings Tab
    component.setTab('tax');
    component.defaultGstRate.set(12);

    await component.saveTaxSettings();
    expect(settingsClient.patchSetting).toHaveBeenCalledWith(
      'tax_settings',
      expect.objectContaining({
        defaultGstRate: 12
      })
    );

    // 3. Bank Details Tab
    component.setTab('bank');
    component.bankName.set('HDFC Bank');
    component.accountName.set('PhoneZone Retail Pvt Ltd');
    component.accountNumber.set('50200012345678');
    component.ifsc.set('HDFC0001234');
    component.branch.set('MG Road');

    await component.saveBankDetails();
    expect(settingsClient.patchSetting).toHaveBeenCalledWith(
      'bank_details',
      expect.objectContaining({
        bankName: 'HDFC Bank',
        accountName: 'PhoneZone Retail Pvt Ltd',
        accountNumber: '50200012345678',
        ifsc: 'HDFC0001234',
        branch: 'MG Road'
      })
    );
  });

  it('theme toggle actually changes the data-theme attribute on document root', async () => {
    fixture.detectChanges();
    component.setTab('theme');

    // Switch to dark theme
    await component.applyTheme('dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(component.currentTheme()).toBe('dark');
    expect(settingsClient.patchSetting).toHaveBeenCalledWith(
      'theme',
      { theme: 'dark' }
    );

    // Switch to light theme
    await component.applyTheme('light');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    expect(component.currentTheme()).toBe('light');
    expect(settingsClient.patchSetting).toHaveBeenCalledWith(
      'theme',
      { theme: 'light' }
    );
  });

  it('the repairs toggle is hidden without settings:manage permission and visible with it', async () => {
    // 1. Without permission
    authStore.setAuthenticated({
      accessToken: 'token-cashier',
      expiresAt: null,
      userId: 'u-cashier',
      displayName: 'Cashier',
      tenantId: 't-1',
      allBranches: false,
      branchIds: ['b-1'],
      permissions: ['pos:create']
    });

    fixture.detectChanges();
    component.setTab('repairs');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.repairs-tab-container')).toBeNull();

    // 2. With settings:manage permission
    authStore.setAuthenticated({
      accessToken: 'token-admin',
      expiresAt: null,
      userId: 'u-admin',
      displayName: 'Admin',
      tenantId: 't-1',
      allBranches: true,
      branchIds: ['b-1'],
      permissions: ['settings:manage']
    });

    fixture.detectChanges();
    expect(compiled.querySelector('.repairs-tab-container')).toBeTruthy();

    // Toggle and save repairs flag
    component.repairsEnabled.set(true);
    await component.saveRepairsEnabled();
    expect(settingsClient.setRepairsEnabled).toHaveBeenCalledWith({ enabled: true });
  });

  it('change password calls API and clears auth session navigating to /login', async () => {
    authStore.setAuthenticated({
      accessToken: 'valid-token',
      expiresAt: null,
      userId: 'u-1',
      displayName: 'Owner',
      tenantId: 't-1',
      allBranches: true,
      branchIds: ['b-1'],
      permissions: ['settings:manage']
    });

    fixture.detectChanges();
    component.setTab('account');

    component.currentPassword.set('OldPass123!');
    component.newPassword.set('NewSecurePass456!');
    component.confirmPassword.set('NewSecurePass456!');

    await component.changePassword();

    expect(authClient.changePassword).toHaveBeenCalledWith(
      expect.objectContaining({
        currentPassword: 'OldPass123!',
        newPassword: 'NewSecurePass456!'
      })
    );

    // Auth cleared and navigated to /login
    expect(authStore.isAuthenticated()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });
});
