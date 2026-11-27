import { NextRequest, NextResponse } from "next/server";
import { readBackendToken } from "../auth/backend-token";

export async function proxy(
  request: NextRequest,
  { params }: { params: Promise<{ path: string[] }> },
) {
  const { path } = await params;
  if (
    !path.length ||
    !["trips", "payments", "wallets", "screens", "drivers"].includes(path[0]) ||
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
  if (!session.roles.some((role) => role === "rider" || role === "driver"))
    return NextResponse.json({ detail: "Cần tài khoản Rider hoặc Driver." }, { status: 403 });
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
    return response;
  } catch {
    return NextResponse.json(
      { detail: "Không kết nối được Gateway. Thử lại với cùng yêu cầu." },
      { status: 503 },
    );
  }
}
