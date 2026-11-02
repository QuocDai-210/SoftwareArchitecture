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
}: {
  mapKey: string;
  pickup: Point | null;
  destination: Point | null;
  route?: RoadRoute | null;
  simulation: Simulation | null;
  onSelect?: (latitude: number, longitude: number) => void;
}) {
  const container = useRef<HTMLDivElement>(null);
  const map = useRef<LeafletMap | null>(null);
  const layers = useRef<LayerGroup | null>(null);
  const select = useRef(onSelect);
  const [ready, setReady] = useState(false);
  const [error, setError] = useState("");
  const [tilesFailed, setTilesFailed] = useState(false);
  const fitted = useRef("");
  select.current = onSelect;
  useEffect(() => {
    if (!mapKey || !container.current) return;
    let disposed = false;
    setReady(false);
    import("leaflet")
      .then((L) => {
        if (disposed || !container.current) return;
        const instance = L.map(container.current).setView([10.7769, 106.7009], 13);
        L.tileLayer(
          `https://maps.geoapify.com/v1/tile/osm-carto/{z}/{x}/{y}.png?apiKey=${encodeURIComponent(mapKey)}`,
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
        [pickup, "#166534", "Điểm đón"],
        [destination, "#b45309", "Điểm đến"],
      ] as const) {
        if (point)
          L.circleMarker([point.latitude, point.longitude], { radius: 8, color, fillOpacity: 1 })
            .bindTooltip(label)
            .addTo(group);
      }
      if (route)
        L.polyline(
          route.coordinates.map((p) => [p[1], p[0]] as [number, number]),
          { color: "#16836d", weight: 5 },
        ).addTo(group);
      if (simulation) {
        const points = simulation.route.coordinates;
        L.polyline(
          points.map((p) => [p[1], p[0]] as [number, number]),
          { color: "#b45309", weight: 4, dashArray: "6 5" },
        ).addTo(group);
        const position = [
          simulation.currentPosition.longitude,
          simulation.currentPosition.latitude,
        ];
        if (position)
          L.circleMarker([position[1], position[0]], {
            radius: 11,
            color: "#1e40af",
            fillOpacity: 1,
          })
            .bindTooltip("Xe")
            .addTo(group);
      }
      const signature = JSON.stringify([
        pickup,
        destination,
        route?.coordinates.length,
        simulation?.tripId,
        simulation?.stage,
      ]);
      const selected = [pickup, destination, simulation?.currentPosition ?? null].filter(
        (point): point is Point => point !== null,
      );
      if (signature !== fitted.current && selected.length > 0) {
        map.current.fitBounds(L.latLngBounds(selected.map((p) => [p.latitude, p.longitude])), {
          paddingTopLeft: window.innerWidth >= 1024 ? [435, 55] : [40, 60],
          paddingBottomRight:
            window.innerWidth >= 1024
              ? [45, 45]
              : [40, Math.min(map.current.getSize().y * 0.5, 340)],
          maxZoom: 15,
        });
        fitted.current = signature;
      }
    });
    return () => {
      disposed = true;
    };
  }, [ready, pickup, destination, route, simulation]);
  return (
    <section className="route-map" aria-label="Bản đồ hành trình">
      <div ref={container} className="map-canvas relative z-0" />
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
