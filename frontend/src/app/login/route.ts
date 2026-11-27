import { NextResponse } from "next/server";
import { authorizationUrl } from "@/infrastructure/auth/oidc";

export async function GET() {
  return NextResponse.redirect(await authorizationUrl());
}
