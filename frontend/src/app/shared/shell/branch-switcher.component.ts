import { Component, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';
import { BranchContextService } from '../../core/branch/branch-context.service';

@Component({
  selector: 'app-branch-switcher',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (isMultiBranch()) {
      <div class="branch-switcher" data-testid="branch-switcher">
        <label for="branch-select" class="branch-label">Branch:</label>
        <select
          id="branch-select"
          class="branch-select"
          [value]="branchContext.currentBranchId() ?? ''"
          (change)="onBranchChange($event)"
        >
          @for (branch of branchContext.branches(); track branch.id) {
            <option [value]="branch.id">{{ branch.name }}</option>
          }
        </select>
      </div>
    }
  `,
  styleUrl: './branch-switcher.component.scss'
})
export class BranchSwitcherComponent {
  readonly authStore = inject(AuthSignalStore);
  readonly branchContext = inject(BranchContextService);

  readonly isMultiBranch = computed(() => {
    return this.authStore.allBranches() ||
      this.authStore.branchIds().length > 1 ||
      this.branchContext.branches().length > 1;
  });

  onBranchChange(event: Event): void {
    const target = event.target as HTMLSelectElement;
    if (target && target.value) {
      this.branchContext.setCurrentBranchId(target.value);
    }
  }
}
