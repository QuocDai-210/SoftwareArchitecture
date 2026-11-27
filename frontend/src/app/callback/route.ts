import { AuthError } from "next-auth";
import { cookies } from "next/headers";
import { NextRequest, NextResponse } from "next/server";
import { signIn } from "@/infrastructure/auth/auth";
import { flowCookie } from "@/infrastructure/auth/oidc";

export async function GET(request: NextRequest) {
  const jar = await cookies();
  const flow = jar.get(flowCookie)?.value;
  if (flow && !request.nextUrl.searchParams.has("error")) {
    try {
      await signIn("credentials", { query: request.nextUrl.search, redirectTo: "/" });
    } catch (error) {
      if (!(error instanceof AuthError)) throw error;
    } finally {
      jar.delete(flowCookie);
    }
  }
  return NextResponse.redirect(new URL("/?login=failed", process.env.AUTH_URL));
}
