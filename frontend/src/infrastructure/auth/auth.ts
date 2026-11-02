import NextAuth from "next-auth";
import Keycloak from "next-auth/providers/keycloak";
import { sessionCookie } from "./session-cookie";
const publicIssuer = `${process.env.AUTH_PUBLIC_URL}/realms/swiftride`;
const internalIssuer = `${process.env.AUTH_INTERNAL_URL}/realms/swiftride`;
export const { handlers, auth, signIn, signOut } = NextAuth({
  trustHost: true,
  secret: process.env.AUTH_SECRET,
  cookies: { sessionToken: sessionCookie },
  providers: [
    Keycloak({
      clientId: "swiftride-web",
      clientSecret: process.env.AUTH_KEYCLOAK_SECRET!,
      issuer: publicIssuer,
      wellKnown: `${internalIssuer}/.well-known/openid-configuration`,
      authorization: {
        url: `${publicIssuer}/protocol/openid-connect/auth`,
        params: { scope: "openid profile email" },
      },
      token: `${internalIssuer}/protocol/openid-connect/token`,
      userinfo: `${internalIssuer}/protocol/openid-connect/userinfo`,
      checks: ["pkce", "state"],
    }),
  ],
  callbacks: {
    jwt({ token, account, profile }) {
      if (account) {
        if (typeof profile?.sub !== "string" || !profile.sub)
          throw new Error("Missing OIDC subject");
        token.sub = profile.sub;
        token.accessToken = account.access_token;
        token.refreshToken = account.refresh_token;
        token.expiresAt = account.expires_at ?? 0;
        const realm = (profile as { realm_access?: { roles?: string[] } } | undefined)
          ?.realm_access;
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
