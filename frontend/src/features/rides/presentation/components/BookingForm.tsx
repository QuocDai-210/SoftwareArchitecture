import type { RidesViewModel } from "../useRides";
import type { RideService } from "../../application/RideService";
import { AddressField } from "./AddressField";

export function BookingForm({ model, service }: { model: RidesViewModel; service: RideService }) {
  const disabled = !!model.active || model.busy;
  return <section className="card p-6">
    <h2 className="text-lg font-bold mb-5">Đặt một chuyến xe</h2>
    <form onSubmit={event => { event.preventDefault(); void model.run(model.book); }} className="space-y-4">
      <AddressField label="Điểm đón" value={model.from} onChange={model.setFrom} service={service} disabled={disabled} />
      <button type="button" className="text-sm underline min-h-11" disabled={disabled}
        onClick={() => model.locate()}>Dùng vị trí hiện tại làm điểm đón</button>
      <AddressField label="Điểm đến" value={model.to} onChange={model.setTo} service={service} disabled={disabled} />
      <label className="block text-sm font-bold">Chọn điểm khi chạm bản đồ
        <select className="field mt-2" value={model.selecting} onChange={e => model.setSelecting(e.target.value as "pickup" | "destination")} disabled={disabled}>
          <option value="pickup">Điểm đón</option><option value="destination">Điểm đến</option>
        </select>
      </label>
      <label className="block text-sm font-bold">Thanh toán
        <select className="field mt-2" value={model.method} onChange={e => model.setMethod(e.target.value as "Card" | "Wallet")} disabled={disabled}>
          <option value="Wallet">Ví SwiftRide</option><option value="Card">VNPAY sandbox</option>
        </select>
      </label>
      <button className="btn w-full" type="submit" disabled={disabled || model.loading || !model.from || !model.to}>
        {model.busy ? "Đang gửi yêu cầu…" : model.active ? "Bạn đang có chuyến" : "Tìm xe và xem giá"}
      </button>
      <p className="text-xs text-[#62766b]">Xác nhận giá trước khi đặt. VNPAY sandbox dùng tài khoản thử nghiệm.</p>
    </form>
  </section>;
}
