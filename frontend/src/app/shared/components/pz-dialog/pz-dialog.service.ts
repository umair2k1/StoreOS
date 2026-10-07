import {
  Injectable,
  InjectionToken,
  Injector,
  Type,
  inject,
  signal,
  computed
} from '@angular/core';
import { Overlay, OverlayConfig, OverlayRef } from '@angular/cdk/overlay';
import { ComponentPortal } from '@angular/cdk/portal';
import { Subject, Observable } from 'rxjs';

export const PZ_DIALOG_DATA = new InjectionToken<unknown>('PZ_DIALOG_DATA');

export interface PzDialogConfig<D = unknown> {
  data?: D;
  width?: string;
  maxWidth?: string;
  hasBackdrop?: boolean;
  disableClose?: boolean;
  panelClass?: string | string[];
}

export class PzDialogRef<T = unknown, R = unknown> {
  componentInstance: T | null = null;
  readonly isOpen = signal<boolean>(true);

  private readonly afterClosed$ = new Subject<R | undefined>();

  constructor(
    private readonly overlayRef: OverlayRef,
    private readonly onClosedCallback: () => void
  ) {}

  close(result?: R): void {
    if (!this.isOpen()) {
      return;
    }

    this.isOpen.set(false);
    this.afterClosed$.next(result);
    this.afterClosed$.complete();
    this.overlayRef.dispose();
    this.onClosedCallback();
  }

  afterClosed(): Observable<R | undefined> {
    return this.afterClosed$.asObservable();
  }
}

@Injectable({
  providedIn: 'root'
})
export class PzDialogService {
  private readonly overlay = inject(Overlay);
  private readonly injector = inject(Injector);

  private readonly _activeDialogs = signal<PzDialogRef<unknown, unknown>[]>([]);
  readonly activeDialogs = this._activeDialogs.asReadonly();
  readonly hasOpenDialogs = computed(() => this._activeDialogs().length > 0);

  open<T, D = unknown, R = unknown>(
    component: Type<T>,
    configOrData?: PzDialogConfig<D> | D
  ): PzDialogRef<T, R> {
    const isConfigObject =
      configOrData !== null &&
      typeof configOrData === 'object' &&
      !Array.isArray(configOrData) &&
      ('data' in configOrData ||
        'hasBackdrop' in configOrData ||
        'width' in configOrData ||
        'disableClose' in configOrData);

    const config: PzDialogConfig<D> = isConfigObject
      ? (configOrData as PzDialogConfig<D>)
      : { data: configOrData as D };

    const overlayConfig = new OverlayConfig({
      hasBackdrop: config.hasBackdrop ?? true,
      backdropClass: 'cdk-overlay-dark-backdrop',
      panelClass: config.panelClass ?? 'pz-dialog-overlay-panel',
      width: config.width ?? 'auto',
      maxWidth: config.maxWidth ?? '90vw',
      positionStrategy: this.overlay.position().global().centerHorizontally().centerVertically(),
      scrollStrategy: this.overlay.scrollStrategies.block()
    });

    const overlayRef = this.overlay.create(overlayConfig);

    const dialogRef = new PzDialogRef<T, R>(overlayRef, () => {
      this._activeDialogs.update((dialogs) =>
        dialogs.filter((d) => d !== (dialogRef as unknown as PzDialogRef<unknown, unknown>))
      );
    });

    const dialogInjector = Injector.create({
      parent: this.injector,
      providers: [
        { provide: PzDialogRef, useValue: dialogRef },
        { provide: PZ_DIALOG_DATA, useValue: config.data }
      ]
    });

    const portal = new ComponentPortal(component, null, dialogInjector);
    const componentRef = overlayRef.attach(portal);
    dialogRef.componentInstance = componentRef.instance;

    if (!config.disableClose) {
      overlayRef.backdropClick().subscribe(() => {
        dialogRef.close();
      });

      overlayRef.keydownEvents().subscribe((event) => {
        if (event.key === 'Escape') {
          dialogRef.close();
        }
      });
    }

    this._activeDialogs.update((dialogs) => [
      ...dialogs,
      dialogRef as unknown as PzDialogRef<unknown, unknown>
    ]);

    return dialogRef;
  }

  closeAll(): void {
    const current = [...this._activeDialogs()];
    for (let i = current.length - 1; i >= 0; i--) {
      current[i].close();
    }
  }
}
