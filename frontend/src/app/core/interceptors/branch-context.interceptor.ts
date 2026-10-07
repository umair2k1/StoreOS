import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { BranchContextService } from '../branch/branch-context.service';

export const branchContextInterceptor: HttpInterceptorFn = (req, next) => {
  const branchContext = inject(BranchContextService);
  const branchId = branchContext.currentBranchId();

  if (branchId) {
    const cloned = req.clone({
      setHeaders: { 'X-Branch-Id': branchId }
    });
    return next(cloned);
  }

  return next(req);
};
