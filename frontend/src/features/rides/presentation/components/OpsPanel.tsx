"use client";
import { useCallback, useEffect, useState } from "react";
import type { RideService } from "../../application/RideService";
import type { DriverApplication, DriverRegistration, PaymentStatistic, TripStatistic, Trip } from "../../domain/models";
import { labels, money } from "../format";

const fields = { username: "Tên đăng nhập", name: "Họ tên", email: "Email", phone: "Số điện thoại", plate: "Biển số", vehicle: "Loại xe" };
const empty: DriverRegistration = { username: "", name: "", email: "", phone: "", plate: "", vehicle: "" };
export function OpsPanel({ service, run, busy, current }: {
  service: RideService;
  run: (action: () => Promise<void>) => Promise<void>;
  busy: boolean;
  current?: Trip;
}) {
  const [applications, setApplications] = useState<DriverApplication[]>([]);
  const [registration, setRegistration] = useState(empty);
  const [payments, setPayments] = useState<PaymentStatistic[]>([]);
  const [trips, setTrips] = useState<TripStatistic[]>([]);
  const [selected, setSelected] = useState<DriverApplication | null>(null);
  const [password, setPassword] = useState("");
  const [reason, setReason] = useState("");
  const [error, setError] = useState("");
  const [page, setPage] = useState(1);
  const reload = useCallback(async () => {
    const [drivers, paymentStats, tripStats] = await Promise.all([service.applications(), service.paymentStatistics(), service.tripStatistics()]);
    setApplications(drivers); setPayments(paymentStats); setTrips(tripStats);
  }, [service]);
  useEffect(() => { void reload().catch(e => setError(e.message)); }, [reload]);
  function act(action: () => Promise<void>) { void run(async () => { setError(""); await action(); await reload(); }); }
  return <section id="operations" className="card p-6 mt-6 space-y-5">
    <div className="flex justify-between gap-3"><h2 className="font-bold text-xl">Vận hành hệ thống</h2><button className="underline text-sm" disabled={busy} onClick={() => act(reload)}>Làm mới</button></div>
    {error && <p role="alert" className="text-sm text-red-800">{error}</p>}
    <div className="grid sm:grid-cols-2 gap-4">
      <div className="bg-[#f3f6ee] rounded-xl p-4"><h3 className="font-bold">Chuyến đi</h3>{trips.map(stat => <p key={stat.status} className="text-sm mt-2">{labels[stat.status] ?? stat.status}: {stat.count}</p>)}</div>
      <div className="bg-[#f3f6ee] rounded-xl p-4"><h3 className="font-bold">Thanh toán</h3>{payments.map(stat => <p key={stat.status + stat.isTopUp} className="text-sm mt-2">{stat.isTopUp ? "Nạp ví" : "Cước xe"} · {stat.status}: {stat.count} khoản · {money(stat.amount)}</p>)}</div>
    </div>
    {current?.status === "PaymentReview" && <button className="btn" disabled={busy} onClick={() => act(async () => { await service.retryPayment(current); })}>Thử lại thanh toán chuyến {current.id.slice(0, 8)}</button>}
    <details><summary className="font-bold cursor-pointer min-h-11">Nhập hồ sơ Driver</summary>
      <form className="grid sm:grid-cols-2 gap-4 mt-4" onSubmit={event => {
        event.preventDefault(); act(async () => { await service.registerDriver(registration); setRegistration(empty); });
      }}>
        {(Object.keys(fields) as (keyof DriverRegistration)[]).map(field => <label className="text-sm" key={field}>{fields[field]}
          <input className="field mt-2" required type={field === "email" ? "email" : field === "phone" ? "tel" : "text"} maxLength={field === "plate" ? 20 : field === "phone" ? 15 : field === "username" ? 60 : field === "email" ? 120 : 100}
            value={registration[field]} onChange={e => setRegistration({ ...registration, [field]: e.target.value })} />
        </label>)}
        <button className="btn" disabled={busy}>Lưu hồ sơ chờ duyệt</button>
      </form>
    </details>
    <div className="overflow-x-auto"><table className="w-full text-left text-sm"><caption className="text-left font-bold mb-3">Hồ sơ tài xế</caption>
      <thead><tr><th className="p-3">Tài xế</th><th className="p-3">Xe</th><th className="p-3">Trạng thái</th><th className="p-3">Thao tác</th></tr></thead>
      <tbody>{applications.slice((page - 1) * 10, page * 10).map(driver => <tr className="border-t border-[#e4e8e0]" key={driver.id}>
        <td className="p-3">{driver.name}<p className="text-xs">{driver.username} · {driver.phone}</p></td><td className="p-3">{driver.vehicle} · {driver.plate}</td><td className="p-3">{driver.status}{driver.reason && <p>{driver.reason}</p>}</td>
        <td className="p-3 flex flex-wrap gap-2">
          {["Pending", "Provisioning"].includes(driver.status) && <button className="btn btn-secondary" disabled={busy} onClick={() => { setSelected(driver); setPassword(""); setReason(""); }}>Xét duyệt</button>}
          {driver.status === "Approved" && driver.subject && <>
            <button className="btn btn-secondary" disabled={busy} onClick={() => act(async () => { await service.suspendDriver(driver.subject!, true); })}>Tạm khóa</button>
            <button className="btn btn-secondary" disabled={busy} onClick={() => act(async () => { await service.suspendDriver(driver.subject!, false); })}>Mở khóa</button>
          </>}
        </td>
      </tr>)}</tbody>
    </table></div>
    <div className="flex gap-4 items-center text-sm"><button className="btn btn-secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>Trang trước</button><span>Trang {page}</span><button className="btn btn-secondary" disabled={page * 10 >= applications.length} onClick={() => setPage(page + 1)}>Trang sau</button></div>
    {selected && <div className="border rounded-xl p-5 space-y-4">
      <h3 className="font-bold">Xét duyệt: {selected.name}</h3>
      <form className="space-y-3" onSubmit={event => {
        event.preventDefault(); act(async () => { await service.approveDriver(selected.id, password); setPassword(""); setSelected(null); });
      }}>
        <label className="block text-sm">Mật khẩu tạm (12–100 ký tự, có chữ hoa, chữ thường, số)
          <input className="field mt-2" type="password" autoComplete="new-password" required minLength={12} maxLength={100} value={password} onChange={e => setPassword(e.target.value)} />
        </label><button className="btn" disabled={busy}>Duyệt và cấp tài khoản</button>
        <p className="text-xs">Driver phải đổi mật khẩu khi đăng nhập lần đầu. Bàn giao mật khẩu trực tiếp cho tài xế.</p>
      </form>
      {selected.status === "Pending" && <form className="space-y-3" onSubmit={event => { event.preventDefault(); act(async () => { await service.rejectDriver(selected.id, reason.trim()); setSelected(null); }); }}>
        <label className="block text-sm">Lý do từ chối<input className="field mt-2" required maxLength={200} value={reason} onChange={e => setReason(e.target.value)} /></label>
        <button className="btn btn-secondary" disabled={busy || !reason.trim()}>Từ chối hồ sơ</button>
      </form>}
      <button className="underline text-sm min-h-11" onClick={() => { setSelected(null); setPassword(""); }}>Đóng</button>
    </div>}
  </section>;
}
