import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { OnboardingWizardComponent } from './onboarding-wizard.component';
import { BrandingSignalStore } from '../../core/branding/branding-signal-store';

describe('OnboardingWizardComponent', () => {
  let fixture: ComponentFixture<OnboardingWizardComponent>;
  let component: OnboardingWizardComponent;
  let httpMock: HttpTestingController;
  let brandingStore: BrandingSignalStore;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OnboardingWizardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        BrandingSignalStore
      ]
    });

    fixture = TestBed.createComponent(OnboardingWizardComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    brandingStore = TestBed.inject(BrandingSignalStore);
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('submits wizard form calling both POST /api/branches and PATCH /api/settings/shop_profile', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    // Fill Step 1
    component.form.patchValue({
      shopName: 'Metro Electronics',
      gstin: '29ABCDE1234F1Z5',
      phone: '+91 9876543210',
      address: '101 MG Road, Bangalore',
      receiptFooter: 'Thank you for shopping with us!'
    });
    component.goToNextStep();
    fixture.detectChanges();

    expect(component.currentStep()).toBe(2);

    // Fill Step 2
    component.form.patchValue({
      branchName: 'Brigade Road Branch',
      branchCode: 'BR01'
    });
    fixture.detectChanges();

    component.onSubmit();

    // Expect POST /api/branches
    const branchReq = httpMock.expectOne(req => req.url.endsWith('/api/branches') && req.method === 'POST');
    expect(JSON.parse(branchReq.request.body)).toEqual({
      code: 'BR01',
      name: 'Brigade Road Branch',
      address: '101 MG Road, Bangalore',
      phone: '+91 9876543210',
      gstin: '29ABCDE1234F1Z5'
    });
    branchReq.flush(new Blob([JSON.stringify({ id: 'branch-101', code: 'BR01', name: 'Brigade Road Branch' })]));

    // Expect PATCH /api/settings/shop_profile
    const settingsReq = httpMock.expectOne(req => req.url.includes('/api/settings/shop_profile') && req.method === 'PATCH');
    expect(JSON.parse(settingsReq.request.body)).toEqual({
      name: 'Metro Electronics',
      address: '101 MG Road, Bangalore',
      phone: '+91 9876543210',
      gstin: '29ABCDE1234F1Z5',
      receiptFooter: 'Thank you for shopping with us!'
    });
    settingsReq.flush(new Blob());


    // Verify Branding updated
    expect(brandingStore.tenantName()).toBe('Metro Electronics');
    expect(brandingStore.activeBranchName()).toBe('Brigade Road Branch');

    // Verify Router navigation
    expect(navigateSpy).toHaveBeenCalledWith('/pos');
  });
});
