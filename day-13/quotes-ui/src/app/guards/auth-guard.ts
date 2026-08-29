import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth';

// Returning a UrlTree (rather than navigating imperatively and returning
// false) is what makes this a redirect instead of a bare rejection - the
// router treats it as "go here instead." Preserving the attempted URL as
// returnUrl is what lets LoginComponent send the user back to where they
// were actually headed instead of a hardcoded default.
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isLoggedIn()) {
    return true;
  }

  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
