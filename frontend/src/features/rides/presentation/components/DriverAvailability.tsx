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
    <section className="card p-6 space-y-4">
      <h2 className="text-lg font-bold">Vị trí và nhận chuyến</h2>
      <p className="text-sm text-[#62766b]">
        Chọn vị trí xe trên bản đồ hoặc tìm địa chỉ. Lời mời nhận chuyến có hiệu lực 60 giây.
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
          disabled={disabled}
          onClick={() => model.locate(true)}
        >
          Dùng vị trí hiện tại
        </button>
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
