import type { RidesViewModel } from "../useRides";
import type { RideService } from "../../application/RideService";
import { AddressField } from "./AddressField";

export function BookingForm({ model, service }: { model: RidesViewModel; service: RideService }) {
  const disabled = !!model.active || model.busy || model.loading;
  return (
    <section className="card p-5 booking-form">
      <h1 className="text-xl font-bold mb-4">Bạn muốn đi đâu?</h1>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          void model.run(model.book);
        }}
        className="space-y-4"
      >
        <div className="booking-stops">
          <AddressField
            label="Điểm đón"
            value={model.from}
            onChange={model.setFrom}
            service={service}
            disabled={disabled}
            onFocus={() => model.setSelecting("pickup")}
          />
          <AddressField
            label="Điểm đến"
            value={model.to}
            onChange={model.setTo}
            service={service}
            disabled={disabled}
            onFocus={() => model.setSelecting("destination")}
          />
        </div>
        <fieldset className="map-target">
          <legend className="sr-only">Chọn điểm khi chạm bản đồ</legend>
          <span>Chạm bản đồ:</span>
          {(["pickup", "destination"] as const).map((target) => (
            <button
              key={target}
              type="button"
              disabled={disabled}
              aria-pressed={model.selecting === target}
              onClick={() => model.setSelecting(target)}
            >
              {target === "pickup" ? "Điểm đón" : "Điểm đến"}
            </button>
          ))}
        </fieldset>
        {model.routeLoading && (
          <p className="text-sm" role="status">
            Đang tìm đường đi…
          </p>
        )}
        {model.routeError && (
          <p className="text-sm text-red-800" role="alert">
            {model.routeError}
          </p>
        )}
        {model.preview && (
          <p className="text-sm">
            {model.preview.distanceKm.toFixed(1)} km · Khoảng{" "}
            {Math.ceil(model.preview.durationMinutes)} phút
          </p>
        )}
        <div className="booking-summary">
          <strong>SwiftRide</strong>
          <span className="badge">Tiền mặt · COD</span>
        </div>
        <button
          className="btn w-full"
          type="submit"
          disabled={disabled || model.routeLoading || !model.preview || !model.from || !model.to}
        >
          {model.busy
            ? "Đang gửi yêu cầu…"
            : model.active
              ? "Bạn đang có chuyến"
              : "Tìm xe và xem giá"}
        </button>
        <p className="text-xs text-[#62766b]">
          Giá chính thức được hiển thị để bạn xác nhận trước khi gửi lời mời cho tài xế.
        </p>
      </form>
    </section>
  );
}
