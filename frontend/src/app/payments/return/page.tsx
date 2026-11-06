import { redirect } from "next/navigation";

export default async function PaymentReturn({
  searchParams,
}: {
  searchParams: Promise<{ vnp_TxnRef?: string }>;
}) {
  const params = await searchParams;
  const reference = params.vnp_TxnRef;
  // The reference only locates an owner-authorized record; no return field changes its state.
  if (typeof reference === "string" && /^[a-f0-9]{32}$/.test(reference)) {
    redirect(`/payments/sandbox?reference=${reference}`);
  }
  redirect("/payments/sandbox");
}
