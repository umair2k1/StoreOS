import { HttpInterceptorFn } from '@angular/common/http';
import { tap } from 'rxjs/operators';

export interface CorrelatedError {
  correlationId?: string;
}

export const correlationIdInterceptor: HttpInterceptorFn = (req, next) => {
  const correlationId = crypto.randomUUID();
  const clonedReq = req.clone({ setHeaders: { 'X-Correlation-ID': correlationId } });

  return next(clonedReq).pipe(
    tap({
      error: (err: unknown) => {
        // Attach the correlation id to the error object so error-toast/problem-details UI
        // can surface it to the user for support purposes.
        if (err && typeof err === 'object') {
          (err as CorrelatedError).correlationId = correlationId;
        }
      }
    })
  );
};
