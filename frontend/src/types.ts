export interface AccessTokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
}

export interface User {
  id: string;
  email: string;
  displayName: string;
}
