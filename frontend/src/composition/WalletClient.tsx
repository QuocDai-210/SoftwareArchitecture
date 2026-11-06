"use client";

import { HttpWalletGateway } from "@/features/payments/infrastructure/HttpWalletGateway";
import { WalletPanel } from "@/features/payments/presentation/WalletPanel";

const gateway = new HttpWalletGateway();

export default function WalletClient() {
  return <WalletPanel gateway={gateway} />;
}
