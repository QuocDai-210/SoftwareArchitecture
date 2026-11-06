"use client";

import { useState } from "react";
import { HttpWalletWithdrawalOpsGateway } from "@/features/payments/infrastructure/HttpWalletWithdrawalOpsGateway";
import { WalletWithdrawalOps } from "@/features/payments/presentation/WalletWithdrawalOps";

export default function WalletWithdrawalOpsClient() {
  const [gateway] = useState(() => new HttpWalletWithdrawalOpsGateway());
  return <WalletWithdrawalOps gateway={gateway} />;
}
