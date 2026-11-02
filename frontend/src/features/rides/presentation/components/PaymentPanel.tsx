"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import type { RideService } from "../../application/RideService";
import type { Page, Payment } from "../../domain/models";
import { money } from "../format";

export function PaymentPanel({ service, run, busy, isOps, paymentReference }: {
  service: RideService;
  run: (action: () => Promise<void>) => Promise<void>;
  busy: boolean;
  isOps: boolean;
  paymentReference?: string;
}) {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Page<Payment>>({ items: [], total: 0, page: 1, pageSize: 10 });
  const [amount, setAmount] = useState(100000);
  const [reason, setReason] = useState("");
  const [error, setError] = useState("");
  const topup = useRef<{ amount: number; key: string } | null>(null);
  const refunds = useRef(new Map<string, string>());
  const reload = useCallback(async () => { setData(await service.payments(page)); }, [service, page]);
  useEffect(() => {
    let active = true;
    service.payments(page).then(result => { if (active) setData(result); }).catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [service, page]);
  useEffect(() => {
    if (!paymentReference) return;
    let active = true;
    service.paymentByReference(paymentReference).then(payment => service.reconcile(payment.id)).then(() => {
      if (active) return reload();
    }).catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [service, paymentReference, reload]);
  async function checkout(id: string) {
    const result = await service.checkout(id);
    const url = new URL(result.url);
    if (url.origin !== "https://sandbox.vnpayment.vn") throw new Error("Địa chỉ thanh toán không hợp lệ.");
    window.location.assign(url.href);
  }
  function act(action: () => Promise<void>) { void run(async () => { setError(""); await action(); await reload(); }); }
  return <section id="payments" className="card p-6 mt-6">
    <div className="flex flex-wrap justify-between gap-4"><h2 className="font-bold text-lg">{isOps ? "Đối soát giao dịch" : "Ví và thanh toán"}</h2>
      <button className="text-sm underline" disabled={busy} onClick={() => act(reload)}>Làm mới</button></div>
    {paymentReference && <p className="text-sm my-3" role="status">Đã quay về từ VNPAY. Kết quả dưới đây do backend đối soát với VNPAY.</p>}
    {error && <p role="alert" className="text-red-800 text-sm my-3">{error}</p>}
    {!isOps && <form className="flex flex-wrap gap-3 items-end my-5" onSubmit={event => {
      event.preventDefault();
      act(async () => {
        if (topup.current?.amount !== amount) topup.current = { amount, key: crypto.randomUUID() };
        const payment = await service.topUp(amount, topup.current.key);
        topup.current = null;
        await reload();
        await checkout(payment.id);
      });
    }}>
      <label className="text-sm">Số tiền nạp (VND)<input className="field mt-2" type="number" min={10000} max={5000000} step={1} required value={amount} onChange={e => setAmount(Number(e.target.value))} /></label>
      <button className="btn" disabled={busy}>Nạp ví qua VNPAY sandbox</button>
    </form>}
    {isOps && <label className="block text-sm my-4">Lý do hoàn toàn bộ tiền chuyến<input className="field mt-2" value={reason} maxLength={200} onChange={e => setReason(e.target.value)} /></label>}
    <div className="overflow-x-auto"><table className="w-full text-sm text-left"><caption className="sr-only">Lịch sử giao dịch</caption>
      <thead><tr><th className="p-3">Giao dịch</th><th className="p-3">Số tiền</th><th className="p-3">Trạng thái</th><th className="p-3">Thao tác</th></tr></thead>
      <tbody>{data.items.map(payment => <tr key={payment.id} className="border-t border-[#e4e8e0]">
        <td className="p-3">{payment.isTopUp ? "Nạp ví" : "Cước xe"} · {payment.id.slice(0, 8)}<p className="text-xs">{new Date(payment.createdAt).toLocaleString("vi-VN")}</p></td>
        <td className="p-3 whitespace-nowrap">{money(payment.amount)}</td><td className="p-3">{payment.status}{payment.reason && <p className="text-xs">{payment.reason}</p>}</td>
        <td className="p-3 flex flex-wrap gap-2">
          {payment.requiresCheckout && <button className="btn" disabled={busy} onClick={() => act(() => checkout(payment.id))}>Thanh toán</button>}
          {["Pending", "RefundPending"].includes(payment.status) && <button className="btn btn-secondary" disabled={busy} onClick={() => act(async () => { await service.reconcile(payment.id); })}>Đối soát</button>}
          {isOps && !payment.isTopUp && payment.status === "Settled" && <button className="btn btn-secondary" disabled={busy || !reason.trim()} onClick={() => act(async () => {
            const payload = payment.id + "|" + reason.trim();
            const key = refunds.current.get(payload) ?? crypto.randomUUID();
            refunds.current.set(payload, key);
            await service.refund(payment.id, reason.trim(), key);
          })}>Hoàn toàn bộ</button>}
        </td>
      </tr>)}</tbody>
    </table></div>
    {!data.items.length && <p className="text-sm my-4">Chưa có giao dịch.</p>}
    <div className="flex justify-between items-center gap-3 mt-4 text-sm">
      <button className="btn btn-secondary" disabled={busy || page <= 1} onClick={() => setPage(page - 1)}>Trang trước</button>
      <span>Trang {page} · {data.total} giao dịch</span>
      <button className="btn btn-secondary" disabled={busy || page * data.pageSize >= data.total} onClick={() => setPage(page + 1)}>Trang sau</button>
    </div>
  </section>;
}
