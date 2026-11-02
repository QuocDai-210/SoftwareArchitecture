import { decode, encode, type JWT } from "next-auth/jwt";
import type { NextRequest } from "next/server";
import { cookieName } from "./session-cookie";
const internalIssuer = `${process.env.AUTH_INTERNAL_URL}/realms/swiftride`;

export async function readBackendToken(
  request: NextRequest,
): Promise<{ token: string; cookie?: string }> {
  const chunks = [...request.cookies.getAll()]
    .filter((c) => c.name === cookieName || c.name.startsWith(`${cookieName}.`))
    .sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }));
  const raw = chunks.map((c) => c.value).join("");
  if (!raw) throw new Error("Unauthenticated");
  const token = await decode({ token: raw, secret: process.env.AUTH_SECRET!, salt: cookieName });
  if (!token?.accessToken || !token.refreshToken) throw new Error("Unauthenticated");
  if (Number(token.expiresAt) > Date.now() / 1000 + 30) return { token: String(token.accessToken) };
  return refresh(raw, token);
}

// ponytail: one web process; use shared session store before horizontal scaling.
const refreshing = new Map<string, Promise<{ token: string; cookie: string }>>();
async function refresh(raw: string, token: JWT) {
  let pending = refreshing.get(raw);
  if (!pending) {
    pending = (async () => {
      const response = await fetch(`${internalIssuer}/protocol/openid-connect/token`, {
        method: "POST",
        cache: "no-store",
        signal: AbortSignal.timeout(5000),
        body: new URLSearchParams({
          grant_type: "refresh_token",
          refresh_token: String(token.refreshToken),
          client_id: "swiftride-web",
          client_secret: process.env.AUTH_KEYCLOAK_SECRET!,
        }),
      });
      if (!response.ok) throw new Error("Unauthenticated");
      const body = await response.json();
      const cookie = await encode({
        token: {
          ...token,
          accessToken: body.access_token,
          refreshToken: body.refresh_token ?? token.refreshToken,
          expiresAt: Math.floor(Date.now() / 1000) + body.expires_in,
        },
        secret: process.env.AUTH_SECRET!,
        salt: cookieName,
        maxAge: 30 * 24 * 3600,
      });
      return { token: String(body.access_token), cookie };
    })();
    refreshing.set(raw, pending);
    void pending.finally(() => setTimeout(() => refreshing.delete(raw), 30000)).catch(() => {});
  }
  return pending;
}
