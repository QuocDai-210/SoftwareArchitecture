import type { RidesViewModel } from "../useRides";
import { labels, money } from "../format";
import { terminal } from "../../domain/trip-rules";
export function TripHistory({
  busy,
  loading,
  trips,
  current,
  run,
  reload,
  setSelected,
  onRebook,
  status,
  onFilter,
}: Pick<RidesViewModel, "busy" | "loading" | "trips" | "current" | "run" | "reload"> & {
  setSelected: (id: string) => void;
  onRebook?: (trip: RidesViewModel["trips"][number]) => void;
  status: string;
  onFilter: (status: string) => void;
}) {
  return (
    <section id="history" className="card overflow-hidden">
      <div className="p-5 border-b border-[#dcebe5] flex justify-between items-center">
        <h2 className="font-bold text-lg">Chuyến đi gần đây</h2>
        <button
          className="text-sm underline min-h-11"
          disabled={busy}
          onClick={() => void run(reload)}
        >
          Làm mới
        </button>
      </div>
      <label className="block p-5 text-sm">
        Lọc trạng thái chuyến
        <select
          className="field mt-2"
          value={status}
          disabled={busy}
          onChange={(event) => onFilter(event.target.value)}
        >
          <option value="">Tất cả trạng thái</option>
          {["Paid", "Cancelled", "Failed", "EnRoute", "PickedUp", "PaymentPending"].map((value) => (
            <option key={value} value={value}>
              {labels[value]}
            </option>
          ))}
        </select>
      </label>
      {loading ? (
        <p className="p-6 text-sm" role="status">
          Đang tải chuyến đi…
        </p>
      ) : !trips.length ? (
        <div className="p-8">
          <p className="font-bold">Hành trình đầu tiên đang chờ</p>
          <p className="text-sm text-[#62766b] mt-2">Chọn điểm đón và điểm đến để bắt đầu.</p>
        </div>
      ) : (
        <ul className="divide-y divide-[#dcebe5]">
          {trips.map((t) => (
            <li key={t.id}>
              <button
                className={`w-full text-left p-5 hover:bg-[#f4fbf7] ${current?.id === t.id ? "bg-[#e7f8ef]" : ""}`}
                onClick={() => setSelected(t.id)}
                aria-pressed={current?.id === t.id}
              >
                <div className="flex justify-between gap-3">
                  <span className="font-bold text-sm">{t.destination.label}</span>
                  <span className="font-bold text-sm whitespace-nowrap">
                    {t.quote
                      ? money(t.quote.amount)
                      : terminal.has(t.status)
                        ? "Chưa có báo giá"
                        : "Đang báo giá"}
                  </span>
                </div>
                <div className="flex justify-between flex-wrap gap-2 mt-2 text-xs text-[#62766b]">
                  <span>{new Date(t.createdAt).toLocaleString("vi-VN")}</span>
                  <span>{labels[t.status]}</span>
                </div>
              </button>
              {t.rating && <p className="px-5 text-sm">{t.rating}/5 sao</p>}
              {onRebook && ["Paid", "Cancelled", "Failed"].includes(t.status) && (
                <button
                  className="btn btn-secondary mx-5 mb-4"
                  disabled={busy}
                  onClick={() => onRebook(t)}
                >
                  Đặt lại chuyến
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
