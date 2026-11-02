"use client";
import type { ReactNode } from "react";
import type { RideService } from "../application/RideService";
import { useRides, type RideUser } from "./useRides";
import { labels } from "./format";
import { Stat } from "./components/Stat";
import { BookingForm } from "./components/BookingForm";
import { DriverAvailability } from "./components/DriverAvailability";
import { TripHistory } from "./components/TripHistory";
import { TripDetails } from "./components/TripDetails";
import { RouteMap } from "./components/RouteMap";
import { AccountPanel } from "./components/AccountPanel";
import { OpsPanel } from "./components/OpsPanel";
import { SimulationControls } from "./components/SimulationControls";

export function RideDashboard({
  user,
  logout,
  service,
  mapKey,
}: {
  user: RideUser;
  logout: ReactNode;
  service: RideService;
  mapKey: string;
}) {
  const model = useRides(user, service);
  const { isDriver, isRider, isOps, driver, error, busy, active, current, reload, run, act } =
    model;
  const roleName = isOps
    ? "Ops · Quản trị hệ thống"
    : isDriver
      ? "Driver · Tài xế"
      : "User · Người đặt xe";
  const vehicle = driver
    ? { latitude: driver.latitude, longitude: driver.longitude, label: "Vị trí xe" }
    : null;
  const selecting = (isRider && !active) || (isDriver && !driver?.busy && !active);
  return (
    <div className="min-h-screen lg:grid lg:grid-cols-[240px_1fr]">
      <aside className="bg-[#164b40] text-white p-5 lg:p-7 lg:min-h-screen flex lg:flex-col justify-between gap-6">
        <div>
          <a href="/" className="text-2xl font-black tracking-tight">
            swift<span className="text-[#b4d59a]">ride</span>
          </a>
          <p className="text-xs tracking-widest text-[#c5d6cc] mt-2">ĐI GẦN HƠN</p>
          <nav
            aria-label="Điều hướng chính"
            className="flex flex-wrap lg:block mt-5 lg:mt-12 gap-2 lg:space-y-3 text-sm"
          >
            <a href="#main" className="block rounded-xl bg-white/10 px-3 py-3">
              {isOps ? "Theo dõi chuyến" : isDriver ? "Nhận chuyến" : "Đặt xe"}
            </a>
            <a href="#history" className="block px-3 py-3">
              Lịch sử chuyến
            </a>
            {isOps && (
              <a href="#operations" className="block px-3 py-3">
                Quản lý tài xế
              </a>
            )}
            <a href="#profile" className="block px-3 py-3">
              Hồ sơ cá nhân
            </a>
          </nav>
        </div>
        <div className="text-sm lg:border-t lg:border-white/20 lg:pt-6">
          <p className="font-bold">{user.name}</p>
          <p className="text-[#c5d6cc] text-xs mb-2">{roleName}</p>
          {logout}
        </div>
      </aside>
      <main id="main" className="p-5 sm:p-8 xl:p-10 max-w-[1500px] w-full mx-auto">
        <header className="flex flex-wrap items-start justify-between gap-4 mb-8">
          <div>
            <p className="text-xs tracking-widest text-[#587467] uppercase mb-2">{roleName}</p>
            <h1 className="text-3xl sm:text-4xl font-bold tracking-tight">
              {isOps
                ? "Vận hành SwiftRide"
                : isDriver
                  ? "Đồng hành cùng thành phố"
                  : "Hôm nay, mình đi đâu?"}
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
            label={isDriver ? "Trạng thái nhận chuyến" : "Tổng số chuyến"}
            value={
              isDriver
                ? driver?.available
                  ? "Đang trực tuyến"
                  : "Đang ngoại tuyến"
                : String(model.history.total)
            }
          />
          <Stat label="Hành trình đã hoàn tất gần đây" value={String(model.complete)} />
          <Stat
            label="Chuyến hiện tại"
            value={active ? (labels[active.status] ?? active.status) : "Sẵn sàng"}
          />
        </div>
        <div className="grid xl:grid-cols-[minmax(300px,400px)_1fr] gap-6 items-start">
          <div className="space-y-5">
            {isRider && <BookingForm model={model} service={service} />}
            {isDriver && <DriverAvailability model={model} service={service} />}
            {isOps && (
              <p className="card p-5 text-sm">
                Chọn chuyến trong lịch sử để xem trạng thái và theo dõi vị trí xe. Hồ sơ tài xế được
                nhập và xét duyệt trong phần quản lý.
              </p>
            )}
            {current && (
              <>
                <TripDetails
                  trip={current}
                  isDriver={isDriver && current.driverId === user.id}
                  isRider={isRider && current.riderId === user.id}
                  busy={busy}
                  act={act}
                  simulation={model.simulation}
                />
                {!active && (
                  <button
                    className="btn btn-secondary w-full"
                    onClick={() => model.setSelected(null)}
                  >
                    {isRider ? "Đặt chuyến mới" : "Đóng chi tiết chuyến"}
                  </button>
                )}
              </>
            )}
          </div>
          <div className="space-y-5">
            <RouteMap
              mapKey={mapKey}
              pickup={current?.pickup ?? (isDriver ? (model.driverPoint ?? vehicle) : model.from)}
              destination={current?.destination ?? (isDriver ? null : model.to)}
              route={current?.quote?.route ?? model.preview}
              simulation={model.simulation}
              onSelect={selecting ? model.selectMap : undefined}
            />
            {(isDriver || isOps) &&
              model.simulation &&
              current &&
              ["EnRoute", "PickedUp"].includes(current.status) && (
                <SimulationControls
                  simulation={model.simulation}
                  onChange={model.controlSimulation}
                  busy={busy}
                />
              )}
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
              <button
                className="btn btn-secondary"
                disabled={busy || model.page <= 1}
                onClick={() => model.setPage(model.page - 1)}
              >
                Trang trước
              </button>
              <span>
                Trang {model.page} · {model.history.total} chuyến
              </span>
              <button
                className="btn btn-secondary"
                disabled={busy || model.page * model.history.pageSize >= model.history.total}
                onClick={() => model.setPage(model.page + 1)}
              >
                Trang sau
              </button>
            </div>
          </div>
        </div>
        {isOps && <OpsPanel service={service} run={run} busy={busy} />}
        <AccountPanel service={service} run={run} busy={busy} initialName={user.name} />
      </main>
    </div>
  );
}
