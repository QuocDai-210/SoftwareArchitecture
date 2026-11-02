import type { RidesViewModel } from "../useRides";
import { labels, money } from "../format";
export function TripHistory({
  busy,
  loading,
  trips,
  current,
  isDriver,
  run,
  reload,
  setSelected,
}: Pick<
  RidesViewModel,
  "busy" | "loading" | "trips" | "current" | "isDriver" | "run" | "reload" | "setSelected"
>) {
  return (
    <section id="history" className="card overflow-hidden">
      <div className="p-5 border-b border-[#e4e8e0] flex justify-between items-center">
        <h2 className="font-bold text-lg">Chuyến đi gần đây</h2>
        <button
          className="text-sm underline min-h-11"
          disabled={busy}
          onClick={() => void run(reload)}
        >
          Làm mới
        </button>
      </div>
      {loading ? (
        <p className="p-6 text-sm" role="status">
          Đang tải chuyến đi…
        </p>
      ) : !trips.length ? (
        <div className="p-8">
          <p className="font-bold">Hành trình đầu tiên đang chờ</p>
          <p className="text-sm text-[#62766b] mt-2">
            {isDriver
              ? "Bật trực tuyến và chờ lời mời từ User."
              : "Chọn điểm đón và điểm đến để bắt đầu."}
          </p>
        </div>
      ) : (
        <ul className="divide-y divide-[#e4e8e0]">
          {trips.map((t) => (
            <li key={t.id}>
              <button
                className={`w-full text-left p-5 hover:bg-[#f6f8f2] ${current?.id === t.id ? "bg-[#f0f5eb]" : ""}`}
                onClick={() => setSelected(t.id)}
                aria-pressed={current?.id === t.id}
              >
                <div className="flex justify-between gap-3">
                  <span className="font-bold text-sm">{t.destination.label}</span>
                  <span className="font-bold text-sm whitespace-nowrap">
                    {t.quote ? money(t.quote.amount) : "Đang báo giá"}
                  </span>
                </div>
                <div className="flex justify-between flex-wrap gap-2 mt-2 text-xs text-[#62766b]">
                  <span>{new Date(t.createdAt).toLocaleString("vi-VN")}</span>
                  <span>{labels[t.status]}</span>
                </div>
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
