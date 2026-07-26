import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { LogLevel, provideAuth } from 'angular-auth-oidc-client';
import { createOidcConfig } from '@ecommerce/shared';

import { routes } from './app.routes';
import { environment } from '../environments/environment';

/**
 * Composition root for the Angular storefront.
 *
 * The OIDC *configuration* comes from `@ecommerce/shared` — the same function
 * the React storefront calls — so both apps request identical scopes and
 * redirect URIs and cannot drift apart.
 */
const shared = createOidcConfig({
  authority: environment.keycloakAuthority,
  clientId: 'storefront-angular',
  origin: window.location.origin,
});

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    provideRouter(routes, withComponentInputBinding()),

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
