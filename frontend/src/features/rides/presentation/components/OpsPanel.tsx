"use client";
import { useCallback, useEffect, useState } from "react";
import type { RideService } from "../../application/RideService";
import type { DriverApplication, DriverRegistration, TripStatistic } from "../../domain/models";
import { labels } from "../format";

const fields = {
  username: "Tên đăng nhập",
  name: "Họ tên",
  email: "Email",
  phone: "Số điện thoại",
  plate: "Biển số",
  vehicle: "Loại xe",
};
const empty: DriverRegistration = {
  username: "",
  name: "",
  email: "",
  phone: "",
  plate: "",
  vehicle: "",
};
export function OpsPanel({
  service,
  run,
  busy,
  summaryOnly = false,
}: {
  service: RideService;
  run: (action: () => Promise<void>) => Promise<void>;
  busy: boolean;
  summaryOnly?: boolean;
}) {
  const [applications, setApplications] = useState<DriverApplication[]>([]);
  const [registration, setRegistration] = useState(empty);
  const [trips, setTrips] = useState<TripStatistic[]>([]);
  const [selected, setSelected] = useState<DriverApplication | null>(null);
  const [password, setPassword] = useState("");
  const [reason, setReason] = useState("");
  const [error, setError] = useState("");
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const reload = useCallback(async () => {
    const [drivers, tripStats] = await Promise.all([
      service.applications(page),
      service.tripStatistics(),
    ]);
    setApplications(drivers.items);
    setTotal(drivers.total);
    setTrips(tripStats);
  }, [service, page]);
  useEffect(() => {
    void reload().catch((e) => setError(e.message));
  }, [reload]);
  function act(action: () => Promise<void>) {
    void run(async () => {
      setError("");
      await action();
      await reload();
    });
  }
  if (summaryOnly)
    return (
      <section className="card p-6 mt-5">
        <h2 className="font-bold text-lg">Trạng thái chuyến đi</h2>
        <div className="stats-grid mt-4">
          {trips.map((stat) => (
            <div key={stat.status} className="rounded-xl bg-emerald-50 p-4">
              <p className="muted text-sm">{labels[stat.status] ?? stat.status}</p>
              <p className="text-2xl font-bold mt-2">{stat.count}</p>
            </div>
          ))}
        </div>
      </section>
    );
  return (
    <section id="operations" className="card p-6 mt-6 space-y-5">
      <div className="flex justify-between gap-3">
        <h2 className="font-bold text-xl">Vận hành hệ thống</h2>
        <button className="underline text-sm" disabled={busy} onClick={() => act(reload)}>
          Làm mới
        </button>
      </div>
      {error && (
        <p role="alert" className="text-sm text-red-800">
          {error}
        </p>
      )}
      <div className="grid sm:grid-cols-2 gap-4">
        <div className="bg-[#f0faf5] rounded-xl p-4">
          <h3 className="font-bold">Chuyến đi</h3>
          {trips.map((stat) => (
            <p key={stat.status} className="text-sm mt-2">
              {labels[stat.status] ?? stat.status}: {stat.count}
            </p>
          ))}
        </div>
        <div className="bg-[#f0faf5] rounded-xl p-4">
          <h3 className="font-bold">Hồ sơ tài xế · {total} hồ sơ</h3>
          <p className="text-sm mt-2">
            {
              applications.filter(
                (driver) => driver.status === "Pending" || driver.status === "Provisioning",
              ).length
            }{" "}
            hồ sơ cần xét duyệt trên trang này
          </p>
          <p className="text-sm mt-2">
            {applications.filter((driver) => driver.status === "Approved").length} hồ sơ đã duyệt
            trên trang này
          </p>
        </div>
      </div>
      <details>
        <summary className="font-bold cursor-pointer min-h-11">Nhập hồ sơ Driver</summary>
        <form
          className="grid sm:grid-cols-2 gap-4 mt-4"
          onSubmit={(event) => {
            event.preventDefault();
            act(async () => {
              await service.registerDriver(registration);
              setRegistration(empty);
            });
          }}
        >
          {(Object.keys(fields) as (keyof DriverRegistration)[]).map((field) => (
            <label className="text-sm" key={field}>
              {fields[field]}
              <input
                className="field mt-2"
                required
                type={field === "email" ? "email" : field === "phone" ? "tel" : "text"}
                maxLength={
                  field === "plate"
                    ? 20
                    : field === "phone"
                      ? 15
                      : field === "username"
                        ? 60
                        : field === "email"
                          ? 120
                          : 100
                }
                value={registration[field]}
                onChange={(e) => setRegistration({ ...registration, [field]: e.target.value })}
              />
            </label>
          ))}
          <button className="btn" disabled={busy}>
            Lưu hồ sơ chờ duyệt
          </button>
        </form>
      </details>
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <caption className="text-left font-bold mb-3">Hồ sơ tài xế</caption>
          <thead>
            <tr>
              <th className="p-3">Tài xế</th>
              <th className="p-3">Xe</th>
              <th className="p-3">Trạng thái</th>
              <th className="p-3">Thao tác</th>
            </tr>
          </thead>
          <tbody>
            {applications.map((driver) => (
              <tr className="border-t border-[#dcebe5]" key={driver.id}>
                <td className="p-3">
                  {driver.name}
                  <p className="text-xs">
                    {driver.username} · {driver.phone}
                  </p>
                </td>
                <td className="p-3">
                  {driver.vehicle} · {driver.plate}
                </td>
                <td className="p-3">
                  {driver.status}
                  {driver.reason && <p>{driver.reason}</p>}
                </td>
                <td className="p-3 flex flex-wrap gap-2">
                  {["Pending", "Provisioning"].includes(driver.status) && (
                    <button
                      className="btn btn-secondary"
                      disabled={busy}
                      onClick={() => {
                        setSelected(driver);
                        setPassword("");
                        setReason("");
                      }}
                    >
                      Xét duyệt
                    </button>
                  )}
                  {driver.status === "Approved" && driver.subject && (
                    <>
                      <button
                        className="btn btn-secondary"
                        disabled={busy}
                        onClick={() =>
                          act(async () => {
                            await service.suspendDriver(driver.subject!, true);
                          })
                        }
                      >
                        Tạm khóa
                      </button>
                      <button
                        className="btn btn-secondary"
                        disabled={busy}
                        onClick={() =>
                          act(async () => {
                            await service.suspendDriver(driver.subject!, false);
                          })
                        }
                      >
                        Mở khóa
                      </button>
                    </>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="flex gap-4 items-center text-sm">
        <button
          className="btn btn-secondary"
          disabled={busy || page <= 1}
          onClick={() => setPage(page - 1)}
        >
          Trang trước
        </button>
        <span>Trang {page}</span>
        <button
          className="btn btn-secondary"
          disabled={busy || page * 10 >= total}
          onClick={() => setPage(page + 1)}
        >
          Trang sau
        </button>
      </div>
      {selected && (
        <div className="border rounded-xl p-5 space-y-4">
          <h3 className="font-bold">Xét duyệt: {selected.name}</h3>
          <form
            className="space-y-3"
            onSubmit={(event) => {
              event.preventDefault();
              act(async () => {
                await service.approveDriver(selected.id, password);
                setPassword("");
                setSelected(null);
              });
            }}
          >
            <label className="block text-sm">
              Mật khẩu tạm (12–100 ký tự, có chữ hoa, chữ thường, số)
              <input
                className="field mt-2"
                type="password"
                autoComplete="new-password"
                required
                minLength={12}
                maxLength={100}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            </label>
            <button className="btn" disabled={busy}>
              Duyệt và cấp tài khoản
            </button>
            <p className="text-xs">
              Driver phải đổi mật khẩu khi đăng nhập lần đầu. Bàn giao mật khẩu trực tiếp cho tài
              xế.
            </p>
          </form>
          {selected.status === "Pending" && (
            <form
              className="space-y-3"
              onSubmit={(event) => {
                event.preventDefault();
                act(async () => {
                  await service.rejectDriver(selected.id, reason.trim());
                  setSelected(null);
                });
              }}
            >
              <label className="block text-sm">
                Lý do từ chối
                <input
                  className="field mt-2"
                  required
                  maxLength={200}
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                />
              </label>
              <button className="btn btn-secondary" disabled={busy || !reason.trim()}>
                Từ chối hồ sơ
              </button>
            </form>
          )}
          <button
            className="underline text-sm min-h-11"
            onClick={() => {
              setSelected(null);
              setPassword("");
            }}
          >
            Đóng
          </button>
        </div>
      )}
    </section>
  );
}
