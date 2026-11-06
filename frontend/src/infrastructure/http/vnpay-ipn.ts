import { NextRequest, NextResponse } from "next/server";

// Public, fixed upstream route. Authentication is the provider signature verified by Payment Service.
export async function receiveVnpayIpn(request: NextRequest) {
  const retry = () =>
    NextResponse.json(
      { RspCode: "99", Message: "Please retry" },
      { headers: { "Cache-Control": "no-store" } },
    );
  if (request.nextUrl.search.length > 8192) {
    return NextResponse.json({ RspCode: "97", Message: "Invalid payload" });
  }
  try {
    const response = await fetch(
      `${process.env.GATEWAY_URL}/payments/vnpay/ipn${request.nextUrl.search}`,
      { cache: "no-store", redirect: "error", signal: AbortSignal.timeout(15000) },
    );
    if (!response.ok) {
      return retry();
    }
    const ack = await response.json();
    if (typeof ack.RspCode !== "string" || typeof ack.Message !== "string") {
      return retry();
    }
    return NextResponse.json(
      { RspCode: ack.RspCode, Message: ack.Message },
      { headers: { "Cache-Control": "no-store" } },
    );
  } catch {
    return retry();
  }
}
