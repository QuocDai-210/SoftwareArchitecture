import type {
  WalletPage,
  WalletWithdrawal,
  WalletWithdrawalOpsGateway,
} from "../application/WalletGateway";

async function request<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(`/api/backend/ops/wallet-withdrawals${path}`, {
    method: body === undefined ? "GET" : "POST",
    headers: { "Content-Type": "application/json" },
    cache: "no-store",
    signal: AbortSignal.timeout(25000),
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const content = await response.text();
  let data: { detail?: string } | T | null = null;
  try {
    data = content ? JSON.parse(content) : null;
  } catch {
    if (!response.ok) {
      throw new Error(`Yêu cầu lỗi (${response.status}). Vui lòng thử lại.`);
    }
    throw new Error("Máy chủ trả dữ liệu không hợp lệ.");
  }
  if (!response.ok) {
    const detail = data ? (data as { detail?: unknown }).detail : null;
    if (typeof detail === "string") throw new Error(detail);
    throw new Error(`Yêu cầu lỗi (${response.status}). Vui lòng thử lại.`);
  }
  if (data === null) throw new Error("Máy chủ trả phản hồi rỗng.");
  return data as T;
}

export class HttpWalletWithdrawalOpsGateway implements WalletWithdrawalOpsGateway {
  pending(page: number) {
    return request<WalletPage<WalletWithdrawal>>(`?page=${page}`);
  }

  decide(id: string, approve: boolean, reason: string) {
    return request<WalletWithdrawal>(`/${id}/decision`, { approve, reason: reason || null });
  }
}
