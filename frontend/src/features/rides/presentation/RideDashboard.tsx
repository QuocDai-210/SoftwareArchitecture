"use client";
import { useEffect, useState, type ReactNode } from "react";
import type { RideService } from "../application/RideService";
import { useRides, type RideUser } from "./useRides";
import { labels, money } from "./format";
import { BookingForm } from "./components/BookingForm";
import { DriverAvailability } from "./components/DriverAvailability";
import { TripHistory } from "./components/TripHistory";
import { TripDetails } from "./components/TripDetails";
import { TripReview } from "./components/TripReview";
import { RouteMap } from "./components/RouteMap";
import { AccountPanel } from "./components/AccountPanel";
import { OpsPanel } from "./components/OpsPanel";
import { SimulationControls } from "./components/SimulationControls";
import { PaymentPanel } from "./components/PaymentPanel";
import { CashReceipt } from "./components/CashReceipt";
import { NavIcon, type View } from "./components/NavIcon";

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
  const [view, setView] = useState<View>(isOps ? "overview" : "ride");
  const [collapsed, setCollapsed] = useState(false);
  useEffect(() => {
    setCollapsed(false);
  }, [current?.id, current?.status]);
  const nav: { id: View; label: string }[] = [
    { id: "ride", label: isOps ? "Bản đồ" : isDriver ? "Nhận chuyến" : "Đặt xe" },
    { id: "history", label: "Lịch sử chuyến" },
    { id: "payments", label: "Tiền mặt" },
    ...(isOps
      ? [
          { id: "operations" as const, label: "Quản lý tài xế" },
          { id: "overview" as const, label: "Tổng quan" },
        ]
      : []),
    { id: "profile", label: "Hồ sơ cá nhân" },
  ];
  useEffect(() => {
    function sync() {
      const [value, id] = window.location.hash.slice(1).split("/");
      if (value === "ride" && id && /^[0-9a-f-]{36}$/i.test(id)) model.setSelected(id);
      const known: View[] = [
        "ride",
        "history",
        "payments",
        "profile",
        ...(isOps ? ["operations" as const, "overview" as const] : []),
      ];
      if (known.includes(value as View)) setView(value as View);
    }
    sync();
    window.addEventListener("hashchange", sync);
    return () => window.removeEventListener("hashchange", sync);
  }, [isOps, model.setSelected]);
  function openRide(id?: string) {
    if (id) model.setSelected(id);
    setCollapsed(false);
    window.location.hash = id ? `ride/${id}` : "ride";
    setView("ride");
  }
  const vehicle = driver
    ? { latitude: driver.latitude, longitude: driver.longitude, label: "Vị trí xe" }
    : null;
  const simulation = model.simulation?.tripId === current?.id ? model.simulation : null;
  const selecting = !current && ((isRider && !active) || (isDriver && !driver?.busy && !active));
  const roleName = isOps
    ? "Ops · Quản trị hệ thống"
    : isDriver
      ? "Driver · Tài xế"
      : "User · Người đặt xe";
  return (
    <div className={`app-shell ${view === "ride" ? "map-shell" : ""}`}>
      <header className="app-header">
        <a className="wordmark" href="#ride" aria-label="SwiftRide trang chủ">
          <span className="brand-symbol" aria-hidden="true">
            s
          </span>
          swift<span>ride</span>
        </a>
        <span className="city-label">
          TP. Hồ Chí Minh <span className="status-dot" />
        </span>
        <a className="account-link" href="#profile">
          <span className="avatar">{user.name.slice(0, 1).toUpperCase()}</span>
          <span>
            <strong>{user.name}</strong>
            <small>{roleName}</small>
          </span>
        </a>
      </header>
      <nav className="app-nav" aria-label="Điều hướng chính">
        {nav.map((item) => (
          <a
            key={item.id}
            href={`#${item.id}`}
            title={item.label}
            aria-current={view === item.id ? "page" : undefined}
          >
            <NavIcon name={item.id} />
            <span>{item.label}</span>
          </a>
        ))}
        <div className="nav-footer">
          <span className="text-xs">Đi gần hơn. Mỗi ngày.</span>
          {logout}
        </div>
      </nav>
      <main id="main" className={view === "ride" ? "app-main map-main" : "app-main content-main"}>
        {error && (
          <div role="alert" className="app-alert">
            <p>{error}</p>
            <button className="underline shrink-0" onClick={() => void run(reload)} disabled={busy}>
              Tải lại
            </button>
          </div>
        )}
        {view === "ride" ? (
          <div className="ride-stage">
            <RouteMap
              mapKey={mapKey}
              pickup={current?.pickup ?? (isDriver ? (model.driverPoint ?? vehicle) : model.from)}
              destination={current?.destination ?? (isDriver ? null : model.to)}
              route={current ? current.quote?.route : model.preview}
              simulation={simulation}
              onSelect={selecting ? model.selectMap : undefined}
              onLocate={selecting ? () => model.locate(isDriver) : undefined}
              locateLabel={isDriver ? "Dùng vị trí hiện tại" : "Dùng vị trí hiện tại làm điểm đón"}
              busy={busy}
            />
            <section className="ride-sheet" aria-label="Thao tác chuyến đi">
              <button
                className="sheet-toggle"
                onClick={() => setCollapsed(!collapsed)}
                aria-expanded={!collapsed}
                aria-controls="ride-sheet-content"
                aria-label={collapsed ? "Mở thao tác chuyến đi" : "Thu gọn thao tác chuyến đi"}
              >
                <span className="sheet-handle" />
                <span className="sheet-title">
                  <strong>
                    {current
                      ? labels[current.status]
                      : isOps
                        ? "Theo dõi chuyến"
                        : isDriver
                          ? "Vị trí và nhận chuyến"
                          : "Đặt xe"}
                  </strong>
                  {collapsed && (
                    <small>
                      {current?.quote
                        ? `${money(current.quote.amount)} · ${current.method === "Cod" ? "Tiền mặt" : current.method}`
                        : isDriver
                          ? driver?.available
                            ? "Đang trực tuyến"
                            : "Đang ngoại tuyến"
                          : "Mở bảng để tiếp tục"}
                    </small>
                  )}
                </span>
                <span className="sheet-chevron" aria-hidden="true">
                  {collapsed ? "+" : "−"}
                </span>
              </button>
              <div className="sheet-content" id="ride-sheet-content" hidden={collapsed}>
                {model.loading && (
                  <p className="p-6" role="status">
                    Đang tải chuyến đi…
                  </p>
                )}
                {isRider && !current && (
                  <BookingForm model={model} service={service} initialName={user.name} />
                )}
                {isDriver && !active && !current && (
                  <DriverAvailability model={model} service={service} />
                )}
                {isOps && !current && (
                  <div className="p-5">
                    <h1 className="text-xl font-bold">Chọn chuyến cần theo dõi</h1>
                    <p className="muted mt-3">
                      Chọn chuyến để xem vị trí xe, trạng thái và thanh toán tiền mặt.
                    </p>
                    <a className="btn mt-5 w-full" href="#history">
                      Xem danh sách chuyến
                    </a>
                  </div>
                )}
                {current && (
                  <>
                    {["PaymentPending", "PaymentReview", "Paid"].includes(current.status) && (
                      <CashReceipt
                        trip={current}
                        payment={model.payment}
                        unavailable={model.paymentUnavailable}
                        isDriver={isDriver && current.driverId === user.id}
                        busy={busy}
                        service={service}
                        run={run}
                      />
                    )}
                    {isOps && current.method === "Cod" && current.status === "PaymentReview" && (
                      <button
                        className="btn btn-secondary m-5"
                        disabled={busy}
                        onClick={() => void run(() => service.retryPayment(current))}
                      >
                        Thử lại tạo giao dịch
                      </button>
                    )}
                    <TripDetails
                      trip={current}
                      isDriver={isDriver && current.driverId === user.id}
                      isRider={isRider && current.riderId === user.id}
                      busy={busy}
                      act={act}
                      simulation={simulation}
                      assignedDriver={model.assignedDriver}
                    />
                    <TripReview
                      key={current.id}
                      trip={current}
                      isRider={isRider && current.riderId === user.id}
                      busy={busy}
                      submit={(rating, comment) => model.review(current, rating, comment)}
                    />
                    {(isDriver || isOps) &&
                      simulation &&
                      ["EnRoute", "PickedUp"].includes(current.status) && (
                        <details className="simulation-panel">
                          <summary>Điều khiển xe giả lập</summary>
                          <SimulationControls
                            simulation={simulation}
                            onChange={model.controlSimulation}
                            busy={busy}
                          />
                        </details>
                      )}
                    {!active && (
                      <>
                        {isRider && (
                          <button
                            className="btn m-5"
                            disabled={busy}
                            onClick={() => {
                              model.rebook(current);
                              setCollapsed(false);
                            }}
                          >
                            Đặt lại chuyến
                          </button>
                        )}
                        <button
                          className="btn btn-secondary m-5"
                          onClick={() => {
                            model.setSelected(null);
                            window.location.hash = "ride";
                          }}
                        >
                          {isRider ? "Đặt chuyến mới" : "Đóng chi tiết chuyến"}
                        </button>
                      </>
                    )}
                  </>
                )}
              </div>
            </section>
          </div>
        ) : (
          <div className="content-wrap">
            <header className="page-heading">
              <p className="eyebrow">SWIFTRIDE / {roleName}</p>
              <h1>{nav.find((item) => item.id === view)?.label}</h1>
              <p className="muted">
                {view === "payments"
                  ? "Thu và hoàn tiền rõ ràng, từng hành trình."
                  : view === "history"
                    ? "Xem lại hành trình và tiếp tục chuyến đang diễn ra."
                    : "Thông tin rõ ràng. Thao tác trong tầm tay."}
              </p>
            </header>
            {(view === "history" || view === "overview") && (
              <>
                {view === "overview" && (
                  <div className="overview-banner">
                    <div>
                      <p className="eyebrow">HOẠT ĐỘNG HỆ THỐNG</p>
                      <h2>
                        {model.history.total}{" "}
                        {model.historyStatus ? "chuyến theo bộ lọc" : "chuyến đi"}
                      </h2>
                      <p>
                        {active
                          ? labels[active.status]
                          : "Theo dõi hành trình, tài xế và các khoản thu tại một nơi."}
                      </p>
                    </div>
                    <a className="btn" href="#operations">
                      Quản lý tài xế
                    </a>
                  </div>
                )}
                <TripHistory
                  busy={busy}
                  loading={model.loading}
                  trips={model.history.items}
                  current={current}
                  isDriver={isDriver}
                  run={run}
                  reload={reload}
                  setSelected={openRide}
                  status={model.historyStatus}
                  onFilter={model.filterHistory}
                  onRebook={
                    isRider && !active
                      ? (trip) => {
                          model.rebook(trip);
                          setCollapsed(false);
                        }
                      : undefined
                  }
                />
                <div className="pagination">
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
              </>
            )}
            {view === "overview" && (
              <OpsPanel service={service} run={run} busy={busy} summaryOnly />
            )}
            {view === "operations" && isOps && <OpsPanel service={service} run={run} busy={busy} />}
            {view === "payments" && (
              <PaymentPanel
                service={service}
                run={run}
                busy={busy}
                isOps={isOps}
                isDriver={isDriver}
                onTrip={openRide}
              />
            )}
            {view === "profile" && (
              <>
                <AccountPanel service={service} run={run} busy={busy} initialName={user.name} />
                <div className="card p-5 mt-5 flex justify-between items-center">
                  <span className="muted">Kết thúc phiên làm việc</span>
                  {logout}
                </div>
              </>
            )}
          </div>
        )}
      </main>
    </div>
  );
}
