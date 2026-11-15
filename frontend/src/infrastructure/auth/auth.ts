import NextAuth from "next-auth";
import Credentials from "next-auth/providers/credentials";
import { sessionCookie } from "./session-cookie";

type LoginResult = {
  accessToken: string;
  expiresAt: number;
  user: { id: string; name: string; role: "rider" | "driver" };
};

export const { handlers, auth, signIn, signOut } = NextAuth({
  trustHost: true,
  secret: process.env.AUTH_SECRET,
  session: { strategy: "jwt", maxAge: 8 * 60 * 60 },
  cookies: { sessionToken: sessionCookie },
  providers: [
    Credentials({
      credentials: {
        username: { label: "Tên đăng nhập", type: "text" },
        password: { label: "Mật khẩu", type: "password" },
      },
      async authorize(credentials) {
        const username = String(credentials.username ?? "").trim();
        const password = String(credentials.password ?? "");
        if (!username || !password) return null;
        const response = await fetch(`${process.env.GATEWAY_URL}/auth/login`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ username, password }),
          cache: "no-store",
          signal: AbortSignal.timeout(6000),
        });
        if (!response.ok) return null;
        const result = (await response.json()) as LoginResult;
        if (result.user.role !== "rider") return null;
        return {
          id: result.user.id,
          name: result.user.name,
          role: result.user.role,
          accessToken: result.accessToken,
          expiresAt: result.expiresAt,
        };
      },
    }),
  ],
  callbacks: {
    jwt({ token, user }) {
      if (user) {
        const authenticated = user as typeof user & {
          role: "rider" | "driver";
          accessToken: string;
          expiresAt: number;
        };
        token.sub = user.id;
        token.name = user.name;
        token.roles = [authenticated.role];
        token.accessToken = authenticated.accessToken;
        token.expiresAt = authenticated.expiresAt;
      }
      if (
        typeof token.expiresAt !== "number" ||
        !Number.isFinite(token.expiresAt) ||
        token.expiresAt <= Date.now() / 1000
      ) {
        return null;
      }
      return token;
    },
    session({ session, token }) {
      session.user.id = token.sub!;
      session.user.roles = (token.roles as string[]) ?? [];
      return session;
    },
  },
});
