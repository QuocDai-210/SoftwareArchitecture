import NextAuth from "next-auth";
import Credentials from "next-auth/providers/credentials";
import { cookies } from "next/headers";
import { sessionCookie } from "./session-cookie";
import { exchange, flowCookie } from "./oidc";

export const { handlers, auth, signIn, signOut } = NextAuth({
  trustHost: true,
  secret: process.env.AUTH_SECRET,
  session: { strategy: "jwt", maxAge: 8 * 60 * 60 },
  cookies: { sessionToken: sessionCookie },
  providers: [
    Credentials({
      credentials: { query: {} },
      async authorize(credentials) {
        if (typeof credentials.query !== "string") return null;
        const flow = (await cookies()).get(flowCookie)?.value;
        return exchange(credentials.query, flow);
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
