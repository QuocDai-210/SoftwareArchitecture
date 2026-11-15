"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import type { RideService } from "../application/RideService";
import type {
  Trip,
  Point,
  Simulation,
  Page,
  RoadRoute,
  Payment,
  AssignedDriver,
} from "../domain/models";
import { isActiveTrip, latestTrip, type TripAction } from "../domain/trip-rules";

export type RideUser = { id: string; name: string; roles: string[] };

export function useRides(user: RideUser, service: RideService) {
  const isRider = user.roles.includes("rider");
  const [trips, setTrips] = useState<Trip[]>([]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [from, setFrom] = useState<Point | null>(null);
  const [seedLocation, setSeedLocation] = useState<Point | null>(null);
  const [to, setTo] = useState<Point | null>(null);
  const [selecting, setSelecting] = useState<"pickup" | "destination">("pickup");
  const [payment, setPayment] = useState<Payment | null>(null);
  const [assignedDriver, setAssignedDriver] = useState<AssignedDriver | null>(null);
  const [paymentUnavailable, setPaymentUnavailable] = useState(false);
  const [simulation, setSimulation] = useState<Simulation | null>(null);
  const [preview, setPreview] = useState<RoadRoute | null>(null);
  const [estimate, setEstimate] = useState<Trip["quote"]>(null);
  const [routeLoading, setRouteLoading] = useState(false);
  const [routeError, setRouteError] = useState("");
  const [page, setPage] = useState(1);
  const [historyStatus, setHistoryStatus] = useState("");
  const [history, setHistory] = useState<Page<Trip>>({
    items: [],
    page: 1,
    pageSize: 10,
    total: 0,
  });
  const [selectedDetail, setSelectedDetail] = useState<Trip | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const lock = useRef(false);
  const pickupChanged = useRef(false);
  const sequence = useRef(0);
  const active = trips.find(isActiveTrip);
  const currentId = selected ?? active?.id;
  const current = latestTrip(
    selectedDetail?.id === currentId ? selectedDetail : null,
    trips.find((t) => t.id === currentId),
    history.items.find((t) => t.id === currentId),
  );

  const reload = useCallback(async () => {
    const request = ++sequence.current;
    const [data, past] = await Promise.all([
      service.trips(),
      service.history(page, historyStatus || undefined),
    ]);
    if (request !== sequence.current) return;
    setTrips(data);
    setHistory(past);
  }, [service, page, historyStatus]);

  useEffect(() => {
    let mounted = true;
    service
      .profile()
      .then((profile) => {
        if (!mounted || profile.latitude == null || profile.longitude == null) return;
        const point = {
          latitude: profile.latitude,
          longitude: profile.longitude,
          label: "Vị trí mô phỏng của bạn",
        };
        setSeedLocation(point);
        if (!pickupChanged.current) {
          setFrom(point);
          setSelecting("destination");
        }
      })
      .catch(() => {});
    return () => {
      mounted = false;
    };
  }, [service]);

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

  const needsPolling = !!active;
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
    setPreview(null);
    setEstimate(null);
    setRouteError("");
    if (!from || !to || active || !isRider) {
      setRouteLoading(false);
      return;
    }
    let mounted = true;
    setRouteLoading(true);
    const timer = setTimeout(() => {
      service
        .quote(from, to)
        .then((quote) => {
          if (mounted) {
            setEstimate(quote);
            setPreview(quote.route ?? null);
          }
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

  const currentStatus = current?.status;
  useEffect(() => {
    setSimulation(null);
    setPayment(null);
    setAssignedDriver(null);
    setPaymentUnavailable(false);
    if (!currentId) return;
    let mounted = true;
    let pending = false;
    const poll = async () => {
      if (pending) return;
      pending = true;
      try {
        const data = await service.screen(currentId);
        if (mounted) {
          setSelectedDetail((previous) =>
            previous?.id === data.trip.id ? latestTrip(data.trip, previous)! : data.trip,
          );
          setSimulation(data.simulation);
          setPayment(data.payment);
          setAssignedDriver(data.driver);
          setPaymentUnavailable(data.paymentUnavailable);
        }
      } catch (e) {
        if (mounted) setError((e as Error).message);
      } finally {
        pending = false;
      }
    };
    void poll();
    const interval =
      current && !isActiveTrip(current)
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

  function act(trip: Trip, action: TripAction, reason?: string) {
    const role = trip.riderId === user.id ? "rider" : "observer";
    return run(async () => {
      await service.act(trip, action, role, reason);
    });
  }

  async function book(pickupNote = "", method: "Cod" | "Wallet" = "Cod") {
    if (!from || !to || !preview) throw new Error("Chọn điểm đón và điểm đến có đường đi hợp lệ.");
    const trip = await service.book({ pickup: from, destination: to, method, pickupNote });
    setTrips((previous) => [trip, ...previous.filter((t) => t.id !== trip.id)]);
    setSelected(trip.id);
    window.location.hash = `ride/${trip.id}`;
    setPage(1);
  }

  function rebook(trip: Trip) {
    if (active) {
      setError("Hoàn tất hoặc hủy chuyến hiện tại trước khi đặt lại.");
      return;
    }
    pickupChanged.current = true;
    setFrom(trip.pickup);
    setTo(trip.destination);
    setSelected(null);
    setSelecting("pickup");
    window.location.hash = "ride";
  }

  function review(trip: Trip, rating: number, comment: string) {
    return run(async () => {
      await service.review(trip, rating, comment);
      const screen = await service.screen(trip.id);
      setSelectedDetail(screen.trip);
    });
  }

  function locate() {
    if (isRider && seedLocation && !busy && !active) {
      pickupChanged.current = true;
      setFrom({ ...seedLocation });
      setSelected(null);
      setSelecting("destination");
    }
  }

  function selectMap(latitude: number, longitude: number) {
    pickupChanged.current = true;
    if (busy || active) return;
    void run(async () => {
      const point = await service.reverse(latitude, longitude);
      setSelected(null);
      if (selecting === "pickup") {
        setFrom(point);
        setSelecting("destination");
      } else {
        setTo(point);
      }
    });
  }

  function chooseFrom(point: Point | null) {
    pickupChanged.current = true;
    setSelected(null);
    setFrom(point);
  }
  function chooseTo(point: Point | null) {
    setSelected(null);
    setTo(point);
  }
  return {
    isRider,
    trips,
    error,
    busy,
    loading,
    from,
    setFrom: chooseFrom,
    to,
    setTo: chooseTo,
    simulation,
    payment,
    assignedDriver,
    paymentUnavailable,
    preview,
    estimate,
    routeLoading,
    routeError,
    selecting,
    setSelecting,
    locate,
    selectMap,
    page,
    setPage,
    historyStatus,
    filterHistory: (status: string) => {
      setHistoryStatus(status);
      setPage(1);
    },
    history,
    setSelected,
    active,
    current,
    reload,
    run,
    act,
    book,
    rebook,
    review,
  };
}
export type RidesViewModel = ReturnType<typeof useRides>;
