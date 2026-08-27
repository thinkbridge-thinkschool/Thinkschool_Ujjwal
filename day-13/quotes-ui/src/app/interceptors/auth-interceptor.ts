import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth';

const API_ORIGIN = 'http://localhost:5296';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(API_ORIGIN)) {
    return next(req);
  }

  const token = inject(AuthService).getToken();
  if (!token) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
