"use client";

import { useEffect, useState } from "react";
import type { SandboxPayment } from "../application/SandboxGateway";
import type {
  Wallet,
  WalletActivity,
  WalletGateway,
  WalletPage,
  WalletWithdrawal,
} from "../application/WalletGateway";

const money = (amount: number) => `${amount.toLocaleString("vi-VN")} đ`;
const date = (value: string) => new Date(value).toLocaleString("vi-VN");
const status: Record<string, string> = {
  Pending: "Chờ thanh toán / xác nhận",
  Succeeded: "Đã nạp vào ví",
  Failed: "Không thành công",
  NeedsReview: "Đang xác minh",
};
const withdrawalStatus: Record<WalletWithdrawal["state"], string> = {
  Pending: "Chờ Ops duyệt · đang giữ tiền",
  Completed: "Đã xử lý mô phỏng",
  Rejected: "Đã từ chối · tiền đã trả lại ví",
};
const activityLabel: Record<string, string> = {
  WalletTopUp: "Nạp ví qua VNPAY",
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
  const [topUps, setTopUps] = useState<WalletPage<SandboxPayment> | null>(null);
  const [withdrawals, setWithdrawals] = useState<WalletPage<WalletWithdrawal> | null>(null);
  const [page, setPage] = useState(1);
  const [topUpPage, setTopUpPage] = useState(1);
  const [withdrawalPage, setWithdrawalPage] = useState(1);
  const [refresh, setRefresh] = useState(0);
  const [amount, setAmount] = useState(100000);
  const [withdrawAmount, setWithdrawAmount] = useState(10000);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    let timer: ReturnType<typeof setTimeout>;
    setLoading(true);
    async function load() {
      try {
        const [balance, activity, requests, payouts] = await Promise.all([
          gateway.balance(),
          gateway.history(page),
          gateway.topUps(topUpPage),
          gateway.withdrawals(withdrawalPage),
        ]);
        if (!active) {
          return;
        }
        setWallet(balance);
        setHistory(activity);
        setTopUps(requests);
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
  }, [gateway, page, topUpPage, withdrawalPage, refresh]);

  async function topUp() {
    if (!Number.isInteger(amount) || amount < 10000 || amount > 5000000) {
      setError("Nhập số tiền nguyên đồng từ 10.000 đến 5.000.000 đ.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      const payment = await gateway.topUp(amount);
      if (payment.checkoutUrl) {
        const url = new URL(payment.checkoutUrl);
        if (url.origin !== "https://sandbox.vnpayment.vn") {
          throw new Error("Địa chỉ thanh toán không hợp lệ.");
        }
        window.location.assign(url.href);
      } else {
        window.location.assign(`/payments/sandbox?reference=${payment.reference}`);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không thể tạo yêu cầu nạp tiền.");
    } finally {
      setBusy(false);
    }
  }

  async function withdraw() {
    if (!Number.isInteger(withdrawAmount) || withdrawAmount < 10000 || withdrawAmount > 5000000) {
      setError("Số tiền rút mỗi lần từ 10.000 đến 5.000.000 đ.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      await gateway.withdraw({ amount: withdrawAmount });
      setRefresh((value) => value + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không thể tạo yêu cầu rút tiền.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="space-y-5" aria-label="Ví SwiftRide">
      <div className="flex flex-wrap gap-3 items-center justify-between">
        <p className="badge">Ví mô phỏng · VNPAY sandbox</p>
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
      {wallet?.canTopUp && (
        <form
          className="card p-6 space-y-4"
          onSubmit={(event) => {
            event.preventDefault();
            void topUp();
          }}
        >
          <h2 className="text-xl font-bold">Nạp tiền vào ví</h2>
          <p className="muted text-sm">Dùng thẻ thử nghiệm VNPAY. Đây là số dư mô phỏng.</p>
          <div className="flex flex-wrap gap-3">
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
            {busy ? "Đang tạo yêu cầu…" : `Nạp ${money(amount)} qua VNPAY`}
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
            Tiền sẽ được giữ khi gửi yêu cầu. Ops duyệt để ghi nhận chi mô phỏng; từ chối sẽ trả
            tiền về số dư khả dụng. Không cần nhập thông tin ngân hàng.
          </p>
          <label htmlFor="wallet-withdraw-amount" className="block font-semibold">
            Số tiền (VND)
          </label>
          <input
            id="wallet-withdraw-amount"
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
      <section className="card p-6 space-y-4">
        <h2 className="text-xl font-bold">Lịch sử ví</h2>
        {history?.items.length === 0 && <p className="muted">Chưa có giao dịch ví.</p>}
        <div className="overflow-x-auto">
          <table className="w-full text-sm text-left">
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
                  <td className="p-3">
                    {item.kind === "WalletTopUp" ? (
                      <a
                        className="underline font-semibold"
                        href={`/payments/sandbox?reference=${item.reference}`}
                      >
                        {activityLabel[item.kind]}
                      </a>
                    ) : (
                      <strong>{activityLabel[item.kind] ?? item.kind}</strong>
                    )}
                    <p className="muted mt-1">{date(item.createdAt)}</p>
                  </td>
                  <td className="p-3 font-bold">
                    {activitySign[item.kind] ?? ""}
                    {money(item.amount)}
                  </td>
                  <td className="p-3">{money(item.availableAfter)}</td>
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
      {wallet?.canTopUp && (
        <section className="card p-6 space-y-4">
          <h2 className="text-xl font-bold">Yêu cầu nạp tiền</h2>
          {topUps?.items.length === 0 && <p className="muted">Chưa có yêu cầu nạp tiền.</p>}
          {topUps?.items.map((item) => (
            <a
              key={item.reference}
              href={`/payments/sandbox?reference=${item.reference}`}
              className="flex flex-wrap gap-3 justify-between border-t border-emerald-50 py-4"
            >
              <span>
                <strong>{money(item.amount)}</strong>
                <small className="block muted mt-1">
                  {date(item.createdAt)} · {item.reference.slice(0, 8)}
                </small>
              </span>
              <span className="badge">{status[item.state]}</span>
            </a>
          ))}
          <Pagination
            page={topUpPage}
            total={topUps?.total ?? 0}
            change={setTopUpPage}
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
    <div className="flex justify-between items-center gap-3">
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
