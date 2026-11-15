import { decode } from "next-auth/jwt";
import type { NextRequest } from "next/server";
import { cookieName } from "./session-cookie";

export async function readBackendToken(
  request: NextRequest,
): Promise<{ token: string; roles: string[] }> {
  const chunks = [...request.cookies.getAll()]
    .filter((cookie) => cookie.name === cookieName || cookie.name.startsWith(`${cookieName}.`))
    .sort((left, right) => left.name.localeCompare(right.name, undefined, { numeric: true }));
  const raw = chunks.map((cookie) => cookie.value).join("");
  if (!raw) throw new Error("Unauthenticated");
  const session = await decode({ token: raw, secret: process.env.AUTH_SECRET!, salt: cookieName });
  if (
    !session?.accessToken ||
    typeof session.expiresAt !== "number" ||
    !Number.isFinite(session.expiresAt) ||
    session.expiresAt <= Date.now() / 1000
  ) {
    throw new Error("Unauthenticated");
  }
  return {
    token: String(session.accessToken),
    roles: Array.isArray(session.roles) ? session.roles.map(String) : [],
  };
}
