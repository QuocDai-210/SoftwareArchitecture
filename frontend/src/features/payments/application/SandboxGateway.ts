export type SandboxPayment = {
  reference: string;
  amount: number;
  state: "Pending" | "Succeeded" | "Failed" | "NeedsReview";
  createdAt: string;
  expiresAt: string;
  confirmedAt: string | null;
  checkoutUrl?: string | null;
  nextQueryAt?: string | null;
  purpose?: "Sandbox" | "WalletTopUp";
  walletCreditedAt?: string | null;
};

export class SandboxRequestError extends Error {
  constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message);
  }
}

export interface SandboxGateway {
  create(amount: number): Promise<SandboxPayment>;
  get(reference: string): Promise<SandboxPayment>;
  reconcile(reference: string): Promise<SandboxPayment>;
}
