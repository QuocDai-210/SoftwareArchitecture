import { NextRequest, NextResponse } from "next/server";
import { readBackendToken } from "../auth/backend-token";
import { sessionCookie } from "../auth/session-cookie";

export async function proxy(
  request: NextRequest,
  { params }: { params: Promise<{ path: string[] }> },
) {
  const { path } = await params;
  if (
    !path.length ||
    ![
      "trips",
      "drivers",
      "payments",
      "wallets",
      "quote",
      "screens",
      "maps",
      "profiles",
      "ops",
      "simulations",
    ].includes(path[0]) ||
    path.some((p) => !/^[a-zA-Z0-9-]+$/.test(p))
  )
    return NextResponse.json({ detail: "Đường dẫn không hợp lệ." }, { status: 400 });
  if (!["GET", "POST", "PUT"].includes(request.method))
    return new NextResponse(null, { status: 405 });
  if (
    request.method !== "GET" &&
    request.headers.get("origin") !== new URL(process.env.AUTH_URL!).origin
  )
    return NextResponse.json({ detail: "Origin không hợp lệ." }, { status: 403 });
  let session;
  try {
    session = await readBackendToken(request);
  } catch {
    return NextResponse.json({ detail: "Phiên đã hết hạn. Hãy đăng nhập lại." }, { status: 401 });
  }
  const headers = new Headers({
    Authorization: `Bearer ${session.token}`,
    "Content-Type": "application/json",
    CorrelationId: crypto.randomUUID(),
  });
  const key = request.headers.get("Idempotency-Key");
  if (key) headers.set("Idempotency-Key", key);
  try {
    if (Number(request.headers.get("content-length") ?? 0) > 16384)
      return new NextResponse(null, { status: 413 });
    const body = request.method === "GET" ? undefined : await request.text();
    if (body && Buffer.byteLength(body) > 16384) return new NextResponse(null, { status: 413 });
    const upstream = await fetch(
      `${process.env.GATEWAY_URL}/${path.join("/")}${request.nextUrl.search}`,
      {
        method: request.method,
        headers,
        body,
        cache: "no-store",
        signal: AbortSignal.timeout(25000),
      },
    );
    const response = new NextResponse(upstream.body, {
      status: upstream.status,
      headers: {
        "Content-Type": upstream.headers.get("Content-Type") ?? "application/json",
        "Cache-Control": "no-store",
        CorrelationId: upstream.headers.get("CorrelationId") ?? "",
      },
    });
    if (session.cookie) {
      // Auth.js tokens can exceed one cookie; use matching chunk convention.
      for (const c of request.cookies
        .getAll()
        .filter(
          (c) => c.name === sessionCookie.name || c.name.startsWith(`${sessionCookie.name}.`),
        ))
        response.cookies.set(c.name, "", { ...sessionCookie.options, maxAge: 0 });
      const chunks = session.cookie.match(/.{1,3800}/g)!;
      chunks.forEach((value, i) =>
        response.cookies.set(
          chunks.length === 1 ? sessionCookie.name : `${sessionCookie.name}.${i}`,
          value,
          { ...sessionCookie.options, maxAge: 30 * 24 * 3600 },
        ),
      );
    }
    return response;
  } catch {
    return NextResponse.json(
      { detail: "Không kết nối được Gateway. Thử lại với cùng yêu cầu." },
      { status: 503 },
    );
  }
}
