"use client";
import type { ReactNode } from "react";
import type { RideService } from "../application/RideService";
import { useRides, type RideUser } from "./useRides";
import { labels, money } from "./format";
import { Stat } from "./components/Stat";
import { BookingForm } from "./components/BookingForm";
import { DriverAvailability } from "./components/DriverAvailability";
import { TripHistory } from "./components/TripHistory";
import { TripDetails } from "./components/TripDetails";
import { RouteMap } from "./components/RouteMap";
import { AccountPanel } from "./components/AccountPanel";
import { PaymentPanel } from "./components/PaymentPanel";
import { OpsPanel } from "./components/OpsPanel";
import { SimulationControls } from "./components/SimulationControls";
export function RideDashboard({
  user,
  logout,
  service,
  mapKey,
  paymentReference,
}: {
  user: RideUser;
  logout: ReactNode;
  service: RideService;
  mapKey: string;
  paymentReference?: string;
}) {
  const model = useRides(user, service);
  const {
    isDriver,
    isRider,
    driver,
    balance,
    error,
    busy,
    from,
    to,
    payment,
    active,
    current,
    reload,
    run,
    act,
    complete,
  } = model;
  return (
    <div className="min-h-screen lg:grid lg:grid-cols-[240px_1fr]">
      <aside className="bg-[#164b40] text-white p-5 lg:p-7 lg:min-h-screen flex lg:flex-col justify-between gap-6">
        <div>
          <a href="/" className="text-2xl font-black tracking-tight">
            swift<span className="text-[#b4d59a]">ride</span>
          </a>
          <p className="hidden lg:block text-xs tracking-[.2em] text-[#c5d6cc] mt-2">ĐI GẦN HƠN</p>
          <nav aria-label="Điều hướng chính" className="hidden lg:block mt-14 space-y-3">
            <a
              href="#main"
              className="block rounded-xl bg-white/10 px-4 py-3 text-sm font-bold"
              aria-current="page"
            >
              {isDriver ? "Bảng điều khiển" : "Đặt chuyến xe"}
            </a>
            <a href="#history" className="block px-4 py-3 text-sm text-[#d0ded5]">
              Lịch sử chuyến đi
            </a>
          </nav>
        </div>
        <div className="text-sm lg:border-t lg:border-white/20 lg:pt-6">
          <p className="font-bold">{user.name}</p>
          <p className="text-[#c5d6cc] text-xs mb-1">
            {isDriver ? "Tài xế" : isRider ? "Người đi xe" : "Vận hành"}
          </p>
          {logout}
        </div>
      </aside>
      <main id="main" className="p-5 sm:p-8 xl:p-10 max-w-[1500px] w-full mx-auto">
        <header className="flex flex-wrap items-start justify-between gap-4 mb-8">
          <div>
            <p className="text-xs tracking-[.15em] text-[#587467] uppercase mb-2">
              {isDriver ? "Đồng hành cùng thành phố" : "Hôm nay, mình đi đâu?"}
            </p>
            <h1 className="text-3xl sm:text-4xl font-bold tracking-tight">
              {isDriver ? "Sẵn sàng cho chuyến mới" : "Chuyến đi của bạn"}
            </h1>
          </div>
          <span className="rounded-full border border-[#cfdacb] bg-white px-4 py-2 text-xs text-[#426551]">
            TP. Hồ Chí Minh
          </span>
        </header>
        {error && (
          <div
            role="alert"
            className="mb-5 rounded-xl border border-[#e6b8a7] bg-[#fff3ec] p-4 text-sm text-[#842b18] flex justify-between gap-3"
          >
            <p>{error}</p>
            <button className="underline shrink-0" onClick={() => void run(reload)} disabled={busy}>
              Tải lại
            </button>
          </div>
        )}
        <div className="grid sm:grid-cols-3 gap-3 mb-7">
          <Stat
            label={isDriver ? "Trạng thái nhận chuyến" : "Số dư ví"}
            value={
              isDriver
                ? driver?.available
                  ? "Đang trực tuyến"
                  : "Đang ngoại tuyến"
                : balance === null
                  ? "—"
                  : money(balance)
            }
          />
          <Stat label="Chuyến đã hoàn tất" value={String(complete).padStart(2, "0")} />
          <Stat label="Chuyến hiện tại" value={active ? labels[active.status] : "Sẵn sàng"} />
        </div>
        <div className="grid xl:grid-cols-[minmax(320px,420px)_1fr] gap-6 items-start">
          <div className="space-y-5">
            {isRider && (
              <BookingForm model={model} service={service} />
            )}
            {isDriver && (
              <DriverAvailability
                busy={busy}
                loading={model.loading}
                driver={driver}
                toggleAvailability={model.toggleAvailability}
              />
            )}
            {current && (
              <TripDetails
                trip={current}
                isDriver={isDriver && current.driverId === user.id}
                isRider={isRider && current.riderId === user.id}
                busy={busy}
                act={act}
                payment={payment}
              />
            )}
          </div>
          <div className="space-y-6">
            <RouteMap mapKey={mapKey} pickup={current?.pickup ?? from} destination={current?.destination ?? to}
              route={current?.quote?.route} simulation={model.simulation} onSelect={isRider && !active ? model.selectMap : undefined} />
            {(isDriver || user.roles.includes("ops")) && model.simulation && <SimulationControls simulation={model.simulation} service={service} run={run} />}
            <TripHistory
              busy={busy}
              loading={model.loading}
              trips={model.history.items}
              current={current}
              isDriver={isDriver}
              run={run}
              reload={reload}
              setSelected={model.setSelected}
            />
            <div className="flex items-center justify-between gap-3 text-sm">
              <button className="btn btn-secondary" disabled={busy || model.page <= 1} onClick={() => model.setPage(model.page - 1)}>Trang trước</button>
              <span>Trang {model.page} · {model.history.total} chuyến</span>
              <button className="btn btn-secondary" disabled={busy || model.page * model.history.pageSize >= model.history.total} onClick={() => model.setPage(model.page + 1)}>Trang sau</button>
            </div>
            <p className="text-xs text-[#62766b]">
              Giá minh bạch · Trạng thái rõ ràng · Thanh toán có đối soát
            </p>
          </div>
        </div>
        {user.roles.includes("ops") && <OpsPanel service={service} run={run} busy={busy} current={current} />}
        {(isRider || user.roles.includes("ops")) && <PaymentPanel service={service} run={run} busy={busy} isOps={user.roles.includes("ops")} paymentReference={paymentReference} />}
        <AccountPanel service={service} run={run} busy={busy} />
      </main>
    </div>
  );
}
