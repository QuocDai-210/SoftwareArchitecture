import type { Payment, Trip } from "../../domain/models";
import { money } from "../format";

export function CashReceipt({
  trip,
  payment,
  unavailable,
}: {
  trip: Trip;
  payment: Payment | null;
  unavailable: boolean;
}) {
  if (trip.method !== "Cod")
    return (
      <section className="cash-receipt" aria-label="Thanh toán ví">
        <p className="font-semibold">Thanh toán ví mô phỏng</p>
        <p className="text-sm mt-2" role="status">
          {unavailable
            ? "Tạm mất kết nối thanh toán. Hệ thống sẽ cập nhật lại."
            : payment?.status === "Settled"
              ? "Đã thanh toán bằng ví."
              : trip.status === "PaymentReview"
                ? "Thanh toán đang chờ xử lý."
                : "Đang chờ kết quả thanh toán ví."}
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
      {!settled && (
        <p className="text-sm mt-3 muted">
          Chuyến hoàn tất khi tài xế xác nhận thu tiền mô phỏng. Không sử dụng tiền thật.
        </p>
      )}
    </section>
  );
}
