"use client";

import { useCallback, useEffect, useState } from "react";
import type { SandboxGateway, SandboxPayment } from "../application/SandboxGateway";
import { isConfirmed, watchSandboxPayment } from "../application/watchSandboxPayment";

const labels = {
  Pending: "Đang chờ xác nhận từ VNPAY",
  Succeeded: "Thanh toán thử nghiệm thành công",
  Failed: "Giao dịch không thành công",
  NeedsReview: "Cần đối soát thêm với VNPAY",
};

export function SandboxPanel({
  gateway,
  reference,
}: {
  gateway: SandboxGateway;
  reference?: string;
}) {
  const [amount, setAmount] = useState(10000);
  const [payment, setPayment] = useState<SandboxPayment | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [now, setNow] = useState(0);
  const isTopUp = payment?.purpose === "WalletTopUp";
  const accept = useCallback((result: SandboxPayment) => {
    setNow(Date.now());
    setPayment((previous) =>
      previous?.reference === result.reference && isConfirmed(previous) ? previous : result,
    );
  }, []);
  const cooldown = payment?.nextQueryAt
    ? Math.max(0, Math.ceil((Date.parse(payment.nextQueryAt) - now) / 1000))
    : 0;

  useEffect(() => {
    setNow(Date.now());
    if (!payment?.nextQueryAt || isConfirmed(payment)) {
      return;
    }
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [payment]);

  useEffect(() => {
    if (!reference) {
      return;
    }
    return watchSandboxPayment(gateway, reference, accept, setError, (action, delay) => {
      const timer = setTimeout(action, delay);
      return () => clearTimeout(timer);
    });
  }, [gateway, reference, accept]);

  async function run(action: () => Promise<SandboxPayment>) {
    setBusy(true);
    setError("");
    try {
      const result = await action();
      accept(result);
      if (result.checkoutUrl) {
        const url = new URL(result.checkoutUrl);
        if (url.origin !== "https://sandbox.vnpayment.vn") {
          throw new Error("Địa chỉ thanh toán không hợp lệ.");
        }
        window.location.assign(url.href);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không thể thực hiện. Hãy thử lại.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="card p-6 mx-auto max-w-xl space-y-5">
      <a href={isTopUp ? "/#wallet" : "/#payments"} className="underline">
        ← Về SwiftRide
      </a>
      <h1 className="text-2xl font-bold">
        {isTopUp
          ? "Kết quả nạp ví"
          : reference
            ? "Kết quả thanh toán"
            : "Thử thanh toán VNPAY sandbox"}
      </h1>
      {isTopUp ? (
        <p>Số dư mô phỏng trong Ví SwiftRide · VNPAY sandbox.</p>
      ) : (
        (!reference || payment) && <p>Giao dịch thử kết nối, không cộng tiền vào ví.</p>
      )}
      {reference && !payment && <p role="status">Đang kiểm tra kết quả thanh toán…</p>}
      {!reference && (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            void run(() => gateway.create(amount));
          }}
          className="space-y-4"
        >
          <label className="block" htmlFor="sandbox-amount">
            Số tiền thử (VND)
          </label>
          <input
            id="sandbox-amount"
            type="number"
            min={10000}
            max={5000000}
            step={1}
            required
            value={amount}
            disabled={busy}
            onChange={(event) => setAmount(Number(event.target.value))}
          />
          <button type="submit" className="btn" disabled={busy}>
            {busy ? "Đang xử lý…" : "Sang VNPAY sandbox"}
          </button>
        </form>
      )}
      {payment && (
        <div className="space-y-3" aria-live="polite">
          <h2 className="font-bold">
            {isTopUp && payment.walletCreditedAt ? "Đã nạp tiền vào ví" : labels[payment.state]}
          </h2>
          <p>
            Mã giao dịch: <code className="break-all">{payment.reference}</code>
          </p>
          <p>Số tiền: {payment.amount.toLocaleString("vi-VN")} đ</p>
          {["Pending", "NeedsReview"].includes(payment.state) && (
            <p>Đang xác minh thanh toán với VNPAY. Trang sẽ tự cập nhật khi có kết quả.</p>
          )}
          <a className="underline" href={`/payments/sandbox?reference=${payment.reference}`}>
            Lưu đường dẫn theo dõi giao dịch
          </a>
        </div>
      )}
      {(reference || payment) && (!payment || !isConfirmed(payment)) && (
        <button
          type="button"
          className="btn btn-secondary"
          disabled={busy || cooldown > 0}
          onClick={() => void run(() => gateway.reconcile(reference ?? payment!.reference))}
        >
          {busy
            ? "Đang kiểm tra…"
            : cooldown > 0
              ? `Kiểm tra lại sau ${cooldown} giây`
              : "Kiểm tra lại với VNPAY"}
        </button>
      )}
      {reference && (
        <a href={isTopUp ? "/#wallet" : "/payments/sandbox"} className="block underline">
          {isTopUp ? "Xem số dư và lịch sử ví" : "Tạo giao dịch thử khác"}
        </a>
      )}
      {error && (
        <p role="alert" className="text-red-700">
          {error}
        </p>
      )}
    </section>
  );
}
