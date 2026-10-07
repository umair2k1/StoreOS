import { HttpInterceptorFn } from '@angular/common/http';

export const IDEMPOTENT_MUTATION_PATTERNS: RegExp[] = [
  /\/pos\/checkout$/,
  /\/pos\/estimate\/[^/]+\/convert$/,
  /\/sales\/[^/]+\/payments$/,
  /\/repair-jobs\/[^/]+\/bill$/,
  /\/tenant\/import$/,
];

export const idempotencyInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'POST' || !IDEMPOTENT_MUTATION_PATTERNS.some((p) => p.test(req.url))) {
    return next(req);
  }

  const key = crypto.randomUUID();
  const clonedReq = req.clone({ setHeaders: { 'Idempotency-Key': key } });
  return next(clonedReq);
};
