import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { LoginRequest, LoginResponse } from '../models/auth.model';
import { environment } from '../../environments/environment';

const AUTH_URL = `${environment.apiOrigin}/api/auth`;
const STORAGE_KEY = 'quotes.auth';

interface StoredAuth {
  accessToken: string;
  expiresAt: number;
}

function decodeUserId(token: string): string | null {
  try {
    const payload = token.split('.')[1];
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    return JSON.parse(json).sub ?? null;
  } catch {
    return null;
  }
}

function readStoredAuth(): StoredAuth | null {
  const raw = localStorage.getItem(STORAGE_KEY);
  if (!raw) return null;
  try {
    const stored: StoredAuth = JSON.parse(raw);
    if (stored.expiresAt <= Date.now()) {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }
    return stored;
  } catch {
    return null;
  }
}

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly initial = readStoredAuth();
  private readonly token = signal<string | null>(this.initial?.accessToken ?? null);

  readonly isLoggedIn = computed(() => this.token() !== null);
  readonly currentUserId = computed(() => {
    const token = this.token();
    return token ? decodeUserId(token) : null;
  });

  getToken(): string | null {
    return this.token();
  }

  login(credentials: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${AUTH_URL}/login`, credentials)
      .pipe(tap((response) => this.persist(response)));
  }

  register(credentials: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${AUTH_URL}/register`, credentials)
      .pipe(tap((response) => this.persist(response)));
  }

  logout(): void {
    localStorage.removeItem(STORAGE_KEY);
    this.token.set(null);
  }

  private persist(response: LoginResponse): void {
    const expiresAt = Date.now() + response.expiresIn * 1000;
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({ accessToken: response.accessToken, expiresAt } satisfies StoredAuth),
    );
    this.token.set(response.accessToken);
  }
}
