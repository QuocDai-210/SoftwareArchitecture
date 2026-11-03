"use client";
import { useEffect, useRef, useState } from "react";
import type { Map as LeafletMap, LayerGroup } from "leaflet";
import type { Point, RoadRoute, Simulation } from "../../domain/models";

export function RouteMap({
  mapKey,
  pickup,
  destination,
  route,
  simulation,
  onSelect,
  onLocate,
  locateLabel = "Xem toàn tuyến",
  busy = false,
}: {
  mapKey: string;
  pickup: Point | null;
  destination: Point | null;
  route?: RoadRoute | null;
  simulation: Simulation | null;
  onSelect?: (latitude: number, longitude: number) => void;
  onLocate?: () => void;
  locateLabel?: string;
  busy?: boolean;
}) {
  const container = useRef<HTMLDivElement>(null);
  const map = useRef<LeafletMap | null>(null);
  const layers = useRef<LayerGroup | null>(null);
  const select = useRef(onSelect);
  const fit = useRef<(() => void) | null>(null);
  const fitted = useRef("");
  const [ready, setReady] = useState(false);
  const [error, setError] = useState("");
  const [tilesFailed, setTilesFailed] = useState(false);
  select.current = onSelect;
  useEffect(() => {
    if (!mapKey || !container.current) return;
    let disposed = false;
    setReady(false);
    setTilesFailed(false);
    fitted.current = "";
    import("leaflet")
      .then((L) => {
        if (disposed || !container.current) return;
        const instance = L.map(container.current, { zoomControl: false }).setView(
          [10.7769, 106.7009],
          13,
        );
        L.control
          .zoom({ position: "topright", zoomInTitle: "Phóng to", zoomOutTitle: "Thu nhỏ" })
          .addTo(instance);
        L.tileLayer(
          `https://maps.geoapify.com/v1/tile/positron/{z}/{x}/{y}.png?apiKey=${encodeURIComponent(mapKey)}`,
          {
            maxZoom: 20,
            attribution:
              '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors | <a href="https://www.geoapify.com/">Geoapify</a>',
          },
        )
          .on("tileerror", () => setTilesFailed(true))
          .on("load", () => setError(""))
          .addTo(instance);
        map.current = instance;
        layers.current = L.layerGroup().addTo(instance);
        instance.on("click", (event) => select.current?.(event.latlng.lat, event.latlng.lng));
        setReady(true);
      })
      .catch(() => setError("Không tải được bản đồ."));
    return () => {
      disposed = true;
      map.current?.remove();
      map.current = null;
      layers.current = null;
      fit.current = null;
    };
  }, [mapKey]);
  useEffect(() => {
    if (!ready) return;
    let disposed = false;
    import("leaflet").then((L) => {
      if (disposed || !map.current || !layers.current) return;
      const group = layers.current;
      group.clearLayers();
      for (const [point, color, label] of [
        [pickup, "#087b66", "Điểm đón"],
        [destination, "#183b35", "Điểm đến"],
      ] as const) {
        if (point)
          L.circleMarker([point.latitude, point.longitude], {
            radius: 7,
            color: "#fff",
            weight: 3,
            fillColor: color,
            fillOpacity: 1,
          })
            .bindTooltip(label)
            .addTo(group);
      }
      const visibleRoute = simulation?.route ?? route;
      if (visibleRoute)
        L.polyline(
          visibleRoute.coordinates.map((p) => [p[1], p[0]] as [number, number]),
          { color: "#087b66", weight: 4, className: "ride-route" },
        ).addTo(group);
      if (simulation)
        L.marker([simulation.currentPosition.latitude, simulation.currentPosition.longitude], {
          title: "Xe",
          alt: "Xe",
          icon: L.divIcon({
            className: "vehicle-marker",
            iconSize: [36, 36],
            iconAnchor: [18, 18],
            html: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><circle cx="5" cy="17" r="4"/><circle cx="19" cy="17" r="4"/><path d="m5 17 5-7 5 7H5m8-13h4l2 13M7 9h5"/></svg>',
          }),
        })
          .bindTooltip("Xe")
          .addTo(group);
      fit.current = () => {
        const instance = map.current;
        if (!instance || !container.current) return;
        const frame = container.current.getBoundingClientRect();
        const sheet = container.current
          .closest(".ride-stage")
          ?.querySelector(".ride-sheet")
          ?.getBoundingClientRect();
        const desktop = window.innerWidth >= 1024;
        const signature = JSON.stringify([
          pickup,
          destination,
          visibleRoute?.coordinates,
          simulation?.tripId,
          simulation?.stage,
          frame.width,
          frame.height,
          sheet?.width,
          sheet?.height,
        ]);
        if (signature === fitted.current) return;
        const points: [number, number][] =
          visibleRoute?.coordinates.map((p) => [p[1], p[0]] as [number, number]) ?? [];
        for (const point of [pickup, destination, simulation?.currentPosition]) {
          if (point) points.push([point.latitude, point.longitude]);
        }
        if (!points.length) return;
        const bottom = desktop ? 30 : frame.bottom - (sheet?.top ?? frame.bottom - 90) + 16;
        const left = desktop ? (sheet?.right ?? 80) - frame.left + 24 : 24;
        instance.fitBounds(L.latLngBounds(points), {
          paddingTopLeft: [Math.min(left, frame.width * 0.5), 80],
          paddingBottomRight: [desktop ? 80 : 70, Math.min(bottom, frame.height - 140)],
          maxZoom: 17,
          animate: false,
        });
        fitted.current = signature;
      };
      fit.current();
    });
    return () => {
      disposed = true;
    };
  }, [ready, pickup, destination, route, simulation]);
  useEffect(() => {
    if (!ready || !container.current) return;
    const observer = new ResizeObserver(() => {
      map.current?.invalidateSize({ pan: false });
      fit.current?.();
    });
    observer.observe(container.current);
    const sheet = container.current.closest(".ride-stage")?.querySelector(".ride-sheet");
    if (sheet) observer.observe(sheet);
    return () => observer.disconnect();
  }, [ready]);
  return (
    <section className="route-map" aria-label="Bản đồ hành trình">
      <div ref={container} className="map-canvas relative z-0" />
      <button
        type="button"
        className="map-locate"
        disabled={busy || (!onLocate && !ready)}
        aria-label={onLocate ? locateLabel : "Xem toàn tuyến"}
        title={onLocate ? locateLabel : "Xem toàn tuyến"}
        onClick={() => {
          fitted.current = "";
          if (onLocate) onLocate();
          else fit.current?.();
        }}
      >
        <svg
          width="22"
          height="22"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.8"
          aria-hidden="true"
        >
          <circle cx="12" cy="12" r="7" />
          <circle cx="12" cy="12" r="2" />
          <path d="M12 2v3m0 14v3M2 12h3m14 0h3" />
        </svg>
        <span className="sr-only">{onLocate ? locateLabel : "Xem toàn tuyến"}</span>
      </button>
      {(!mapKey || error || tilesFailed) && (
        <p role="status" className="map-notice">
          {error ||
            (tilesFailed
              ? "Một số ô bản đồ chưa tải được. Có thể tiếp tục tìm địa chỉ hoặc chọn điểm."
              : "Bản đồ chưa sẵn sàng. Dùng tìm địa chỉ để đặt xe.")}
        </p>
      )}
    </section>
  );
}
