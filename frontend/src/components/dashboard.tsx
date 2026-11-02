"use client";
import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { api, labels, money, places, terminal, type Driver, type Payment, type Trip } from "@/lib/types";

export default function Dashboard({ user, logout }: { user: { id: string; name: string; roles: string[] }; logout: ReactNode }) {
  const isDriver = user.roles.includes("driver");
  const isRider = user.roles.includes("rider");
  const [trips, setTrips] = useState<Trip[]>([]);
  const [driver, setDriver] = useState<Driver | null>(null);
  const [balance, setBalance] = useState<number | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [from, setFrom] = useState(0);
  const [to, setTo] = useState(2);
  const [method, setMethod] = useState("Wallet");
  const [token, setToken] = useState("demo-success");
  const [selected, setSelected] = useState<string | null>(null);
  const [payment, setPayment] = useState<Payment | null>(null);
  const lock = useRef(false);
  const booking = useRef<{ payload: string; key: string } | null>(null);
  const active = trips.find(t => !terminal.has(t.status));
  const current = trips.find(t => t.id === selected) ?? active;

  const reload = useCallback(async () => {
    const data = await api<Trip[]>("trips"); setTrips(data);
    if (isDriver) setDriver(await api<Driver>("drivers/me"));
    if (isRider) setBalance((await api<{ balance: number }>("wallets/me")).balance);
  }, [isDriver, isRider]);
  useEffect(() => { let mounted = true; reload().catch(e => { if (mounted) setError(e.message); }).finally(() => { if (mounted) setLoading(false); }); return () => { mounted = false; }; }, [reload]);
  const needsPolling = isDriver || !!active;
  useEffect(() => {
    if (!needsPolling) return;
    let pending = false;
    const interval = setInterval(async () => {
      if (pending || document.hidden) return;
      pending = true;
      try { await reload(); } catch (e) { setError((e as Error).message); } finally { pending = false; }
    }, 2000);
    return () => clearInterval(interval);
  }, [reload, needsPolling]);
  useEffect(() => {
    if (!isDriver || !driver?.available) return;
    const ping = () => api("drivers/me/location", { latitude: driver.latitude, longitude: driver.longitude }, "PUT").catch(e => setError(e.message));
    void ping(); const interval = setInterval(ping, 10000); return () => clearInterval(interval);
  }, [isDriver, driver?.available, driver?.latitude, driver?.longitude]);
  useEffect(() => {
    setPayment(null);
    if (!current || !["Paid", "PaymentPending", "PaymentReview"].includes(current.status)) return;
    let mounted = true;
    api<{ payment: Payment | null; paymentUnavailable: boolean }>(`screens/trips/${current.id}`).then(data => {
      if (mounted) { setPayment(data.payment); if (data.paymentUnavailable) setError("Thông tin thanh toán tạm thời chưa tải được."); }
    }).catch(e => { if (mounted) setError(e.message); });
    return () => { mounted = false; };
  }, [current?.id, current?.status, current?.version]);
  async function run(action: () => Promise<void>) {
    if (lock.current) return;
    lock.current = true; setBusy(true); setError("");
    try { await action(); await reload(); } catch (e) { setError((e as Error).message); }
    finally { lock.current = false; setBusy(false); }
  }
  function act(trip: Trip, action: string) {
    void run(async () => { await api(`trips/${trip.id}/${action}`, { expectedVersion: trip.version, ...(action === "confirm-quote" ? { quoteId: trip.quote?.id } : {}) }); });
  }
  async function book() {
    if (from === to) throw new Error("Điểm đón và điểm đến phải khác nhau.");
    const payload = JSON.stringify({ pickup: places[from], destination: places[to], method, paymentToken: method === "Card" ? token : null });
    if (booking.current?.payload !== payload) booking.current = { payload, key: crypto.randomUUID() };
    const trip = await api<Trip>("trips", JSON.parse(payload), "POST", booking.current.key);
    setTrips(previous => [trip, ...previous.filter(t => t.id !== trip.id)]);
    setSelected(trip.id); booking.current = null;
  }
  const complete = trips.filter(t => t.status === "Paid").length;
  return <div className="min-h-screen lg:grid lg:grid-cols-[240px_1fr]">
    <aside className="bg-[#164b40] text-white p-5 lg:p-7 lg:min-h-screen flex lg:flex-col justify-between gap-6">
      <div><a href="/" className="text-2xl font-black tracking-tight">swift<span className="text-[#b4d59a]">ride</span></a>
        <p className="hidden lg:block text-xs tracking-[.2em] text-[#c5d6cc] mt-2">ĐI GẦN HƠN</p>
        <nav aria-label="Điều hướng chính" className="hidden lg:block mt-14 space-y-3">
          <a href="#main" className="block rounded-xl bg-white/10 px-4 py-3 text-sm font-bold" aria-current="page">{isDriver ? "Bảng điều khiển" : "Đặt chuyến xe"}</a>
          <a href="#history" className="block px-4 py-3 text-sm text-[#d0ded5]">Lịch sử chuyến đi</a>
        </nav></div>
      <div className="text-sm lg:border-t lg:border-white/20 lg:pt-6"><p className="font-bold">{user.name}</p><p className="text-[#c5d6cc] text-xs mb-1">{isDriver ? "Tài xế" : isRider ? "Người đi xe" : "Vận hành"}</p>{logout}</div>
    </aside>
    <main id="main" className="p-5 sm:p-8 xl:p-10 max-w-[1500px] w-full mx-auto">
      <header className="flex flex-wrap items-start justify-between gap-4 mb-8"><div><p className="text-xs tracking-[.15em] text-[#587467] uppercase mb-2">{isDriver ? "Đồng hành cùng thành phố" : "Hôm nay, mình đi đâu?"}</p>
        <h1 className="text-3xl sm:text-4xl font-bold tracking-tight">{isDriver ? "Sẵn sàng cho chuyến mới" : "Chuyến đi của bạn"}</h1></div>
        <span className="rounded-full border border-[#cfdacb] bg-white px-4 py-2 text-xs text-[#426551]">TP. Hồ Chí Minh · Demo</span></header>
      {error && <div role="alert" className="mb-5 rounded-xl border border-[#e6b8a7] bg-[#fff3ec] p-4 text-sm text-[#842b18] flex justify-between gap-3"><p>{error}</p><button className="underline shrink-0" onClick={() => void run(reload)} disabled={busy}>Tải lại</button></div>}
      <div className="grid sm:grid-cols-3 gap-3 mb-7">
        <Stat label={isDriver ? "Trạng thái nhận chuyến" : "Số dư ví demo"} value={isDriver ? (driver?.available ? "Đang trực tuyến" : "Đang ngoại tuyến") : balance === null ? "—" : money(balance)} />
        <Stat label="Chuyến đã hoàn tất" value={String(complete).padStart(2, "0")} />
        <Stat label="Chuyến hiện tại" value={active ? labels[active.status] : "Sẵn sàng"} />
      </div>
      <div className="grid xl:grid-cols-[minmax(320px,420px)_1fr] gap-6 items-start">
        <div className="space-y-5">
          {isRider && <section className="card p-6"><div className="flex justify-between items-center mb-5"><h2 className="text-lg font-bold">Đặt một chuyến xe</h2><span className="text-xs rounded-full bg-[#edf3e7] text-[#416342] px-3 py-1">SwiftCar</span></div>
            <form onSubmit={e => { e.preventDefault(); void run(book); }} className="space-y-4">
              <label className="block text-sm font-bold">Điểm đón<select className="field mt-2 font-normal" value={from} onChange={e => setFrom(Number(e.target.value))} disabled={!!active || busy}>{places.map((p, i) => <option key={p.label} value={i}>{p.label}</option>)}</select></label>
              <label className="block text-sm font-bold">Điểm đến<select className="field mt-2 font-normal" value={to} onChange={e => setTo(Number(e.target.value))} disabled={!!active || busy}>{places.map((p, i) => <option key={p.label} value={i}>{p.label}</option>)}</select></label>
              <label className="block text-sm font-bold">Thanh toán<select className="field mt-2 font-normal" value={method} onChange={e => setMethod(e.target.value)} disabled={!!active || busy}><option value="Wallet">Ví SwiftRide · Tiền mẫu</option><option value="Card">Thẻ · Token mô phỏng</option></select></label>
              {method === "Card" && <label className="block text-sm font-bold">Kịch bản thẻ demo<select className="field mt-2 font-normal" value={token} onChange={e => setToken(e.target.value)} disabled={!!active || busy}><option value="demo-success">Thanh toán thành công</option><option value="demo-decline">Thẻ bị từ chối</option><option value="demo-transient">Lỗi tạm thời, phục hồi</option><option value="demo-unknown">Timeout sau thu tiền</option><option value="demo-compensate">Saga lỗi, hoàn tiền</option><option value="demo-unavailable">Hết lượt retry, chờ xử lý</option></select></label>}
              <button className="btn w-full mt-2" disabled={busy || !!active || loading} type="submit">{busy ? "Đang gửi yêu cầu…" : active ? "Bạn đang có chuyến" : "Tìm xe và xem giá"}</button>
              <p className="text-xs text-[#62766b] leading-relaxed">Giá được hiển thị trước khi xác nhận. Tuyến đường và thanh toán đều mô phỏng.</p>
            </form></section>}
          {isDriver && <section className="card p-6"><h2 className="text-lg font-bold mb-2">Nhận chuyến quanh bạn</h2><p className="text-sm text-[#62766b] mb-5">Bật trực tuyến để gửi vị trí demo và nhận lời mời. Lời mời có hiệu lực 60 giây.</p>
            <button className="btn w-full" disabled={busy || loading || !driver} onClick={() => void run(async () => {
              await api("drivers/me/location", { latitude: places[0].latitude, longitude: places[0].longitude }, "PUT");
              await api("drivers/me/availability", { available: !driver?.available }, "PUT");
            })}>{driver?.available ? "Chuyển ngoại tuyến" : "Bật nhận chuyến"}</button>
            {driver?.busy && <p className="text-sm mt-4 text-[#416342]">Bạn đang phục vụ một chuyến.</p>}</section>}
          {current && <TripPanel trip={current} isDriver={isDriver && current.driverId === user.id} isRider={isRider && current.riderId === user.id} busy={busy} act={act} payment={payment} />}
        </div>
        <div className="space-y-6"><DemoMap from={current?.pickup.label ?? places[from].label} to={current?.destination.label ?? places[to].label} />
          <section id="history" className="card overflow-hidden"><div className="p-5 border-b border-[#e4e8e0] flex justify-between items-center"><h2 className="font-bold text-lg">Chuyến đi gần đây</h2><button className="text-sm underline min-h-11" disabled={busy} onClick={() => void run(reload)}>Làm mới</button></div>
            {loading ? <p className="p-6 text-sm" role="status">Đang tải chuyến đi…</p> : !trips.length ? <div className="p-8"><p className="font-bold">Hành trình đầu tiên đang chờ</p><p className="text-sm text-[#62766b] mt-2">{isDriver ? "Bật trực tuyến và chờ lời mời từ Rider." : "Chọn điểm đón và điểm đến để bắt đầu."}</p></div> :
              <ul className="divide-y divide-[#e4e8e0]">{trips.slice(0, 8).map(t => <li key={t.id}><button className={`w-full text-left p-5 hover:bg-[#f6f8f2] ${current?.id === t.id ? "bg-[#f0f5eb]" : ""}`} onClick={() => setSelected(t.id)} aria-pressed={current?.id === t.id}>
                <div className="flex justify-between gap-3"><span className="font-bold text-sm">{t.destination.label}</span><span className="font-bold text-sm whitespace-nowrap">{t.quote ? money(t.quote.amount) : "Đang báo giá"}</span></div>
                <div className="flex justify-between flex-wrap gap-2 mt-2 text-xs text-[#62766b]"><span>{new Date(t.createdAt).toLocaleString("vi-VN")}</span><span>{labels[t.status]}</span></div></button></li>)}</ul>}
          </section>
          <p className="text-xs text-[#62766b]">Giá minh bạch · Trạng thái rõ ràng · Thanh toán có đối soát</p>
        </div>
      </div>
      {!isRider && !isDriver && <p className="card p-5 mt-5">Tài khoản vận hành: dùng API và kịch bản trong README để đối soát, hoàn tiền và phát lại thanh toán.</p>}
    </main>
  </div>;
}
function Stat({ label, value }: { label: string; value: string }) {
  return <div className="card px-5 py-4"><p className="text-xs text-[#62766b] mb-2">{label}</p><p className="text-lg font-bold truncate">{value}</p></div>;
}
function TripPanel({ trip: t, isDriver, isRider, busy, act, payment }: { trip: Trip; isDriver: boolean; isRider: boolean; busy: boolean; act: (t: Trip, action: string) => void; payment: Payment | null }) {
  const b = t.quote?.breakdown;
  return <section className="card p-6" aria-label="Chi tiết chuyến"><div className="flex justify-between gap-3 items-start"><h2 className="font-bold text-lg">Chi tiết chuyến</h2><span className="text-xs bg-[#edf3e7] px-2 py-1 rounded-lg">{t.id.slice(0, 8)}</span></div>
    <p className="text-sm font-bold text-[#12695b] my-4" role="status" aria-live="polite">{labels[t.status]}</p>
    <div className="border-l-2 border-[#90ad8b] pl-4 space-y-4 text-sm"><p><span className="block text-xs text-[#62766b]">ĐIỂM ĐÓN</span>{t.pickup.label}</p><p><span className="block text-xs text-[#62766b]">ĐIỂM ĐẾN</span>{t.destination.label}</p></div>
    {t.quote && <div className="bg-[#f3f6ee] rounded-xl p-4 my-5"><p className="text-xs text-[#62766b]">Giá chuyến đi · {t.method === "Wallet" ? "Ví demo" : "Thẻ demo"}</p><p className="text-3xl font-bold my-2">{money(t.quote.amount)}</p>
      {b && <details className="text-xs text-[#526c5b]"><summary className="cursor-pointer min-h-8">{b.distanceKm.toFixed(1)} km · {Math.ceil(b.durationMinutes)} phút ước lượng · Chi tiết giá</summary><dl className="grid grid-cols-2 gap-2 mt-2">
        <dt>Giá mỗi km</dt><dd>{money(b.perKm)}</dd><dt>Giá mỗi phút</dt><dd>{money(b.perMinute)}</dd><dt>Hệ số giá động</dt><dd>{b.surge}</dd><dt>Phụ phí</dt><dd>{money(b.toll)}</dd><dt>Hệ số khuyến mãi</dt><dd>{b.promotion}</dd><dt>Thuế</dt><dd>{b.tax * 100}%</dd></dl></details>}
      {t.status === "AwaitingRiderConfirmation" && <p className="text-xs mt-3">Giá có hiệu lực đến {new Date(t.quote.expiresAt).toLocaleTimeString("vi-VN")}.</p>}</div>}
    {t.reason && <p className="text-sm my-4 text-[#6b5a32]">{t.reason}</p>}
    {payment && <p className="text-sm my-4">Thanh toán: <strong>{({ Settled: "Đã thu tiền", Refunded: "Đã hoàn tiền", Compensated: "Đã hoàn tiền bồi thường", Declined: "Bị từ chối", Pending: "Chờ xử lý" } as Record<string, string>)[payment.status] ?? payment.status}</strong>{payment.reason && <span className="block text-xs mt-1">{payment.reason}</span>}</p>}
    <div className="flex flex-wrap gap-2 mt-5">
      {isRider && t.status === "AwaitingRiderConfirmation" && <button disabled={busy} className="btn flex-1" onClick={() => act(t, "confirm-quote")}>Đồng ý giá</button>}
      {isRider && ["Requested", "AwaitingRiderConfirmation", "AwaitingDriverAcceptance"].includes(t.status) && <button disabled={busy} className="btn btn-secondary" onClick={() => act(t, "cancel")}>{t.status === "AwaitingRiderConfirmation" ? "Từ chối giá" : "Hủy chuyến"}</button>}
      {isDriver && t.status === "AwaitingDriverAcceptance" && <><button disabled={busy} className="btn flex-1" onClick={() => act(t, "driver-accept")}>Nhận chuyến</button><button disabled={busy} className="btn btn-secondary" onClick={() => act(t, "driver-reject")}>Từ chối</button></>}
      {isDriver && ([ ["DriverAccepted", "en-route", "Đang đến đón"], ["EnRoute", "pickup", "Đã đón khách"], ["PickedUp", "dropoff", "Đã trả khách"] ].filter(([status]) => t.status === status).map(([, action, text]) => <button key={action} disabled={busy} className="btn w-full" onClick={() => act(t, action)}>{text}</button>))}
    </div>
    <details className="mt-5 border-t border-[#e4e8e0] pt-4 text-xs"><summary className="cursor-pointer text-[#526c5b] min-h-8">Lịch sử trạng thái</summary><ol className="space-y-2 mt-2">{t.history.map((h, i) => <li key={i}><span className="text-[#62766b]">{new Date(h.at).toLocaleTimeString("vi-VN")}</span> · {labels[h.status]}{h.reason && ` · ${h.reason}`}</li>)}</ol></details>
  </section>;
}
function DemoMap({ from, to }: { from: string; to: string }) {
  return <section className="card overflow-hidden" aria-label="Minh họa tuyến đường demo"><div className="map-grid relative h-[270px] sm:h-[340px]">
    <div className="absolute top-5 left-5 bg-white/95 rounded-xl px-4 py-2 text-xs font-bold shadow-sm">KHU VỰC TRUNG TÂM · TP.HCM</div>
    <svg viewBox="0 0 600 300" className="absolute inset-0 w-full h-full" aria-hidden="true"><path d="M470 -20 C400 80 500 160 550 320" fill="none" stroke="#cadfdc" strokeWidth="62" /><path d="M145 215 L145 155 L290 155 L290 85 L400 85" fill="none" stroke="white" strokeWidth="13" strokeLinecap="round" strokeLinejoin="round"/><path d="M145 215 L145 155 L290 155 L290 85 L400 85" fill="none" stroke="#24745c" strokeWidth="5" strokeLinecap="round" strokeLinejoin="round"/><circle cx="145" cy="215" r="11" fill="#164b40" stroke="white" strokeWidth="4"/><circle cx="400" cy="85" r="11" fill="#ba742b" stroke="white" strokeWidth="4"/></svg>
    <div className="absolute bottom-5 left-5 right-5 bg-white/95 rounded-xl p-3 text-sm shadow-sm flex justify-between gap-3"><span className="truncate">{from}</span><span aria-hidden="true">→</span><span className="truncate font-bold">{to}</span></div>
    </div><p className="px-5 py-3 text-xs text-[#62766b] border-t border-[#dce2d9]">Sơ đồ minh họa; không phải bản đồ hoặc tuyến đường thực tế.</p></section>;
}
