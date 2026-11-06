import type { SandboxGateway, SandboxPayment } from "../application/SandboxGateway";
import { SandboxRequestError } from "../application/SandboxGateway";

async function request(path: string, options?: RequestInit): Promise<SandboxPayment> {
  const response = await fetch(`/api/backend/payments/sandbox${path}`, {
    ...options,
    cache: "no-store",
    signal: AbortSignal.timeout(25000),
  });
  const body = await response.json();
  if (!response.ok) {
    throw new SandboxRequestError(
      body.detail ?? "Không thể kiểm tra thanh toán. Vui lòng thử lại.",
      response.status,
    );
  }
  return body;
}

export class HttpSandboxGateway implements SandboxGateway {
  async create(amount: number) {
    // Keep the key across network errors/reloads; only clear after a definite response.
    const storageKey = `swiftride:sandbox:${amount}`;
    const key = sessionStorage.getItem(storageKey) ?? crypto.randomUUID();
    sessionStorage.setItem(storageKey, key);
    const payment = await request("", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Idempotency-Key": key },
      body: JSON.stringify({ amount }),
    });
    sessionStorage.removeItem(storageKey);
    return payment;
  }

  get(reference: string) {
    return request(`/${encodeURIComponent(reference)}`);
  }

  reconcile(reference: string) {
    return request(`/${encodeURIComponent(reference)}/reconcile`, { method: "POST" });
  }
}
