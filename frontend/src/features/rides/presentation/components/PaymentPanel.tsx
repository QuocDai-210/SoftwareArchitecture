"use client";
import { useCallback, useEffect, useState } from "react";
import type { RideService } from "../../application/RideService";
import type { Journal, Page, Payment, PaymentStatistic } from "../../domain/models";
import { money } from "../format";

const statuses: Record<string, string> = {
  Pending: "Chờ thanh toán",
  Settled: "Đã thanh toán",
  Refunded: "Đã hoàn tiền",
  RefundPending: "Chờ hoàn tiền",
  Declined: "Thất bại",
  Compensated: "Đã bồi hoàn",
};
const journalLabels: Record<string, string> = {
  Charge: "Thu tiền",
  Refund: "Hoàn tiền",
  TripHold: "Giữ tiền cho chuyến",
  TripHoldReleased: "Trả khoản giữ",
  TripSettled: "Thanh toán ví",
  DriverEarning: "Thu nhập tài xế",
};

type Receipt = { payment: Payment; journals: Journal[] };

export function PaymentPanel({
  service,
  run,
  busy,
  onTrip,
}: {
  service: RideService;
  run: (action: () => Promise<void>) => Promise<void>;
  busy: boolean;
  onTrip: (id: string) => void;
}) {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Page<Payment>>({ items: [], total: 0, page: 1, pageSize: 10 });
  const [statistics, setStatistics] = useState<PaymentStatistic[]>([]);
  const [receipt, setReceipt] = useState<Receipt | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const reload = useCallback(async () => {
    const [payments, stats] = await Promise.all([
      service.payments(page),
      service.paymentStatistics(),
    ]);
    setData(payments);
    setStatistics(stats);
    setError("");
  }, [service, page]);
  useEffect(() => {
    let mounted = true;
    setLoading(true);
    Promise.all([service.payments(page), service.paymentStatistics()])
      .then(([payments, stats]) => {
        if (mounted) {
          setData(payments);
          setStatistics(stats);
          setError("");
        }
      })
      .catch((cause) => {
        if (mounted) setError((cause as Error).message);
      })
      .finally(() => {
        if (mounted) setLoading(false);
      });
    return () => {
      mounted = false;
    };
  }, [service, page]);
  const amount = (status: string) =>
    statistics
      .filter((item) => item.status === status)
      .reduce((total, item) => total + item.amount, 0);
  return (
    <section id="payments" className="space-y-5">
      <a href="/#wallet" className="btn btn-secondary">
        Mở ví SwiftRide
      </a>
      <div className="stats-grid">
        {[
          { label: "Đã thanh toán", value: amount("Settled") },
          { label: "Chờ thanh toán", value: amount("Pending") },
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
        <div className="panel-toolbar mb-5">
          <h2 className="text-lg font-bold">Lịch sử thanh toán</h2>
          <button
            className="btn btn-secondary"
            disabled={busy || loading}
            onClick={() => void run(reload)}
          >
            Làm mới
          </button>
        </div>
        <div className="overflow-x-auto">
          <table className="responsive-table w-full text-sm text-left">
            <caption className="sr-only">Lịch sử thanh toán tiền mặt và ví</caption>
            <thead>
              <tr>
                <th className="p-3">Chuyến đi</th>
                <th className="p-3">Số tiền</th>
                <th className="p-3">Trạng thái</th>
                <th className="p-3">Biên nhận</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((payment) => (
                <tr key={payment.id} className="border-t border-slate-100">
                  <td className="p-3" data-label="Chuyến đi">
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
                  <td className="p-3 whitespace-nowrap font-semibold" data-label="Số tiền">
                    {money(payment.amount)}
                  </td>
                  <td className="p-3" data-label="Trạng thái">
                    <span className="badge">{statuses[payment.status] ?? payment.status}</span>
                    <p className="muted text-xs mt-2">
                      {payment.method === "Cod" ? "Tiền mặt mô phỏng" : "Ví SwiftRide"}
                    </p>
                    {payment.collectedAt && (
                      <p className="muted text-xs mt-2">
                        Thu: {new Date(payment.collectedAt).toLocaleString("vi-VN")}
                      </p>
                    )}
                  </td>
                  <td className="p-3" data-label="Biên nhận">
                    <button
                      className="btn btn-secondary"
                      disabled={busy}
                      onClick={() =>
                        void run(async () => {
                          setReceipt({ payment, journals: await service.ledger(payment.id) });
                        })
                      }
                    >
                      Xem biên nhận
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {loading && (
          <p role="status" className="muted py-5">
            Đang tải thanh toán…
          </p>
        )}
        {!loading && !error && !data.items.length && (
          <div className="empty-state">
            <h3>Chưa có giao dịch</h3>
            <p>Lịch sử thanh toán sẽ xuất hiện sau khi hoàn tất chuyến.</p>
          </div>
        )}
        <div className="pagination">
          <button
            className="btn btn-secondary"
            disabled={busy || loading || page <= 1}
            onClick={() => setPage(page - 1)}
          >
            Trang trước
          </button>
          <span>
            Trang {page} · {data.total} giao dịch
          </span>
          <button
            className="btn btn-secondary"
            disabled={busy || loading || page * data.pageSize >= data.total}
            onClick={() => setPage(page + 1)}
          >
            Trang sau
          </button>
        </div>
      </div>
      {receipt && (
        <section className="card p-6 space-y-3" aria-label="Biên nhận thanh toán">
          <div className="flex justify-between">
            <h2 className="font-bold">Biên nhận · {receipt.payment.id.slice(0, 8)}</h2>
            <button className="btn btn-secondary" onClick={() => setReceipt(null)}>
              Đóng
            </button>
          </div>
          <p className="text-2xl font-bold">{money(receipt.payment.amount)}</p>
          <p className="badge">{statuses[receipt.payment.status] ?? receipt.payment.status}</p>
          {!receipt.journals.length && <p>Chưa ghi nhận thu tiền.</p>}
          {receipt.journals.map((journal) => (
            <div key={journal.id}>
              <h3 className="font-semibold">
                {journalLabels[journal.kind] ?? journal.kind} ·{" "}
                {new Date(journal.createdAt).toLocaleString("vi-VN")}
              </h3>
              <p className="mt-2 font-semibold">{money(receipt.payment.amount)}</p>
            </div>
          ))}
        </section>
      )}
    </section>
  );
}
