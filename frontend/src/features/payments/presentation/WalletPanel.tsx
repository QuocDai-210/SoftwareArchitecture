"use client";

import { useEffect, useRef, useState } from "react";
import type {
  Wallet,
  WalletActivity,
  WalletGateway,
  WalletPage,
  WalletWithdrawal,
} from "../application/WalletGateway";

const money = (amount: number) => `${amount.toLocaleString("vi-VN")} đ`;
const date = (value: string) => new Date(value).toLocaleString("vi-VN");
const withdrawalStatus: Record<WalletWithdrawal["state"], string> = {
  Pending: "Chờ xử lý · đang giữ tiền",
  Completed: "Đã xử lý mô phỏng",
  Rejected: "Đã từ chối · tiền đã trả lại ví",
};
const activityLabel: Record<string, string> = {
  WalletTopUp: "Nạp ví mô phỏng",
  TripHold: "Giữ tiền cho chuyến",
  TripHoldReleased: "Hoàn khoản giữ chuyến",
  TripSettled: "Thanh toán chuyến bằng ví",
  DriverEarning: "Thu nhập chuyến",
  WalletWithdrawalRequested: "Giữ tiền chờ rút",
  WalletWithdrawalPaid: "Rút tiền mô phỏng",
  WalletWithdrawalRejected: "Từ chối rút · trả tiền về ví",
};
const activitySign: Record<string, string> = {
  WalletTopUp: "+",
  TripHold: "−",
  TripHoldReleased: "+",
  TripSettled: "−",
  DriverEarning: "+",
  WalletWithdrawalRequested: "−",
  WalletWithdrawalPaid: "−",
  WalletWithdrawalRejected: "+",
};

export function WalletPanel({ gateway }: { gateway: WalletGateway }) {
  const [wallet, setWallet] = useState<Wallet | null>(null);
  const [history, setHistory] = useState<WalletPage<WalletActivity> | null>(null);
  const [withdrawals, setWithdrawals] = useState<WalletPage<WalletWithdrawal> | null>(null);
  const [page, setPage] = useState(1);
  const [withdrawalPage, setWithdrawalPage] = useState(1);
  const [refresh, setRefresh] = useState(0);
  const [amount, setAmount] = useState(100000);
  const [withdrawAmount, setWithdrawAmount] = useState(10000);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const submitting = useRef(false);

  useEffect(() => {
    let active = true;
    let timer: ReturnType<typeof setTimeout>;
    setLoading(true);
    async function load() {
      try {
        const [balance, activity, payouts] = await Promise.all([
          gateway.balance(),
          gateway.history(page),
          gateway.withdrawals(withdrawalPage),
        ]);
        if (!active) {
          return;
        }
        setWallet(balance);
        setHistory(activity);
        setWithdrawals(payouts);
        setError("");
      } catch (e) {
        if (active) {
          setError(e instanceof Error ? e.message : "Không thể tải ví.");
        }
      } finally {
        if (active) {
          setLoading(false);
          timer = setTimeout(load, 15000);
        }
      }
    }
    void load();
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [gateway, page, withdrawalPage, refresh]);

  async function topUp() {
    if (submitting.current) {
      return;
    }
    if (!Number.isInteger(amount) || amount < 10000 || amount > 5000000) {
      setError("Nhập số tiền nguyên đồng từ 10.000 đến 5.000.000 đ.");
      return;
    }
    submitting.current = true;
    setBusy(true);
    setError("");
    try {
      await gateway.topUp(amount);
      setRefresh((value) => value + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không thể tạo yêu cầu nạp tiền.");
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }

  async function withdraw() {
    if (submitting.current) {
      return;
    }
    if (!Number.isInteger(withdrawAmount) || withdrawAmount < 10000 || withdrawAmount > 5000000) {
      setError("Số tiền rút mỗi lần từ 10.000 đến 5.000.000 đ.");
      return;
    }
    submitting.current = true;
    setBusy(true);
    setError("");
    try {
      await gateway.withdraw({ amount: withdrawAmount });
      setRefresh((value) => value + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không thể tạo yêu cầu rút tiền.");
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }

  return (
    <section className="wallet-panel" aria-label="Ví SwiftRide">
      <div className="flex flex-wrap gap-3 items-center justify-between">
        <p className="badge">Ví mô phỏng · Không sử dụng tiền thật</p>
        <button
          className="btn btn-secondary"
          disabled={loading || busy}
          onClick={() => setRefresh((value) => value + 1)}
        >
          Làm mới ví
        </button>
      </div>
      {error && (
        <p role="alert" className="app-alert">
          {error}
        </p>
      )}
      {!wallet && loading && <p role="status">Đang tải ví…</p>}
      {wallet && (
        <div className="stats-grid">
          {[
            ["Khả dụng", wallet.available, "Số dư hiện có trong ví."],
            ["Đang giữ", wallet.held, "Khoản dành cho chuyến đi hoặc yêu cầu rút."],
            [
              "Thu nhập chờ nhận",
              wallet.pendingEarnings,
              "Doanh thu tài xế chưa đến hạn khả dụng.",
            ],
          ].map(([label, value, description]) => (
            <div className="card p-6" key={label}>
              <p className="muted">{label}</p>
              <p className="text-3xl font-bold mt-3">{money(Number(value))}</p>
              <p className="text-sm muted mt-3">{description}</p>
            </div>
          ))}
        </div>
      )}
      <div className="wallet-actions">
        {wallet?.canTopUp && (
          <form
            className="card p-6 space-y-4"
            onSubmit={(event) => {
              event.preventDefault();
              void topUp();
            }}
          >
            <h2 className="text-xl font-bold">Nạp tiền vào ví</h2>
            <p className="muted text-sm">
              Số tiền mô phỏng được cộng trực tiếp vào ví sau khi gửi yêu cầu thành công.
            </p>
            <div className="amount-presets">
              {[50000, 100000, 200000, 500000].map((value) => (
                <button
                  type="button"
                  key={value}
                  disabled={busy}
                  aria-pressed={amount === value}
                  className={`btn ${amount === value ? "" : "btn-secondary"}`}
                  onClick={() => setAmount(value)}
                >
                  {money(value)}
                </button>
              ))}
            </div>
            <label htmlFor="wallet-amount" className="block font-semibold">
              Số tiền (VND)
            </label>
            <input
              id="wallet-amount"
              className="field"
              type="number"
              min={10000}
              max={5000000}
              step={1}
              value={amount}
              required
              disabled={busy}
              onChange={(event) => setAmount(Number(event.target.value))}
            />
            <p className="text-sm muted">Từ 10.000 đến 5.000.000 đ mỗi lần nạp.</p>
            <button className="btn" disabled={busy} type="submit">
              {busy ? "Đang tạo yêu cầu…" : `Nạp mô phỏng ${money(amount)}`}
            </button>
          </form>
        )}
        {wallet?.canWithdraw && (
          <form
            className="card p-6 space-y-4"
            onSubmit={(event) => {
              event.preventDefault();
              void withdraw();
            }}
          >
            <h2 className="text-xl font-bold">Rút tiền mô phỏng</h2>
            <p className="muted text-sm">
              Tiền được giữ khi gửi yêu cầu và hệ thống tự động xử lý. Nếu bị từ chối, khoản giữ
              được trả về số dư khả dụng. Đây là thao tác mô phỏng.
            </p>
            <label htmlFor="wallet-withdraw-amount" className="block font-semibold">
              Số tiền (VND)
            </label>
            <input
              id="wallet-withdraw-amount"
              className="field"
              type="number"
              min={10000}
              max={5000000}
              step={1}
              value={withdrawAmount}
              required
              disabled={busy}
              onChange={(event) => setWithdrawAmount(Number(event.target.value))}
            />
            {wallet.available < 10000 && (
              <p className="muted text-sm">Cần tối thiểu 10.000 đ khả dụng để gửi yêu cầu rút.</p>
            )}
            {withdrawAmount > wallet.available && (
              <p className="muted text-sm">
                Số tiền rút vượt số dư khả dụng {money(wallet.available)}.
              </p>
            )}
            <p className="text-sm muted">Giới hạn 10.000–5.000.000 đ mỗi yêu cầu.</p>
            <button
              className="btn"
              disabled={
                busy || loading || wallet.available < 10000 || withdrawAmount > wallet.available
              }
              type="submit"
            >
              {busy ? "Đang gửi yêu cầu…" : `Yêu cầu rút ${money(withdrawAmount)}`}
            </button>
          </form>
        )}
      </div>
      <section className="card p-6 space-y-4">
        <h2 className="text-xl font-bold">Lịch sử ví</h2>
        {history?.items.length === 0 && <p className="muted">Chưa có giao dịch ví.</p>}
        <div className="overflow-x-auto">
          <table className="responsive-table w-full text-sm text-left">
            <thead>
              <tr>
                <th className="p-3">Giao dịch</th>
                <th className="p-3">Số tiền</th>
                <th className="p-3">Khả dụng sau giao dịch</th>
              </tr>
            </thead>
            <tbody>
              {history?.items.map((item) => (
                <tr key={item.reference} className="border-t border-emerald-50">
                  <td className="p-3" data-label="Giao dịch">
                    <strong>{activityLabel[item.kind] ?? item.kind}</strong>
                    <p className="muted mt-1">{date(item.createdAt)}</p>
                  </td>
                  <td className="p-3 font-bold" data-label="Số tiền">
                    {activitySign[item.kind] ?? ""}
                    {money(item.amount)}
                  </td>
                  <td className="p-3" data-label="Khả dụng">
                    {money(item.availableAfter)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination page={page} total={history?.total ?? 0} change={setPage} loading={loading} />
      </section>
      {wallet?.canWithdraw && (
        <section className="card p-6 space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h2 className="text-xl font-bold">Yêu cầu rút tiền</h2>
            <button
              className="btn btn-secondary"
              disabled={loading}
              onClick={() => setRefresh((value) => value + 1)}
            >
              Làm mới
            </button>
          </div>
          {withdrawals?.items.length === 0 && (
            <p className="muted">Bạn chưa gửi yêu cầu rút tiền.</p>
          )}
          {withdrawals?.items.map((item) => (
            <article
              key={item.id}
              className="flex flex-wrap items-start justify-between gap-3 border-t border-emerald-50 py-4"
            >
              <div>
                <strong>{money(item.amount)}</strong>
                <p className="muted mt-1">Yêu cầu mô phỏng · mã {item.id.slice(0, 8)}</p>
                <small className="muted">{date(item.requestedAt)}</small>
                {item.decisionReason && <p className="muted">Lý do: {item.decisionReason}</p>}
              </div>
              <span className="badge">{withdrawalStatus[item.state]}</span>
            </article>
          ))}
          <Pagination
            page={withdrawalPage}
            total={withdrawals?.total ?? 0}
            change={setWithdrawalPage}
            loading={loading}
          />
        </section>
      )}
    </section>
  );
}

function Pagination({
  page,
  total,
  change,
  loading,
}: {
  page: number;
  total: number;
  change: (page: number) => void;
  loading: boolean;
}) {
  return (
    <div className="pagination">
      <button
        className="btn btn-secondary"
        disabled={loading || page <= 1}
        onClick={() => change(page - 1)}
      >
        Trang trước
      </button>
      <span className="muted text-sm">
        Trang {page} · {total} giao dịch
      </span>
      <button
        className="btn btn-secondary"
        disabled={loading || page * 10 >= total}
        onClick={() => change(page + 1)}
      >
        Trang sau
      </button>
    </div>
  );
}
