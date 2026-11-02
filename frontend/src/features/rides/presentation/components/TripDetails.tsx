import type { Trip, Simulation } from "../../domain/models";
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
}: {
  trip: Trip;
  isDriver: boolean;
  isRider: boolean;
  busy: boolean;
  act: (t: Trip, action: TripAction) => void;
  simulation: Simulation | null;
}) {
  const b = t.quote?.breakdown;
  return (
    <section className="card p-6" aria-label="Chi tiết chuyến">
      <div className="flex justify-between gap-3 items-start">
        <h2 className="font-bold text-lg">Chi tiết chuyến</h2>
        <span className="text-xs bg-[#edf3e7] px-2 py-1 rounded-lg">{t.id.slice(0, 8)}</span>
      </div>
      <p className="text-sm font-bold text-[#12695b] my-4" role="status" aria-live="polite">
        {labels[t.status]}
      </p>
      <div className="border-l-2 border-[#90ad8b] pl-4 space-y-4 text-sm">
        <p>
          <span className="block text-xs text-[#62766b]">ĐIỂM ĐÓN</span>
          {t.pickup.label}
        </p>
        <p>
          <span className="block text-xs text-[#62766b]">ĐIỂM ĐẾN</span>
          {t.destination.label}
        </p>
      </div>
      {t.quote && (
        <div className="bg-[#f3f6ee] rounded-xl p-4 my-5">
          <p className="text-xs text-[#62766b]">Giá chuyến đi</p>
          <p className="text-3xl font-bold my-2">{money(t.quote.amount)}</p>
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
      <details className="mt-5 border-t border-[#e4e8e0] pt-4 text-xs">
        <summary className="cursor-pointer text-[#526c5b] min-h-8">Lịch sử trạng thái</summary>
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
