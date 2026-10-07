import { Directive, Input, TemplateRef, ViewContainerRef, effect, signal } from '@angular/core';
import { AuthSignalStore } from '../auth/auth-signal-store';

/**
 * Structural directive: `*pzCan="'sales:create'"`
 *
 * SECURITY NOTICE:
 * This directive controls UI affordances and visibility for UX convenience ONLY.
 * It is NOT a security boundary. Client-side state can be inspected or altered in
 * browser devtools. The StoreOS backend remains the sole, authoritative enforcer
 * of authorization via MediatR pipeline behaviors (P-114/P-308) and live DB checks.
 */
@Directive({
  selector: '[pzCan]',
  standalone: true
})
export class PermissionDirective {
  private hasView = false;
  private readonly permissionCodeSignal = signal<string>('');

  @Input() set pzCan(permissionCode: string) {
    this.permissionCodeSignal.set(permissionCode);
  }

  constructor(
    private readonly templateRef: TemplateRef<unknown>,
    private readonly viewContainer: ViewContainerRef,
    private readonly authStore: AuthSignalStore
  ) {
    effect(() => {
      const code = this.permissionCodeSignal();
      const allowed = code ? this.authStore.hasPermission(code) : false;

      if (allowed && !this.hasView) {
        this.viewContainer.createEmbeddedView(this.templateRef);
        this.hasView = true;
      } else if (!allowed && this.hasView) {
        this.viewContainer.clear();
        this.hasView = false;
      }
    });
  }
}
