import { Component, EventEmitter, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';

export interface LogoUploadResponse {
  logoUrl: string;
}

@Component({
  selector: 'app-logo-uploader',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="logo-uploader">
      <div class="uploader-controls">
        <div class="preview-container">
          @if (previewUrl()) {
            <img [src]="previewUrl()" alt="Logo preview" />
          } @else {
            <span class="placeholder-text">No Logo</span>
          }
        </div>

        <input
          type="file"
          id="logo-input"
          class="file-input"
          accept="image/png,image/jpeg,image/webp"
          (change)="onFileSelected($event)"
        />
      </div>

      @if (errorMessage()) {
        <div class="error-message" role="alert">{{ errorMessage() }}</div>
      }

      @if (isUploading()) {
        <div class="upload-status">Uploading logo...</div>
      }
    </div>
  `,
  styleUrl: './logo-uploader.component.scss'
})
export class LogoUploaderComponent {
  private readonly http = inject(HttpClient);

  readonly previewUrl = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);
  readonly isUploading = signal<boolean>(false);

  @Output() readonly logoUploaded = new EventEmitter<string>();

  onFileSelected(event: Event): void {
    const target = event.target as HTMLInputElement;
    const file = target.files?.[0];
    if (!file) {
      return;
    }

    this.errorMessage.set(null);
    this.previewUrl.set(URL.createObjectURL(file));

    this.upload(file);
  }

  upload(file: File): void {
    this.isUploading.set(true);
    this.errorMessage.set(null);

    const formData = new FormData();
    formData.append('file', file, file.name);

    this.http.post<LogoUploadResponse>('/api/tenants/me/logo', formData).subscribe({
      next: (res) => {
        this.isUploading.set(false);
        if (res?.logoUrl) {
          this.previewUrl.set(res.logoUrl);
          this.logoUploaded.emit(res.logoUrl);
        }
      },
      error: (err: unknown) => {
        this.isUploading.set(false);
        if (err instanceof HttpErrorResponse) {
          const message = err.error?.message || err.error?.title || 'Logo upload failed. Please try again.';
          this.errorMessage.set(message);
        } else {
          this.errorMessage.set('Logo upload failed. Please try again.');
        }
      }
    });
  }
}
