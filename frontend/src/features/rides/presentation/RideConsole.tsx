"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { RouteMap } from "./components/RouteMap";
import { AddressField } from "./components/AddressField";
import type { Trip, Point, RoadRoute } from "../domain/models";
import { labels, money } from "./format";

type Ride = Pick<
  Trip,
  "id" | "pickup" | "destination" | "status" | "version" | "quote" | "history" | "reason"
>;
type Screen = {
  trip: Ride;
  payment: { status: string; amount: number; method: string } | null;
  paymentUnavailable: boolean;
};
const places = [
  { label: "Nhà hát Thành phố", latitude: 10.7769, longitude: 106.7009 },
  { label: "Đại học Sài Gòn", latitude: 10.7599, longitude: 106.6822 },
  { label: "Chợ Bến Thành", latitude: 10.7725, longitude: 106.698 },
  { label: "Bưu điện Thành phố", latitude: 10.7799, longitude: 106.6999 },
];
const normalize = (text: string) =>
  text.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase().replaceAll("đ", "d");
const searchService = {
  async search(query: string): Promise<Point[]> {
    const local = places.filter((p) => normalize(p.label).includes(normalize(query)));
    const coordinates = query.match(/^(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)$/);
    if (coordinates) {
      const latitude = Number(coordinates[1]),
        longitude = Number(coordinates[2]);
      return Math.abs(latitude) <= 90 && Math.abs(longitude) <= 180
        ? [{ label: query, latitude, longitude }]
        : [];
    }
    const params = new URLSearchParams({ q: query, lat: "10.7769", lon: "106.7009", limit: "6" });
    try {
      const res = await fetch("https://photon.komoot.io/api/?" + params, {
        signal: AbortSignal.timeout(5000),
      });
      if (!res.ok) throw new Error();
      const data = await res.json();
      for (const { geometry, properties: p } of data.features) {
        const [longitude, latitude] = geometry.coordinates;
        const label = [
          p.name,
          [p.housenumber, p.street].filter(Boolean).join(" "),
          p.district,
          p.city,
        ]
          .filter(Boolean)
          .join(", ")
          .slice(0, 120);
        if (
          label &&
          Number.isFinite(latitude) &&
          Number.isFinite(longitude) &&
          !local.some((point) => point.label === label)
        )
          local.push({ label, latitude, longitude });
      }
    } catch {
      if (!local.length)
        throw new Error(
          "Tìm địa chỉ trực tuyến tạm thời gián đoạn. Hãy chọn trên bản đồ hoặc nhập vĩ độ, kinh độ.",
        );
    }
    return local;
  },
};
function JourneyMap({
  pickup,
  destination,
  onSelect,
}: {
  pickup: Point | null;
  destination: Point | null;
  onSelect?: (lat: number, lon: number) => void;
}) {
  const path =
    pickup && destination
      ? [pickup.longitude, pickup.latitude].join(",") +
        ";" +
        [destination.longitude, destination.latitude].join(",")
      : "";
  const [result, setResult] = useState<{ path: string; route: RoadRoute | null } | null>(null);
  useEffect(() => {
    if (!path) return;
    const controller = new AbortController();
    const timer = setTimeout(async () => {
      try {
        const res = await fetch(
          "https://router.project-osrm.org/route/v1/driving/" +
            path +
            "?overview=simplified&geometries=geojson",
          { signal: AbortSignal.any([controller.signal, AbortSignal.timeout(6000)]) },
        );
        if (!res.ok) throw new Error();
        const { routes } = await res.json();
        const first = routes?.[0];
        if (!first?.geometry?.coordinates?.length) throw new Error();
        if (!controller.signal.aborted)
          setResult({
            path,
            route: {
              coordinates: first.geometry.coordinates,
              distanceKm: first.distance / 1000,
              durationMinutes: first.duration / 60,
            },
          });
      } catch {
        if (!controller.signal.aborted) setResult({ path, route: null });
      }
    }, 400);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [path]);
  const route = result?.path === path ? result.route : null;
  return (
    <div className="dispatch-map">
      <div className="dispatch-canvas">
        <RouteMap
          mapKey=""
          pickup={pickup}
          destination={destination}
          route={route}
          simulation={null}
          onSelect={onSelect}
        />
      </div>
      {route ? (
        <p className="dispatch-distance">
          Tuyến đường tham khảo · {route.distanceKm.toFixed(1)} km ·{" "}
          {Math.max(1, Math.round(route.durationMinutes))} phút
        </p>
      ) : (
        path &&
        result?.path === path && (
          <p className="dispatch-distance">Chưa tải được tuyến đường giữa hai điểm.</p>
        )
      )}
    </div>
  );
}
const closed = new Set(["Paid", "Cancelled", "Failed", "PaymentReview"]);
const commands: Record<string, string> = {
  "confirm-quote": "Xác nhận giá",
  cancel: "Hủy chuyến",
  "driver-accept": "Nhận chuyến",
  "driver-reject": "Từ chối",
  "en-route": "Đang đến đón",
  pickup: "Đã đón khách",
  dropoff: "Đã trả khách",
};
const driverActions: Record<string, string[]> = {
  AwaitingDriverAcceptance: ["driver-accept", "driver-reject"],
  DriverAccepted: ["en-route"],
  EnRoute: ["pickup"],
  PickedUp: ["dropoff"],
};
const riderActions: Record<string, string[]> = {
  Requested: ["cancel"],
  AwaitingRiderConfirmation: ["confirm-quote", "cancel"],
  AwaitingDriverAcceptance: ["cancel"],
  DriverAccepted: ["cancel"],
  EnRoute: ["cancel"],
};
const paymentLabels: Record<string, string> = {
  Settled: "Đã thu",
  Declined: "Bị từ chối",
  Refunded: "Đã hoàn",
};

async function api<T>(path: string, body?: object, key?: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch("/api/backend/" + path, {
    method: body ? "POST" : "GET",
    cache: "no-store",
    signal,
    headers: { "Content-Type": "application/json", ...(key && { "Idempotency-Key": key }) },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (response.status === 401) {
    window.location.assign("/login");
    throw new Error("Phiên đăng nhập đã hết hạn.");
  }
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(
      problem.detail || `Yêu cầu thất bại (HTTP ${response.status}). Vui lòng thử lại.`,
    );
  }
  return response.json();
}

export function RideConsole({ role }: { role: "rider" | "driver" }) {
  const [panel, setPanel] = useState<"home" | "booking" | "history" | "trip">("home");
  const [pickup, setPickup] = useState<Point | null>(places[0]);
  const [destination, setDestination] = useState<Point | null>(null);
  const [target, setTarget] = useState<"pickup" | "destination">("destination");
  const rider = role === "rider";
  const [rides, setRides] = useState<Ride[]>([]);
  const [selected, select] = useState("");
  const [screen, setScreen] = useState<Screen | null>(null);
  const [busy, setBusy] = useState(false);
  const [ready, setReady] = useState(false);
  const [error, setError] = useState("");
  const [readError, setReadError] = useState("");
  const [reason, setReason] = useState("");
  const [refresh, reload] = useState(0);
  const pending = useRef({ payload: "", key: "" });
  const lock = useRef(false);
  useEffect(() => {
    const controller = new AbortController();
    const signal = controller.signal;
    let timer: ReturnType<typeof setTimeout>;
    async function poll() {
      try {
        const list = await api<Ride[]>("trips", undefined, undefined, signal);
        const current = list.find((t) => t.id === selected);
        const active = list.find((t) => !closed.has(t.status));
        const id =
          panel === "home" ||
          (!rider && closed.has(current?.status ?? "") && panel === "trip" && active)
            ? active?.id
            : (current?.id ?? (panel === "trip" ? active?.id : undefined));
        const detail = id
          ? await api<Screen>("screens/trips/" + id, undefined, undefined, signal)
          : null;
        if (signal.aborted) return;
        if (selected && selected !== id) select(id ?? "");
        setRides(list);
        setScreen(detail);
        setReady(true);
        setReadError("");
      } catch (e) {
        if (!signal.aborted) setReadError(e instanceof Error ? e.message : "Mất kết nối.");
      } finally {
        if (!signal.aborted) timer = setTimeout(poll, 2200);
      }
    }
    void poll();
    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, [selected, refresh, panel, rider]);

  const trip = screen?.trip;
  const actions = trip ? ((rider ? riderActions : driverActions)[trip.status] ?? []) : [];
  async function submit(path: string, body: object, key?: string) {
    if (lock.current) return;
    lock.current = true;
    setBusy(true);
    setError("");
    try {
      const updated = await api<Ride>(path, body, key);
      select(updated.id);
      setPanel(path.endsWith("/driver-reject") ? "home" : "trip");
      setReason("");
      if (key) pending.current = { payload: "", key: "" };
      setScreen({ trip: updated, payment: null, paymentUnavailable: false });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Yêu cầu thất bại.");
    } finally {
      lock.current = false;
      setBusy(false);
      reload((n) => n + 1);
    }
  }
  function book(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    if (!pickup || !destination) {
      setError("Hãy chọn cả điểm đón và điểm đến.");
      return;
    }
    if (pickup.latitude === destination.latitude && pickup.longitude === destination.longitude) {
      setError("Hãy chọn điểm đến khác điểm đón.");
      return;
    }
    const method = form.get("method");
    const body = {
      pickup,
      destination,
      method,
      ...(method === "Card" && { paymentToken: "card-ok" }),
    };
    const payload = JSON.stringify(body);
    if (pending.current.payload !== payload)
      pending.current = { payload, key: crypto.randomUUID() };
    void submit("trips", body, pending.current.key);
  }
  function act(action: string) {
    if (!trip) return;
    if (
      action === "cancel" &&
      ["DriverAccepted", "EnRoute"].includes(trip.status) &&
      !reason.trim()
    ) {
      setError("Vui lòng nhập lý do hủy chuyến.");
      return;
    }
    void submit(`trips/${trip.id}/${action}`, {
      expectedVersion: trip.version,
      ...(action === "confirm-quote" && { quoteId: trip.quote?.id }),
      ...(action === "cancel" && { reason: reason.trim() || null }),
    });
  }

  const booking = panel === "booking";
  const showTrip = !!trip && (panel === "trip" || (panel === "home" && !closed.has(trip.status)));
  const canBook =
    rider && ready && !rides.some((t) => !closed.has(t.status) && t.status !== "PaymentPending");
  function open(next: typeof panel) {
    setError("");
    setReason("");
    setPanel(next);
    if (next !== "trip") select("");
    if (next === "booking") {
      setDestination(null);
      setTarget("destination");
    }
  }
  return (
    <div className="dispatch-stage">
      <JourneyMap
        pickup={showTrip ? trip.pickup : pickup}
        destination={showTrip ? trip.destination : booking ? destination : null}
        onSelect={
          booking && !busy
            ? (latitude, longitude) => {
                const point = {
                  label: latitude.toFixed(5) + ", " + longitude.toFixed(5),
                  latitude,
                  longitude,
                };
                (target === "pickup" ? setPickup : setDestination)(point);
              }
            : undefined
        }
      />
      <section className="dispatch-panel">
        <h1 className="sr-only">{rider ? "Chuyến đi của bạn" : "Lời mời và hành trình"}</h1>
        {(error || readError) && (
          <p role="alert" className="mb-3 text-red-800">
            {error || readError}
          </p>
        )}
        {panel === "history" ? (
          <>
            <button type="button" className="dispatch-back" onClick={() => open("home")}>
              ← Trang chính
            </button>
            <h2>Lịch sử chuyến</h2>
            {rides.length === 0 && <p>Chưa có chuyến đi.</p>}
            <ul className="dispatch-list">
              {rides.map((t) => (
                <li key={t.id}>
                  <button
                    type="button"
                    onClick={() => {
                      select(t.id);
                      setScreen(null);
                      open("trip");
                      reload((n) => n + 1);
                    }}
                  >
                    <strong>{t.destination.label}</strong>
                    <span>{labels[t.status]}</span>
                  </button>
                </li>
              ))}
            </ul>
          </>
        ) : booking ? (
          <>
            <button type="button" className="dispatch-back" onClick={() => open("home")}>
              ← Trang chính
            </button>
            <h2>Đặt xe</h2>
            <form className="grid gap-3" onSubmit={book}>
              <AddressField
                label="Điểm đón"
                value={pickup}
                onChange={setPickup}
                service={searchService}
                disabled={busy}
                onFocus={() => setTarget("pickup")}
              />
              <AddressField
                label="Điểm đến"
                value={destination}
                onChange={setDestination}
                service={searchService}
                disabled={busy}
                onFocus={() => setTarget("destination")}
              />
              <label className="dispatch-option">
                Chọn trên bản đồ
                <select
                  value={target}
                  onChange={(e) =>
                    setTarget(e.target.value === "pickup" ? "pickup" : "destination")
                  }
                >
                  <option value="pickup">Điểm đón</option>
                  <option value="destination">Điểm đến</option>
                </select>
              </label>
              <label className="dispatch-option">
                Thanh toán
                <select name="method">
                  <option value="Wallet">Ví SwiftRide</option>
                  <option value="Card">Thẻ mô phỏng</option>
                </select>
              </label>
              <button className="btn" disabled={busy || !canBook || !pickup || !destination}>
                Đặt chuyến
              </button>
            </form>
          </>
        ) : showTrip ? (
          <>
            <p className="badge my-4" role="status">
              {labels[trip.status] ?? trip.status}
            </p>
            <p className="text-lg font-semibold">
              {trip.pickup.label} → {trip.destination.label}
            </p>
            {trip.quote && (
              <p className="my-4">
                Giá chuyến <strong className="ml-2 text-2xl">{money(trip.quote.amount)}</strong>
              </p>
            )}
            {trip.reason && <p className="muted">{trip.reason}</p>}
            {actions.includes("cancel") && ["DriverAccepted", "EnRoute"].includes(trip.status) && (
              <label>
                Lý do hủy chuyến
                <input
                  className="field"
                  value={reason}
                  maxLength={240}
                  onChange={(e) => setReason(e.target.value)}
                />
              </label>
            )}
            <div className="my-4 flex flex-wrap gap-3">
              {actions.map((a, i) => (
                <button
                  key={a}
                  className={i || a === "cancel" ? "btn btn-secondary" : "btn"}
                  disabled={busy || (!!selected && selected !== trip.id)}
                  onClick={() => act(a)}
                >
                  {commands[a]}
                </button>
              ))}
            </div>
            {screen?.payment && (
              <p className="mb-4 font-semibold">
                Thanh toán · {paymentLabels[screen.payment.status] ?? screen.payment.status}{" "}
                {money(screen.payment.amount)} ({screen.payment.method})
              </p>
            )}
            {screen?.paymentUnavailable && <p role="status">Tạm thời chưa đọc được thanh toán.</p>}
            <details>
              <summary>Diễn biến chuyến</summary>{" "}
              <ol className="mt-2 grid gap-2">
                {trip.history.map((h, i) => (
                  <li className="flex gap-4" key={i}>
                    <time className="muted" dateTime={h.at}>
                      {new Date(h.at).toLocaleTimeString("vi-VN")}
                    </time>
                    {labels[h.status] ?? h.status}
                  </li>
                ))}
              </ol>
            </details>
            {canBook && (
              <button className="btn mt-3 w-full" onClick={() => open("booking")}>
                Đặt chuyến mới
              </button>
            )}
          </>
        ) : rider ? (
          <>
            <button className="dispatch-search" disabled={!canBook} onClick={() => open("booking")}>
              <span aria-hidden="true">⌕</span>Bạn muốn đi đâu?
            </button>
          </>
        ) : (
          <h2>{ready ? "Đang chờ lời mời" : "Đang tải…"}</h2>
        )}
        {panel !== "history" && !booking && (
          <button className="dispatch-history" onClick={() => open("history")}>
            Lịch sử chuyến <span aria-hidden="true">→</span>
          </button>
        )}
      </section>
    </div>
  );
}
