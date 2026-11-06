import { SandboxRequestError, type SandboxPayment } from "../application/SandboxGateway";
import type {
  WalletGateway,
  Wallet,
  WalletActivity,
  WalletPage,
  WalletWithdrawal,
  WalletWithdrawalRequest,
} from "../application/WalletGateway";

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`/api/backend/wallets/${path}`, {
    ...options,
    cache: "no-store",
    signal: AbortSignal.timeout(25000),
  });
  const body = await response.json();
  if (!response.ok) {
    throw new SandboxRequestError(
      body.detail ?? "Không thể tải ví. Vui lòng thử lại.",
      response.status,
    );
  }
  return body;
}

export class HttpWalletGateway implements WalletGateway {
  balance() {
    return request<Wallet>("me");
  }
  history(page: number) {
    return request<WalletPage<WalletActivity>>(`history?page=${page}`);
  }
  topUps(page: number) {
    return request<WalletPage<SandboxPayment>>(`topups?page=${page}`);
  }

  async topUp(amount: number) {
    const storageKey = `swiftride:wallet-topup:${amount}`;
    const key = sessionStorage.getItem(storageKey) ?? crypto.randomUUID();
    sessionStorage.setItem(storageKey, key);
    const payment = await request<SandboxPayment>("topups", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Idempotency-Key": key },
      body: JSON.stringify({ amount }),
    });
    sessionStorage.removeItem(storageKey);
    return payment;
  }

  withdrawals(page: number) {
    return request<WalletPage<WalletWithdrawal>>(`withdrawals?page=${page}`);
  }

  async withdraw(payload: WalletWithdrawalRequest) {
    const serialized = JSON.stringify(payload);
    const storageKey = `swiftride:wallet-withdrawal:${serialized}`;
    const key = sessionStorage.getItem(storageKey) ?? crypto.randomUUID();
    sessionStorage.setItem(storageKey, key);
    const withdrawal = await request<WalletWithdrawal>("withdrawals", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Idempotency-Key": key },
      body: serialized,
    });
    sessionStorage.removeItem(storageKey);
    return withdrawal;
  }
}
