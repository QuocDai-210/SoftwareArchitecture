"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import type { RideService } from "../../application/RideService";
import type { Page, Payment, PaymentStatistic, Journal } from "../../domain/models";
import { money } from "../format";

const statuses: Record<string, string> = {
  Pending: "Chờ thu",
  Settled: "Đã thu",
  Refunded: "Đã hoàn",
  RefundPending: "Chờ hoàn",
  Declined: "Chưa thanh toán",
  Compensated: "Đã bồi hoàn",
};
export function PaymentPanel({
  service,
  run,
  busy,
  isOps,
  isDriver,
  onTrip,
}: {
  service: RideService;
  run: (action: () => Promise<void>) => Promise<void>;
  busy: boolean;
  isOps: boolean;
  isDriver: boolean;
  onTrip: (id: string) => void;
}) {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Page<Payment>>({ items: [], total: 0, page: 1, pageSize: 10 });
  const [statistics, setStatistics] = useState<PaymentStatistic[]>([]);
  const [selected, setSelected] = useState<Payment | null>(null);
  const [action, setAction] = useState<"collect" | "refund">("collect");
  const [reason, setReason] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [error, setError] = useState("");
  const [journals, setJournals] = useState<{
    id: string;
    payment: Payment;
    items: Journal[];
  } | null>(null);
  const actionForm = useRef<HTMLFormElement>(null);
  const receipt = useRef<HTMLElement>(null);
  useEffect(() => {
    const target = selected ? actionForm.current : journals ? receipt.current : null;
    target?.scrollIntoView({ block: "center" });
    target?.querySelector<HTMLElement>("input,button")?.focus();
  }, [selected, journals]);
  const refunds = useRef(new Map<string, string>());
  const reload = useCallback(async () => {
    const [payments, stats] = await Promise.all([
      service.payments(page),
      service.paymentStatistics(),
    ]);
    setData(payments);
    setStatistics(stats);
  }, [service, page]);
  useEffect(() => {
    let mounted = true;
    Promise.all([service.payments(page), service.paymentStatistics()])
      .then(([payments, stats]) => {
        if (mounted) {
          setData(payments);
          setStatistics(stats);
        }
      })
      .catch((e) => {
        if (mounted) setError(e.message);
      });
    return () => {
      mounted = false;
    };
  }, [service, page]);
  function choose(payment: Payment, next: "collect" | "refund") {
    setSelected(payment);
    setAction(next);
    setReason("");
    setConfirmed(false);
  }
  const amount = (status: string) =>
    statistics
      .filter((s) => s.method === "Cod" && s.status === status)
      .reduce((total, s) => total + s.amount, 0);
  return (
    <section id="payments" className="space-y-5">
      {!isDriver && !isOps && (
        <a href="/#wallet" className="btn btn-secondary">
          Mở ví và nạp tiền
        </a>
      )}
      <div className="stats-grid">
        {[
          {
            label: isDriver || isOps ? "Đã thu, chưa hoàn" : "Đã thanh toán",
            value: amount("Settled"),
          },
          { label: "Chờ thu tiền mặt", value: amount("Pending") },
          { label: "Đã hoàn tiền", value: amount("Refunded") },
        ].map((stat) => (
          <div className="card p-5" key={stat.label}>
            <p className="muted text-sm">{stat.label}</p>
            <p className="text-2xl font-bold mt-3">{money(stat.value)}</p>
          </div>
        ))}
      </div>
      {error && (
        <p role="alert" className="app-alert">
          {error}
        </p>
      )}
      <div className="card p-5 sm:p-6">
        <div className="flex justify-between gap-3 items-center mb-5">
          <h2 className="text-lg font-bold">{isOps ? "Đối soát tiền mặt" : "Lịch sử tiền mặt"}</h2>
          <button className="btn btn-secondary" disabled={busy} onClick={() => void run(reload)}>
            Làm mới
          </button>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-sm text-left">
            <caption className="sr-only">Lịch sử thu và hoàn tiền</caption>
            <thead>
              <tr>
                <th className="p-3">Chuyến đi</th>
                <th className="p-3">Số tiền</th>
                <th className="p-3">Trạng thái</th>
                <th className="p-3">Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((payment) => (
                <tr key={payment.id} className="border-t border-slate-100">
                  <td className="p-3">
                    <a
                      className="font-bold underline"
                      href={`#ride/${payment.id}`}
                      onClick={() => onTrip(payment.id)}
                    >
                      {payment.id.slice(0, 8)}
                    </a>
                    <p className="muted text-xs mt-1">
                      {new Date(payment.createdAt).toLocaleString("vi-VN")}
                    </p>
                  </td>
                  <td className="p-3 whitespace-nowrap font-semibold">{money(payment.amount)}</td>
                  <td className="p-3">
                    <span className="badge">
                      {payment.method === "Cod"
                        ? (statuses[payment.status] ?? payment.status)
                        : "Giao dịch cũ · " + payment.method}
                    </span>
                    {payment.method !== "Cod" &&
                      ["Pending", "RefundPending", "Declined"].includes(payment.status) && (
                        <p className="muted mt-2">Cần Ops đối soát ngoài hệ thống.</p>
                      )}
                    {payment.collectedAt && (
                      <p className="muted text-xs mt-2">
                        Thu: {new Date(payment.collectedAt).toLocaleString("vi-VN")}
                      </p>
                    )}
                    {payment.collectionReason && (
                      <p className="text-xs mt-1">{payment.collectionReason}</p>
                    )}
                    {payment.refundedAt && (
                      <p className="muted text-xs mt-2">
                        Hoàn: {new Date(payment.refundedAt).toLocaleString("vi-VN")}
                      </p>
                    )}
                    {payment.reason && <p className="text-xs mt-1">{payment.reason}</p>}
                  </td>
                  <td className="p-3">
                    <div className="flex flex-wrap gap-2">
                      {payment.method === "Cod" &&
                        payment.status === "Pending" &&
                        (isOps || isDriver) && (
                          <button
                            className="btn"
                            disabled={busy}
                            onClick={() => choose(payment, "collect")}
                          >
                            Xác nhận thu
                          </button>
                        )}
                      {isOps && payment.method === "Cod" && payment.status === "Settled" && (
                        <button
                          className="btn btn-secondary"
                          disabled={busy}
                          onClick={() => choose(payment, "refund")}
                        >
                          Hoàn tiền
                        </button>
                      )}
                      <button
                        className="btn btn-secondary"
                        disabled={busy}
                        onClick={() =>
                          void run(async () => {
                            setJournals({
                              id: payment.id,
                              payment,
                              items: await service.ledger(payment.id),
                            });
                          })
                        }
                      >
                        Biên nhận
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!data.items.length && (
          <div className="empty-state">
            <h3>Chưa có giao dịch</h3>
            <p>Khoản thu sẽ xuất hiện sau khi trả khách.</p>
          </div>
        )}
        <div className="pagination">
          <button
            className="btn btn-secondary"
            disabled={busy || page <= 1}
            onClick={() => setPage(page - 1)}
          >
            Trang trước
          </button>
          <span>
            Trang {page} · {data.total} giao dịch
          </span>
          <button
            className="btn btn-secondary"
            disabled={busy || page * data.pageSize >= data.total}
            onClick={() => setPage(page + 1)}
          >
            Trang sau
          </button>
        </div>
      </div>
      {selected && (
        <form
          ref={actionForm}
          className="card p-6 space-y-4"
          aria-label="Xác nhận giao dịch tiền mặt"
          onSubmit={(event) => {
            event.preventDefault();
            void run(async () => {
              if (action === "collect")
                await service.confirmCash(selected.id, reason.trim() || undefined);
              else {
                const payload = `${selected.id}|${reason.trim()}`;
                const key = refunds.current.get(payload) ?? crypto.randomUUID();
                refunds.current.set(payload, key);
                await service.refund(selected.id, reason.trim(), key);
              }
              setSelected(null);
              setJournals(null);
              await reload();
            });
          }}
        >
          <h2 className="text-xl font-bold">
            {action === "refund" ? "Ghi nhận hoàn tiền mặt" : "Xác nhận đã thu tiền"} ·{" "}
            {money(selected.amount)}
          </h2>
          {(isOps || action === "refund") && (
            <label className="block text-sm">
              Lý do xử lý
              <input
                className="field mt-2"
                required
                maxLength={200}
                value={reason}
                onChange={(event) => setReason(event.target.value)}
              />
            </label>
          )}
          <label className="check-label">
            <input
              type="checkbox"
              required
              checked={confirmed}
              onChange={(event) => setConfirmed(event.target.checked)}
            />
            {action === "refund"
              ? "Tôi xác nhận tiền mặt đã được trả đủ cho khách."
              : "Tôi xác nhận đã kiểm tra và nhận đủ tiền mặt."}
          </label>
          <div className="flex gap-3">
            <button className="btn" disabled={busy || !confirmed}>
              {action === "refund" ? "Ghi nhận đã hoàn tiền" : "Xác nhận đã thu tiền"}
            </button>
            <button
              type="button"
              className="btn btn-secondary"
              disabled={busy}
              onClick={() => setSelected(null)}
            >
              Đóng
            </button>
          </div>
        </form>
      )}
      {journals && (
        <section ref={receipt} className="card p-6 space-y-3" aria-label="Biên nhận thanh toán">
          <div className="flex justify-between">
            <h2 className="font-bold">Biên nhận · {journals.id.slice(0, 8)}</h2>
            <button className="btn btn-secondary" onClick={() => setJournals(null)}>
              Đóng
            </button>
          </div>
          <p className="text-2xl font-bold">{money(journals.payment.amount)}</p>
          <p className="badge">
            {journals.payment.method === "Cod" ? "Tiền mặt" : journals.payment.method} ·{" "}
            {statuses[journals.payment.status] ?? journals.payment.status}
          </p>
          {journals.payment.collectedAt && (
            <p className="text-sm muted">
              Xác nhận thu: {new Date(journals.payment.collectedAt).toLocaleString("vi-VN")}
            </p>
          )}
          {isOps && journals.payment.collectedBy && (
            <p className="text-xs break-all muted">
              Người xác nhận: {journals.payment.collectedBy}
            </p>
          )}
          {journals.payment.refundedAt && (
            <p className="text-sm muted">
              Hoàn tiền: {new Date(journals.payment.refundedAt).toLocaleString("vi-VN")} ·{" "}
              {journals.payment.reason}
            </p>
          )}
          {!journals.items.length && <p>Chưa ghi nhận thu tiền.</p>}
          {journals.items.map((journal) => (
            <div key={journal.id}>
              <h3 className="font-semibold">
                {journal.kind === "Charge" ? "Thu tiền" : "Hoàn tiền"} ·{" "}
                {new Date(journal.createdAt).toLocaleString("vi-VN")}
              </h3>
              {!isOps && <p className="mt-2 font-semibold">{money(journals.payment.amount)}</p>}
              {isOps && (
                <dl className="space-y-2 mt-3">
                  {journal.lines.map((line, index) => (
                    <div key={index} className="flex justify-between gap-3 text-sm">
                      <dt className="break-all">
                        {line.side === "Debit" ? "Nợ" : "Có"} · {line.account}
                      </dt>
                      <dd className="whitespace-nowrap">{money(line.amount)}</dd>
                    </div>
                  ))}
                </dl>
              )}
            </div>
          ))}
        </section>
      )}
    </section>
  );
}
