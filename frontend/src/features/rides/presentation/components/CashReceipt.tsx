import { useState } from "react";
import type { Payment, Trip } from "../../domain/models";
import type { RideService } from "../../application/RideService";
import { money } from "../format";

export function CashReceipt({
  trip,
  payment,
  unavailable,
  isDriver,
  busy,
  service,
  run,
}: {
  trip: Trip;
  payment: Payment | null;
  unavailable: boolean;
  isDriver: boolean;
  busy: boolean;
  service: RideService;
  run: (action: () => Promise<void>) => Promise<void>;
}) {
  const [received, setReceived] = useState(false);
  if (trip.method !== "Cod")
    return (
      <section className="cash-receipt" aria-label="Giao dịch cũ">
        <p className="font-semibold">Giao dịch cũ · {trip.method}</p>
        <p className="text-sm muted mt-2">
          Giữ nguyên lịch sử thanh toán. Liên hệ Ops để đối soát nếu còn giao dịch dang dở.
        </p>
      </section>
    );
  const settled = !!payment?.collectedAt;
  return (
    <section className="cash-receipt" aria-label="Thanh toán tiền mặt">
      <p className="eyebrow">THANH TOÁN TIỀN MẶT</p>
      <h2 className="text-2xl font-bold mt-2">
        {money(payment?.amount ?? trip.quote?.amount ?? 0)}
      </h2>
      <p className="mt-2 font-semibold" role="status">
        {payment?.status === "Refunded"
          ? "Đã hoàn tiền mặt"
          : settled
            ? "Đã thu tiền mặt"
            : "Chưa xác nhận thu tiền"}
      </p>
      {payment?.collectedAt && (
        <p className="text-sm mt-2 muted">
          Đã ghi nhận lúc {new Date(payment.collectedAt).toLocaleString("vi-VN")}.
        </p>
      )}
      {payment?.refundedAt && (
        <p className="text-sm mt-2 muted">
          Đã hoàn lúc {new Date(payment.refundedAt).toLocaleString("vi-VN")} · {payment.reason}
        </p>
      )}
      {unavailable && (
        <p role="status" className="text-sm mt-3">
          Tạm mất kết nối thanh toán. Hệ thống sẽ cập nhật lại; không cần thu tiền lần nữa.
        </p>
      )}
      {!payment && !unavailable && (
        <p className="text-sm mt-3 muted">Đang tạo giao dịch. Vui lòng chờ.</p>
      )}
      {isDriver && payment?.method === "Cod" && payment.status === "Pending" && !settled && (
        <form
          className="space-y-4 mt-4"
          onSubmit={(event) => {
            event.preventDefault();
            void run(async () => {
              await service.confirmCash(payment.id);
            });
          }}
        >
          <label className="check-label">
            <input
              type="checkbox"
              required
              checked={received}
              onChange={(event) => setReceived(event.target.checked)}
            />
            Tôi đã nhận đủ {money(payment.amount)} từ khách.
          </label>
          <button className="btn w-full" disabled={busy || !received}>
            Xác nhận đã thu tiền
          </button>
        </form>
      )}
      {!isDriver && !settled && (
        <p className="text-sm mt-3 muted">
          Thanh toán trực tiếp cho tài xế. Chuyến hoàn tất khi tài xế xác nhận đã nhận tiền.
        </p>
      )}
    </section>
  );
}
