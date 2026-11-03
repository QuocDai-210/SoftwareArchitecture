import type { Trip, Simulation, AssignedDriver } from "../../domain/models";
import { availableActions, type TripAction } from "../../domain/trip-rules";
import { labels, money } from "../format";
const actionLabels: Record<TripAction, string> = {
  "confirm-quote": "Đồng ý giá",
  cancel: "Hủy chuyến",
  "driver-accept": "Nhận chuyến",
  "driver-reject": "Từ chối",
  "en-route": "Đang đến đón",
  pickup: "Đã đón khách",
  dropoff: "Đã trả khách",
};
export function TripDetails({
  trip: t,
  isDriver,
  isRider,
  busy,
  act,
  simulation,
  assignedDriver,
}: {
  trip: Trip;
  isDriver: boolean;
  isRider: boolean;
  busy: boolean;
  act: (t: Trip, action: TripAction) => void;
  simulation: Simulation | null;
  assignedDriver: AssignedDriver | null;
}) {
  const b = t.quote?.breakdown;
  const hasCashReceipt =
    t.method === "Cod" && ["PaymentPending", "PaymentReview", "Paid"].includes(t.status);
  return (
    <section className="card p-5" aria-label="Chi tiết chuyến">
      <h2 className="sr-only">Chi tiết chuyến</h2>
      <p className="sr-only" role="status" aria-live="polite">
        {labels[t.status]}
      </p>
      <details className="trip-route text-sm">
        <summary className="cursor-pointer min-h-11">
          <span>
            <span className="block truncate">Đón: {t.pickup.label}</span>
            <span className="block truncate muted mt-1">Đến: {t.destination.label}</span>
          </span>
        </summary>
        <div className="border-l-2 border-[#9fd7be] pl-4 space-y-3 mt-3">
          <p>
            <span className="block text-xs muted">ĐIỂM ĐÓN</span>
            {t.pickup.label}
          </p>
          <p>
            <span className="block text-xs muted">ĐIỂM ĐẾN</span>
            {t.destination.label}
          </p>
        </div>
      </details>
      {assignedDriver &&
        !isDriver &&
        [
          "DriverAccepted",
          "EnRoute",
          "PickedUp",
          "PaymentPending",
          "PaymentReview",
          "Paid",
        ].includes(t.status) && (
          <div className="driver-card">
            <span className="avatar">{assignedDriver.name.slice(0, 1)}</span>
            <div>
              <p className="font-bold">{assignedDriver.name}</p>
              <p className="text-xs muted">
                {assignedDriver.vehicle || "Xe của bạn"} ·{" "}
                {assignedDriver.plate || "Chưa có biển số"}
              </p>
              {assignedDriver.phone && (
                <a className="text-sm underline" href={`tel:${assignedDriver.phone}`}>
                  Gọi tài xế
                </a>
              )}
            </div>
          </div>
        )}
      {t.deadline &&
        ["AwaitingDriverAcceptance", "AwaitingRiderConfirmation"].includes(t.status) && (
          <p className="text-xs muted mt-4">
            Còn {Math.max(0, Math.ceil((new Date(t.deadline).getTime() - Date.now()) / 1000))} giây
            để xác nhận.
          </p>
        )}
      {t.quote && (
        <div className={hasCashReceipt ? "mt-4" : "bg-[#f0faf5] rounded-xl p-4 my-4"}>
          {!hasCashReceipt && (
            <>
              <p className="text-xs text-[#62766b]">
                Giá chuyến đi · {t.method === "Cod" ? "Tiền mặt · COD" : t.method}
              </p>
              <p className="text-2xl font-bold my-2">{money(t.quote.amount)}</p>
            </>
          )}
          {b && (
            <details className="text-xs text-[#526c5b]">
              <summary className="cursor-pointer min-h-8">
                {b.distanceKm.toFixed(1)} km · {Math.ceil(b.durationMinutes)} phút ước lượng · Chi
                tiết giá
              </summary>
              <dl className="grid grid-cols-2 gap-2 mt-2">
                <dt>Giá mỗi km</dt>
                <dd>{money(b.perKm)}</dd>
                <dt>Giá mỗi phút</dt>
                <dd>{money(b.perMinute)}</dd>
                <dt>Hệ số giá động</dt>
                <dd>{b.surge}</dd>
                <dt>Phụ phí</dt>
                <dd>{money(b.toll)}</dd>
                <dt>Hệ số khuyến mãi</dt>
                <dd>{b.promotion}</dd>
                <dt>Thuế</dt>
                <dd>{b.tax * 100}%</dd>
              </dl>
            </details>
          )}
          {t.status === "AwaitingRiderConfirmation" && (
            <p className="text-xs mt-3">
              Giá có hiệu lực đến {new Date(t.quote.expiresAt).toLocaleTimeString("vi-VN")}.
            </p>
          )}
        </div>
      )}
      {t.reason && <p className="text-sm my-4 text-[#6b5a32]">{t.reason}</p>}
      {isDriver && ["EnRoute", "PickedUp"].includes(t.status) && !simulation?.arrived && (
        <p className="text-sm mt-4" role="status">
          Chờ xe đến điểm {t.status === "EnRoute" ? "đón" : "trả"} khách trước khi xác nhận.
        </p>
      )}
      <div className="flex flex-wrap gap-2 mt-5">
        {availableActions(t, isRider ? "rider" : isDriver ? "driver" : "observer").map((action) => (
          <button
            key={action}
            disabled={
              busy ||
              ((action === "pickup" || action === "dropoff") &&
                (!simulation?.arrived ||
                  simulation.stage !== (action === "pickup" ? "EnRoute" : "PickedUp")))
            }
            className={`btn ${["cancel", "driver-reject"].includes(action) ? "btn-secondary" : "flex-1"}`}
            onClick={() => act(t, action)}
          >
            {action === "cancel" && t.status === "AwaitingRiderConfirmation"
              ? "Từ chối giá"
              : actionLabels[action]}
          </button>
        ))}
      </div>
      <details className="mt-5 border-t border-[#dcebe5] pt-4 text-xs">
        <summary className="cursor-pointer text-[#526c5b] min-h-8">Lịch sử trạng thái</summary>
        <p className="break-all mt-2 muted">Mã chuyến: {t.id}</p>
        <ol className="space-y-2 mt-2">
          {t.history.map((h, i) => (
            <li key={i}>
              <span className="text-[#62766b]">{new Date(h.at).toLocaleTimeString("vi-VN")}</span> ·{" "}
              {labels[h.status]}
              {h.reason && ` · ${h.reason}`}
            </li>
          ))}
        </ol>
      </details>
    </section>
  );
}
