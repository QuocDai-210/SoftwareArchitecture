export type Wallet = {
  available: number;
  held: number;
  pendingEarnings: number;
  currency: string;
  canTopUp: boolean;
  canWithdraw: boolean;
};

export type WalletWithdrawal = {
  id: string;
  ownerId: string;
  amount: number;
  state: "Pending" | "Completed" | "Rejected";
  requestedAt: string;
  reviewedAt: string | null;
  reviewedBy: string | null;
  decisionReason: string | null;
};

export type WalletWithdrawalRequest = { amount: number };

export type WalletActivity = {
  reference: string;
  kind: string;
  amount: number;
  availableAfter: number;
  heldAfter: number;
  pendingAfter: number;
  createdAt: string;
};

export type WalletPage<T> = { items: T[]; page: number; pageSize: number; total: number };

export interface WalletGateway {
  balance(): Promise<Wallet>;
  history(page: number): Promise<WalletPage<WalletActivity>>;
  topUp(amount: number): Promise<WalletActivity>;
  withdrawals(page: number): Promise<WalletPage<WalletWithdrawal>>;
  withdraw(request: WalletWithdrawalRequest): Promise<WalletWithdrawal>;
}
