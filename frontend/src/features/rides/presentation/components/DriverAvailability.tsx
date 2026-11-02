import type { RidesViewModel } from "../useRides";
export function DriverAvailability({
  busy,
  loading,
  driver,
  toggleAvailability,
}: Pick<RidesViewModel, "busy" | "loading" | "driver" | "toggleAvailability">) {
  return (
    <section className="card p-6">
      <h2 className="text-lg font-bold mb-2">Nhận chuyến quanh bạn</h2>
      <p className="text-sm text-[#62766b] mb-5">
        Bật trực tuyến để nhận lời mời tại vị trí xe. Lời mời có hiệu lực 60 giây.
      </p>
      <button
        className="btn w-full"
        disabled={busy || loading || !driver || driver.busy}
        onClick={toggleAvailability}
      >
        {driver?.available ? "Chuyển ngoại tuyến" : "Bật nhận chuyến"}
      </button>
      {driver?.busy && <p className="text-sm mt-4 text-[#416342]">Bạn đang phục vụ một chuyến.</p>}
    </section>
  );
}
