import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { LogLevel, provideAuth } from 'angular-auth-oidc-client';
import { createOidcConfig } from './core/auth-config';

import { authInterceptor } from './core/auth-interceptor';
import { routes } from './app.routes';
import { environment } from '../environments/environment';

/**
 * Composition root for the Angular back office.
 *
 * Its OWN Keycloak client (`admin-angular`), separate from the storefront's. Two
 * clients means two redirect-URI allow-lists, so a redirect misconfiguration on
 * the shop cannot be used to obtain a token the back office would accept.
 */
const shared = createOidcConfig({
  authority: environment.keycloakAuthority,
  clientId: 'admin-angular',
  origin: window.location.origin,
});

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    provideRouter(routes, withComponentInputBinding()),

    // withFetch uses the Fetch API rather than XMLHttpRequest - required for
    // request cancellation to work properly and the modern default.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),

    provideAuth({
      config: {
        authority: shared.authority,
        redirectUrl: shared.redirectUri,
        postLogoutRedirectUri: shared.postLogoutRedirectUri,
        clientId: shared.clientId,
        scope: shared.scope,

        // Authorization Code + PKCE. No client secret: this bundle is
        // downloaded to the browser, so anything inside it is readable.
        responseType: 'code',

        // Renew in a hidden iframe before the 5-minute access token expires,
        // so the user is never bounced to a login page mid-session.
        silentRenew: true,
        silentRenewUrl: shared.silentRedirectUri,
        useRefreshToken: true,
        renewTimeBeforeTokenExpiresInSeconds: 30,

        logLevel: environment.production ? LogLevel.Error : LogLevel.Warn,
      },
    }),
  ],
};
