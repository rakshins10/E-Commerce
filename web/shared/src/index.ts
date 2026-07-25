/**
 * @ecommerce/shared — everything the applications *know*, as opposed to what
 * they *show*.
 *
 * Consumed by the React storefront, the Angular storefront, both admin panels
 * and the React Native app. Nothing here may import `react` or `@angular/core`:
 * the moment it does, it has stopped being shared and become a React layer with
 * extra steps.
 *
 * @see web/README.md
 * @see docs/adr/0014-react-and-angular-in-lockstep.md
 */

export * from './permissions/index.js';
export * from './auth/index.js';
export * from './formatting/index.js';
export * from './api/index.js';
