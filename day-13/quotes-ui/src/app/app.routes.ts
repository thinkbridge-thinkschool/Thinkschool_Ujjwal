import { Routes } from '@angular/router';
import { LoginComponent } from './components/login/login';
import { authGuard } from './guards/auth-guard';

export const routes: Routes = [
  // Public, and eager - it's the first thing almost every unauthenticated
  // visitor sees, so a lazy-chunk round trip buys nothing here.
  { path: 'login', component: LoginComponent },

  { path: 'quotes', canActivate: [authGuard], loadComponent: () => import('./components/quotes/quotes').then((m) => m.QuotesComponent) },

  // Must come before quotes/:id - a param route matches any segment,
  // 'new' included, so the static routes have to win the match first.
  { path: 'quotes/new', canActivate: [authGuard], loadComponent: () => import('./components/quote-form/quote-form').then((m) => m.QuoteFormComponent) },
  { path: 'quotes/new-signal', canActivate: [authGuard], loadComponent: () => import('./components/quote-form-signal/quote-form-signal').then((m) => m.QuoteFormSignalComponent) },

  { path: 'quotes/:id', canActivate: [authGuard], loadComponent: () => import('./components/quote-detail/quote-detail').then((m) => m.QuoteDetailComponent) },

  { path: '', pathMatch: 'full', redirectTo: 'quotes' },

  // Wildcard: a real page, not a redirect - a bad link should say so, not
  // silently bounce somewhere else.
  { path: '**', loadComponent: () => import('./components/not-found/not-found').then((m) => m.NotFoundComponent) },
];
