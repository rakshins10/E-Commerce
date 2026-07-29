import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { switchMap, take } from 'rxjs';

import { environment } from '../../environments/environment';

/**
 * Attaches the access token to calls that need one.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * React's `ApiClient` takes a token *getter* and each caller constructs a client
 * with it. Angular uses an interceptor, which applies to every `HttpClient`
 * request automatically — more ceremony to set up, but impossible to forget on
 * a new call site. A genuine point for Angular's DI model.
 *
 * ---
 * **Only our own BFF gets the token.** Attaching a bearer token to every
 * outbound request would leak it to any third-party URL the app ever calls —
 * an analytics endpoint, an image CDN — which is a real and easily-made
 * credential leak.
 *
 * Unlike the storefront, EVERY call here needs a token - there is no anonymous back-office page.
 * A request without one is still sent rather than blocked, because the server rejecting it produces a
 * clearer failure than a client that silently drops the call.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith(environment.adminBffBaseUrl)) {
    return next(request);
  }

  const oidc = inject(OidcSecurityService);

  return oidc.getAccessToken().pipe(
    take(1),
    switchMap((token) =>
      next(
        token
          ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
          : request,
      ),
    ),
  );
};
