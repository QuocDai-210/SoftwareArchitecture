import { cookies } from "next/headers";
import { decode, encode } from "next-auth/jwt";
import * as oauth from "oauth4webapi";

const issuer = process.env.OIDC_ISSUER!;
const backchannel = process.env.OIDC_BACKCHANNEL ?? issuer;
const endpoints = {
  issuer,
  authorization_endpoint: issuer + "/protocol/openid-connect/auth",
  jwks_uri: backchannel + "/protocol/openid-connect/certs",
  token_endpoint: backchannel + "/protocol/openid-connect/token",
};
const client = { client_id: "swiftride-postman" };
const callback = () => new URL("/callback", process.env.AUTH_URL).href;
const cookie = "swiftride.oidc";
const seal = () => ({ secret: process.env.AUTH_SECRET!, salt: cookie });

export async function authorizationUrl() {
  const verifier = oauth.generateRandomCodeVerifier();
  const state = oauth.generateRandomState();
  const nonce = oauth.generateRandomNonce();
  (await cookies()).set(
    cookie,
    await encode({ ...seal(), token: { verifier, state, nonce }, maxAge: 600 }),
    {
      httpOnly: true,
      sameSite: "lax",
      secure: callback().startsWith("https:"),
      path: "/",
      maxAge: 600,
    },
  );
  const url = new URL(endpoints.authorization_endpoint);
  url.search = new URLSearchParams({
    client_id: client.client_id,
    redirect_uri: callback(),
    response_type: "code",
    scope: "openid profile",
    prompt: "login",
    state,
    nonce,
    code_challenge_method: "S256",
    code_challenge: await oauth.calculatePKCECodeChallenge(verifier),
  }).toString();
  return url;
}

export async function exchange(query: string, sealed: string | undefined) {
  if (!sealed) return null;
  const flow = await decode({ ...seal(), token: sealed });
  if (
    typeof flow?.state !== "string" ||
    typeof flow.verifier !== "string" ||
    typeof flow.nonce !== "string"
  )
    return null;
  const params = oauth.validateAuthResponse(
    endpoints,
    client,
    new URLSearchParams(query),
    flow.state,
  );
  const transport = {
    [oauth.allowInsecureRequests]: issuer === "http://localhost:8180/realms/swiftride",
    signal: AbortSignal.timeout(5000),
  };
  const response = await oauth.authorizationCodeGrantRequest(
    endpoints,
    client,
    oauth.None(),
    params,
    callback(),
    flow.verifier,
    transport,
  );
  const tokens = await oauth.processAuthorizationCodeResponse(endpoints, client, response, {
    expectedNonce: flow.nonce,
    requireIdToken: true,
  });
  await oauth.validateApplicationLevelSignature(endpoints, response, transport);
  const claims = oauth.getValidatedIdTokenClaims(tokens);
  const roles = Array.isArray(claims?.role) ? claims.role : [claims?.role];
  const role = roles.find((r) => r === "driver" || r === "rider");
  if (!claims || !role || !tokens.expires_in) return null;
  return {
    id: claims.sub,
    name: String(claims.preferred_username ?? claims.sub),
    role,
    accessToken: tokens.access_token,
    expiresAt: Date.now() / 1000 + tokens.expires_in,
  };
}

export const flowCookie = cookie;
