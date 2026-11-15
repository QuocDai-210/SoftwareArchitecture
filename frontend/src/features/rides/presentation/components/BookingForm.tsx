import { useEffect, useState } from "react";
import type { RidesViewModel } from "../useRides";
import type { RideService } from "../../application/RideService";
import { AddressField } from "./AddressField";
import { money } from "../format";

export function BookingForm({
  model,
  service,
  initialName,
}: {
  model: RidesViewModel;
  service: RideService;
  initialName: string;
}) {
  const [name, setName] = useState(initialName);
  const [phone, setPhone] = useState("");
  const [note, setNote] = useState("");
  const [contact, setContact] = useState<{ name: string; phone: string } | null>(null);
  const [contactError, setContactError] = useState("");
  const [attempt, setAttempt] = useState(0);
  const [paymentMethod, setPaymentMethod] = useState<"Cod" | "Wallet">("Cod");
  useEffect(() => {
    let mounted = true;
    setContactError("");
    service
      .profile()
      .then((profile) => {
        if (!mounted) return;
        setContact(profile);
        setName(profile.name || initialName);
        setPhone(profile.phone);
      })
      .catch((error) => {
        if (mounted) setContactError(error.message);
      });
    return () => {
      mounted = false;
    };
  }, [service, initialName, attempt]);
  const disabled = !!model.active || model.busy || model.loading;
  return (
    <section className="card p-5 booking-form">
      <h1 className="text-xl font-bold mb-4">Bạn muốn đi đâu?</h1>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          void model.run(async () => {
            if (name !== contact?.name || phone !== contact?.phone)
              await service.saveProfile(name, phone);
            await model.book(note, paymentMethod);
          });
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
        <button
          className="text-sm underline min-h-11"
          type="button"
          disabled={disabled || (!model.from && !model.to)}
          onClick={() => {
            const from = model.from;
            model.setFrom(model.to);
            model.setTo(from);
          }}
        >
          Đổi điểm đón và điểm đến
        </button>
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
        <details
          open={contact !== null && !contact.phone}
          onInvalid={(event) => {
            event.currentTarget.open = true;
          }}
        >
          <summary className="cursor-pointer text-sm min-h-11">
            Liên hệ và ghi chú đón khách
          </summary>
          <fieldset className="space-y-3 mt-2" disabled={disabled || !contact}>
            <label className="block text-sm font-bold">
              Người đi xe
              <input
                className="field mt-2 font-normal"
                required
                maxLength={100}
                autoComplete="name"
                value={name}
                onChange={(event) => setName(event.target.value)}
              />
            </label>
            <label className="block text-sm font-bold">
              Số điện thoại liên hệ
              <input
                className="field mt-2 font-normal"
                type="tel"
                required
                maxLength={15}
                pattern="\+?[0-9]{9,15}"
                autoComplete="tel"
                value={phone}
                onChange={(event) => setPhone(event.target.value)}
              />
            </label>
            <label className="block text-sm font-bold">
              Ghi chú điểm đón
              <textarea
                className="field mt-2 font-normal"
                rows={2}
                maxLength={240}
                placeholder="Ví dụ: đón ở cổng chính, áo màu xanh"
                value={note}
                onChange={(event) => setNote(event.target.value)}
              />
            </label>
          </fieldset>
        </details>
        {contactError && (
          <p role="alert" className="text-sm text-red-800">
            {contactError}{" "}
            <button
              type="button"
              className="underline min-h-11"
              onClick={() => setAttempt(attempt + 1)}
            >
              Tải lại thông tin liên hệ
            </button>
          </p>
        )}
        <label className="block text-sm font-bold">
          Phương thức thanh toán
          <select
            className="field mt-2"
            value={paymentMethod}
            onChange={(event) => setPaymentMethod(event.target.value as "Cod" | "Wallet")}
            disabled={disabled}
          >
            <option value="Cod">Tiền mặt · COD</option>
            <option value="Wallet">Ví SwiftRide</option>
          </select>
        </label>
        <div className="booking-summary">
          <div>
            <strong>SwiftBike · Xe máy</strong>
            <p className="text-xs muted mt-1">Giá tham khảo</p>
            {model.estimate && (
              <p className="text-xl font-bold mt-1">{money(model.estimate.amount)}</p>
            )}
          </div>
          <span className="badge">
            {paymentMethod === "Cod" ? "Tiền mặt · COD" : "Ví SwiftRide"}
          </span>
        </div>
        <button
          className="btn w-full"
          type="submit"
          disabled={
            disabled || !contact || model.routeLoading || !model.preview || !model.from || !model.to
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
