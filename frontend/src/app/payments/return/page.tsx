import { redirect } from "next/navigation";

export default async function PaymentReturn({ searchParams }: { searchParams: Promise<{ vnp_TxnRef?: string }> }) {
  const { vnp_TxnRef } = await searchParams;
  redirect(vnp_TxnRef && /^[a-f0-9]{32}$/.test(vnp_TxnRef) ? `/?paymentReference=${vnp_TxnRef}#payments` : "/#payments");
}
