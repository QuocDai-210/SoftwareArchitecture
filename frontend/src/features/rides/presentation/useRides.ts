"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import type { RideService } from "../application/RideService";
import type { Driver, Trip, Point, Simulation, Page, RoadRoute } from "../domain/models";
import { terminal, type TripAction } from "../domain/trip-rules";

export type RideUser = { id: string; name: string; roles: string[] };

export function useRides(user: RideUser, service: RideService) {
  const isDriver = user.roles.includes("driver");
  const isRider = user.roles.includes("user");
  const isOps = user.roles.includes("ops");
  const [trips, setTrips] = useState<Trip[]>([]);
  const [driver, setDriver] = useState<Driver | null>(null);
  const [driverPoint, setDriverPoint] = useState<Point | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [from, setFrom] = useState<Point | null>(null);
  const [to, setTo] = useState<Point | null>(null);
  const [selecting, setSelecting] = useState<"pickup" | "destination">("pickup");
  const [simulation, setSimulation] = useState<Simulation | null>(null);
  const [preview, setPreview] = useState<RoadRoute | null>(null);
  const [routeLoading, setRouteLoading] = useState(false);
  const [routeError, setRouteError] = useState("");
  const [page, setPage] = useState(1);
  const [history, setHistory] = useState<Page<Trip>>({
    items: [],
    page: 1,
    pageSize: 10,
    total: 0,
  });
  const [selected, setSelected] = useState<string | null>(null);
  const lock = useRef(false);
  const sequence = useRef(0);
  const screenSequence = useRef(0);
  const active = trips.find((t) => !terminal.has(t.status));
  const current =
    trips.find((t) => t.id === selected) ?? history.items.find((t) => t.id === selected) ?? active;

  const reload = useCallback(async () => {
    const request = ++sequence.current;
    const [data, past, presence] = await Promise.all([
      service.trips(),
      service.history(page),
      isDriver ? service.driver() : Promise.resolve(null),
    ]);
    if (request !== sequence.current) return;
    setTrips(data);
    setHistory(past);
    setDriver(presence);
  }, [service, isDriver, page]);

  useEffect(() => {
    let mounted = true;
    reload()
      .catch((e) => {
        if (mounted) setError(e.message);
      })
      .finally(() => {
        if (mounted) setLoading(false);
      });
    return () => {
      mounted = false;
      sequence.current++;
    };
  }, [reload]);

  const needsPolling = isDriver || isOps || !!active;
  useEffect(() => {
    if (!needsPolling) return;
    let pending = false;
    const interval = setInterval(async () => {
      if (pending || document.hidden) return;
      pending = true;
      try {
        await reload();
      } catch (e) {
        setError((e as Error).message);
      } finally {
        pending = false;
      }
    }, 2000);
    return () => clearInterval(interval);
  }, [reload, needsPolling]);

  useEffect(() => {
    if (!isDriver || !driver?.available) return;
    const ping = () => service.heartbeat().catch((e) => setError(e.message));
    void ping();
    const interval = setInterval(ping, 10000);
    return () => clearInterval(interval);
  }, [service, isDriver, driver?.available]);

  useEffect(() => {
    setPreview(null);
    setRouteError("");
    if (!from || !to || active || !isRider) {
      setRouteLoading(false);
      return;
    }
    let mounted = true;
    setRouteLoading(true);
    const timer = setTimeout(() => {
      service
        .route(from, to)
        .then((route) => {
          if (mounted) setPreview(route);
        })
        .catch((e) => {
          if (mounted) setRouteError(e.message);
        })
        .finally(() => {
          if (mounted) setRouteLoading(false);
        });
    }, 400);
    return () => {
      mounted = false;
      clearTimeout(timer);
    };
  }, [service, from, to, !!active, isRider]);

  const currentId = current?.id;
  const currentStatus = current?.status;
  useEffect(() => {
    setSimulation(null);
    if (!currentId) return;
    let mounted = true;
    let pending = false;
    const poll = async () => {
      if (pending) return;
      pending = true;
      const request = screenSequence.current;
      try {
        const data = await service.screen(currentId);
        if (mounted && request === screenSequence.current) {
          setSimulation(data.simulation);
        }
      } catch (e) {
        if (mounted) setError((e as Error).message);
      } finally {
        pending = false;
      }
    };
    void poll();
    const interval = terminal.has(currentStatus ?? "")
      ? null
      : setInterval(() => {
          if (!document.hidden) void poll();
        }, 1000);
    return () => {
      mounted = false;
      if (interval) clearInterval(interval);
    };
  }, [service, currentId, currentStatus]);

  async function run(action: () => Promise<void>) {
    if (lock.current) return;
    lock.current = true;
    setBusy(true);
    setError("");
    try {
      await action();
      await reload();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      lock.current = false;
      setBusy(false);
    }
  }

  function controlSimulation(speed: number, paused: boolean) {
    if (!simulation) return;
    void run(async () => {
      const updated = await service.control(simulation.driverId, speed, paused);
      screenSequence.current++;
      setSimulation(updated);
    });
  }

  function act(trip: Trip, action: TripAction) {
    const role =
      isRider && trip.riderId === user.id
        ? "rider"
        : isDriver && trip.driverId === user.id
          ? "driver"
          : "observer";
    void run(async () => {
      await service.act(trip, action, role);
    });
  }

  async function book() {
    if (!from || !to || !preview) throw new Error("Chọn điểm đón và điểm đến có đường đi hợp lệ.");
    const trip = await service.book({
      pickup: from,
      destination: to,
      method: "Wallet",
      paymentToken: null,
    });
    setTrips((previous) => [trip, ...previous.filter((t) => t.id !== trip.id)]);
    setSelected(trip.id);
    setPage(1);
  }

  function toggleAvailability() {
    if (!driver) return;
    const point = driverPoint ?? driver;
    void run(() => service.toggleAvailability(driver, point.latitude, point.longitude));
  }

  function locate(asDriver = false) {
    if (!navigator.geolocation) {
      setError("Trình duyệt không hỗ trợ định vị.");
      return;
    }
    navigator.geolocation.getCurrentPosition(
      (position) => {
        void run(async () => {
          const point = await service.reverse(position.coords.latitude, position.coords.longitude);
          if (asDriver) setDriverPoint(point);
          else {
            setSelected(null);
            setFrom(point);
          }
        });
      },
      () => setError("Không lấy được vị trí. Chọn điểm trên bản đồ hoặc tìm địa chỉ."),
      { timeout: 10000 },
    );
  }

  function selectMap(latitude: number, longitude: number) {
    if (busy || (isRider && active) || (isDriver && driver?.busy)) return;
    void run(async () => {
      const point = await service.reverse(latitude, longitude);
      if (isDriver) setDriverPoint(point);
      else {
        setSelected(null);
        if (selecting === "pickup") {
          setFrom(point);
          setSelecting("destination");
        } else setTo(point);
      }
    });
  }

  function chooseFrom(point: Point | null) {
    setSelected(null);
    setFrom(point);
  }
  function chooseTo(point: Point | null) {
    setSelected(null);
    setTo(point);
  }
  const complete = trips.filter((t) =>
    ["DroppedOff", "PaymentPending", "PaymentReview", "Paid"].includes(t.status),
  ).length;
  return {
    isDriver,
    isRider,
    isOps,
    trips,
    driver,
    driverPoint,
    setDriverPoint,
    error,
    busy,
    loading,
    from,
    setFrom: chooseFrom,
    to,
    setTo: chooseTo,
    simulation,
    preview,
    routeLoading,
    routeError,
    selecting,
    setSelecting,
    locate,
    selectMap,
    page,
    setPage,
    history,
    setSelected,
    active,
    current,
    reload,
    run,
    act,
    book,
    toggleAvailability,
    controlSimulation,
    complete,
  };
}
export type RidesViewModel = ReturnType<typeof useRides>;
