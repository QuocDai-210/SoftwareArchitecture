"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import type { RideService } from "../application/RideService";
import type { Driver, Payment, Trip, Point, Simulation, Page } from "../domain/models";
import { terminal, type TripAction } from "../domain/trip-rules";
export type RideUser = { id: string; name: string; roles: string[] };
export function useRides(user: RideUser, service: RideService) {
  const isDriver = user.roles.includes("driver");
  const isRider = user.roles.includes("user");
  const [trips, setTrips] = useState<Trip[]>([]);
  const [driver, setDriver] = useState<Driver | null>(null);
  const [balance, setBalance] = useState<number | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [from, setFrom] = useState<Point | null>(null);
  const [to, setTo] = useState<Point | null>(null);
  const [selecting, setSelecting] = useState<"pickup" | "destination">("pickup");
  const [simulation, setSimulation] = useState<Simulation | null>(null);
  const [page, setPage] = useState(1);
  const [history, setHistory] = useState<Page<Trip>>({ items: [], page: 1, pageSize: 10, total: 0 });
  const [method, setMethod] = useState<"Card" | "Wallet">("Wallet");
  const [selected, setSelected] = useState<string | null>(null);
  const [payment, setPayment] = useState<Payment | null>(null);
  const lock = useRef(false);
  const active = trips.find((t) => !terminal.has(t.status));
  const current = trips.find((t) => t.id === selected) ?? history.items.find(t => t.id === selected) ?? active;

  const reload = useCallback(async () => {
    const data = await service.trips();
    setTrips(data);
    setHistory(await service.history(page));
    if (isDriver) setDriver(await service.driver());
    if (isRider) setBalance(await service.balance());
  }, [service, isDriver, isRider, page]);
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
    };
  }, [reload]);
  const needsPolling = isDriver || !!active;
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
    const ping = () =>
      service.heartbeat().catch((e) => setError(e.message));
    void ping();
    const interval = setInterval(ping, 10000);
    return () => clearInterval(interval);
  }, [service, isDriver, driver?.available]);
  useEffect(() => {
    if (!current) { setPayment(null); setSimulation(null); return; }
    let mounted = true;
    let pending = false;
    const poll = async () => {
      if (pending) return;
      pending = true;
      try {
        const data = await service.screen(current.id);
        if (mounted) {
          setPayment(data.payment);
          setSimulation(data.simulation);
          if (data.paymentUnavailable) setError("Thông tin thanh toán tạm thời chưa tải được.");
        }
      } catch (e) { if (mounted) setError((e as Error).message); }
      finally { pending = false; }
    };
    void poll();
    const interval = setInterval(() => { if (!document.hidden) void poll(); }, 2000);
    return () => { mounted = false; clearInterval(interval); };
  }, [service, current?.id]);
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
    if (!from || !to) throw new Error("Chọn điểm đón và điểm đến.");
    const trip = await service.book({ pickup: from, destination: to, method, paymentToken: null });
    setTrips((previous) => [trip, ...previous.filter((t) => t.id !== trip.id)]);
    setSelected(trip.id);
  }
  function toggleAvailability() {
    if (driver) void run(() => service.toggleAvailability(driver, driver.latitude, driver.longitude));
  }
  function locate() {
    if (!navigator.geolocation) { setError("Trình duyệt không hỗ trợ định vị."); return; }
    navigator.geolocation.getCurrentPosition(position => {
      void run(async () => { setFrom(await service.reverse(position.coords.latitude, position.coords.longitude)); });
    }, () => setError("Không lấy được vị trí. Chọn điểm trên bản đồ hoặc tìm địa chỉ."), { timeout: 10000 });
  }
  function selectMap(latitude: number, longitude: number) {
    if (active || busy) return;
    void run(async () => {
      const point = await service.reverse(latitude, longitude);
      if (selecting === "pickup") { setFrom(point); setSelecting("destination"); } else setTo(point);
    });
  }
  const complete = trips.filter((t) => t.status === "Paid").length;
  return {
    isDriver,
    isRider,
    trips,
    driver,
    balance,
    error,
    busy,
    loading,
    from,
    setFrom,
    to,
    setTo,
    method,
    setMethod,
    simulation,
    selecting,
    setSelecting,
    locate,
    selectMap,
    page,
    setPage,
    history,
    setSelected,
    payment,
    active,
    current,
    reload,
    run,
    act,
    book,
    toggleAvailability,
    complete,
  };
}
export type RidesViewModel = ReturnType<typeof useRides>;
