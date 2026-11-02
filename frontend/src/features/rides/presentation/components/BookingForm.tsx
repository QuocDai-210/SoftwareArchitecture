import type { RidesViewModel } from "../useRides";
import type { RideService } from "../../application/RideService";
import { AddressField } from "./AddressField";

export function BookingForm({ model, service }: { model: RidesViewModel; service: RideService }) {
  const disabled = !!model.active || model.busy;
  return (
    <section className="card p-6">
      <p className="eyebrow">MỘT CHẠM, MỘT HÀNH TRÌNH</p>
      <h1 className="text-2xl font-bold mt-2 mb-5">Bạn muốn đi đâu?</h1>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          void model.run(model.book);
        }}
        className="space-y-4"
      >
        <AddressField
          label="Điểm đón"
          value={model.from}
          onChange={model.setFrom}
          service={service}
          disabled={disabled}
        />
        <button
          type="button"
          className="text-sm underline min-h-11"
          disabled={disabled}
          onClick={() => model.locate()}
        >
          Dùng vị trí hiện tại làm điểm đón
        </button>
        <AddressField
          label="Điểm đến"
          value={model.to}
          onChange={model.setTo}
          service={service}
          disabled={disabled}
        />
        <label className="block text-sm font-bold">
          Chọn điểm khi chạm bản đồ
          <select
            className="field mt-2"
            value={model.selecting}
            onChange={(e) => model.setSelecting(e.target.value as "pickup" | "destination")}
            disabled={disabled}
          >
            <option value="pickup">Điểm đón</option>
            <option value="destination">Điểm đến</option>
          </select>
        </label>
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
        <div className="service-choice">
          <span className="service-icon" aria-hidden="true">
            S
          </span>
          <div>
            <strong>SwiftRide</strong>
            <p className="text-xs muted">Một hành trình, giá rõ ràng</p>
          </div>
          <span className="badge ml-auto">Tiền mặt</span>
        </div>
        <button
          className="btn w-full"
          type="submit"
          disabled={
            disabled ||
            model.loading ||
            model.routeLoading ||
            !model.preview ||
            !model.from ||
            !model.to
          }
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
