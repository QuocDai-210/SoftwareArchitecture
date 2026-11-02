export const cookieName = process.env.AUTH_URL?.startsWith("https:")
  ? "__Secure-authjs.session-token"
  : "authjs.session-token";
export const sessionCookie = {
  name: cookieName,
  options: {
    httpOnly: true,
    sameSite: "lax" as const,
    path: "/",
    secure: cookieName.startsWith("__Secure-"),
  },
};
