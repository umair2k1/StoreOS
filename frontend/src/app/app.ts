import { Component, inject, signal } from '@angular/core';
import { DynamicManifestService } from './core/branding/dynamic-manifest.service';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('storeos-web');
  private readonly manifestService = inject(DynamicManifestService);
}
