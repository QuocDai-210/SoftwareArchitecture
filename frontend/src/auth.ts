import NextAuth from "next-auth";
import Keycloak from "next-auth/providers/keycloak";
import { decode, encode, type JWT } from "next-auth/jwt";
import type { NextRequest } from "next/server";

const publicIssuer = `${process.env.AUTH_PUBLIC_URL}/realms/swiftride`;
const internalIssuer = `${process.env.AUTH_INTERNAL_URL}/realms/swiftride`;
const cookieName = process.env.AUTH_URL?.startsWith("https:") ? "__Secure-authjs.session-token" : "authjs.session-token";
export const sessionCookie = { name: cookieName, options: { httpOnly: true, sameSite: "lax" as const, path: "/", secure: cookieName.startsWith("__Secure-") } };

export const { handlers, auth, signIn, signOut } = NextAuth({
  trustHost: true,
  secret: process.env.AUTH_SECRET,
  cookies: { sessionToken: sessionCookie },
  providers: [Keycloak({
    clientId: "swiftride-web", clientSecret: process.env.AUTH_KEYCLOAK_SECRET!, issuer: publicIssuer,
    wellKnown: `${internalIssuer}/.well-known/openid-configuration`,
    authorization: { url: `${publicIssuer}/protocol/openid-connect/auth`, params: { scope: "openid profile email" } },
    token: `${internalIssuer}/protocol/openid-connect/token`,
    userinfo: `${internalIssuer}/protocol/openid-connect/userinfo`,
    checks: ["pkce", "state"],
  })],
  callbacks: {
    jwt({ token, account, profile }) {
      if (account) {
        if (typeof profile?.sub !== "string" || !profile.sub) throw new Error("Missing OIDC subject");
        token.sub = profile.sub;
        token.accessToken = account.access_token;
        token.refreshToken = account.refresh_token;
        token.expiresAt = account.expires_at ?? 0;
        const realm = (profile as { realm_access?: { roles?: string[] } } | undefined)?.realm_access;
        token.roles = realm?.roles ?? [];
      }
      // ponytail: token refresh serialized by backend proxy; no access token in browser session.
      return token;
    },
    session({ session, token }) {
      session.user.id = token.sub!;
      session.user.roles = (token.roles as string[]) ?? [];
      return session;
    },
  },
});

export async function readBackendToken(request: NextRequest): Promise<{ token: string; cookie?: string }> {
  const chunks = [...request.cookies.getAll()].filter(c => c.name === cookieName || c.name.startsWith(`${cookieName}.`))
    .sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }));
  const raw = chunks.map(c => c.value).join("");
  if (!raw) throw new Error("Unauthenticated");
  const token = await decode({ token: raw, secret: process.env.AUTH_SECRET!, salt: cookieName });
  if (!token?.accessToken || !token.refreshToken) throw new Error("Unauthenticated");
  if (Number(token.expiresAt) > Date.now() / 1000 + 30) return { token: String(token.accessToken) };
  return refresh(raw, token);
}

// One process in local Compose; shared session store required before horizontal web scaling.
const refreshing = new Map<string, Promise<{ token: string; cookie: string }>>();
async function refresh(raw: string, token: JWT) {
  let pending = refreshing.get(raw);
  if (!pending) {
    pending = (async () => {
      const response = await fetch(`${internalIssuer}/protocol/openid-connect/token`, {
        method: "POST", cache: "no-store", signal: AbortSignal.timeout(5000),
        body: new URLSearchParams({ grant_type: "refresh_token", refresh_token: String(token.refreshToken),
          client_id: "swiftride-web", client_secret: process.env.AUTH_KEYCLOAK_SECRET! }),
      });
      if (!response.ok) throw new Error("Unauthenticated");
      const body = await response.json();
      const cookie = await encode({ token: { ...token, accessToken: body.access_token,
        refreshToken: body.refresh_token ?? token.refreshToken, expiresAt: Math.floor(Date.now() / 1000) + body.expires_in },
        secret: process.env.AUTH_SECRET!, salt: cookieName, maxAge: 30 * 24 * 3600 });
      return { token: String(body.access_token), cookie };
    })();
    refreshing.set(raw, pending);
    void pending.finally(() => setTimeout(() => refreshing.delete(raw), 30000)).catch(() => {});
  }
  return pending;
}
