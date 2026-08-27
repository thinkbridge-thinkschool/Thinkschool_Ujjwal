export interface LoginRequest {
  email: string;
  password: string;
}

// Matches the real /api/auth/login response shape exactly.
export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}
