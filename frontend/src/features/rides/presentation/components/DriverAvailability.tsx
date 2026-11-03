import type { RidesViewModel } from "../useRides";
import type { RideService } from "../../application/RideService";
import { AddressField } from "./AddressField";

export function DriverAvailability({
  model,
  service,
}: {
  model: RidesViewModel;
  service: RideService;
}) {
  const disabled =
    model.busy || model.loading || !model.driver || model.driver.busy || !!model.active;
  return (
    <section className="card p-5 space-y-4">
      <div className="flex justify-between gap-2 items-center">
        <h1 className="text-lg font-bold">Nhận chuyến</h1>
        <span className="badge">
          {model.driver?.available ? "Đang trực tuyến" : "Đang ngoại tuyến"}
        </span>
      </div>
      <p className="text-sm text-[#62766b]">
        Chọn vị trí xe hoặc dùng nút định vị trên bản đồ để bắt đầu.
      </p>
      <AddressField
        label="Đặt vị trí xe"
        value={model.driverPoint}
        onChange={model.setDriverPoint}
        service={service}
        disabled={disabled}
      />
      <div className="flex flex-wrap gap-3">
        <button
          className="btn btn-secondary"
          disabled={disabled || !model.driverPoint}
          onClick={() =>
            void model.run(async () => {
              if (model.driverPoint)
                await service.location(model.driverPoint.latitude, model.driverPoint.longitude);
            })
          }
        >
          Lưu vị trí xe
        </button>
      </div>
      <button className="btn w-full" disabled={disabled} onClick={model.toggleAvailability}>
        {model.driver?.available ? "Chuyển ngoại tuyến" : "Bật nhận chuyến"}
      </button>
      {model.driver?.busy && (
        <p className="text-sm text-[#416342]" role="status">
          Bạn đang phục vụ một chuyến. Vị trí xe do hành trình cập nhật.
        </p>
      )}
    </section>
  );
}
