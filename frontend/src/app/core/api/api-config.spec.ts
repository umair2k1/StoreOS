import { TestBed } from '@angular/core/testing';
import { provideApiConfig, API_BASE_URL } from './api-config';
import { HttpClient } from '@angular/common/http';

describe('provideApiConfig', () => {
  it('provides HttpClient and resolved API_BASE_URL', () => {
    TestBed.configureTestingModule({
      providers: [provideApiConfig()]
    });

    const http = TestBed.inject(HttpClient);
    const baseUrl = TestBed.inject(API_BASE_URL);

    expect(http).toBeTruthy();
    expect(baseUrl).toBe('http://localhost:5000');
  });
});
