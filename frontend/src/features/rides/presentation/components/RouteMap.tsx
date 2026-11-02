"use client";
import { useEffect, useRef, useState } from "react";
import type { Map as LeafletMap, LayerGroup } from "leaflet";
import type { Point, RoadRoute, Simulation } from "../../domain/models";

export function RouteMap({ mapKey, pickup, destination, route, simulation, onSelect }: {
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
  const fitted = useRef("");
  select.current = onSelect;
  useEffect(() => {
    if (!mapKey || !container.current) return;
    let disposed = false;
    import("leaflet").then(L => {
      if (disposed || !container.current) return;
      const instance = L.map(container.current).setView([10.7769, 106.7009], 13);
      L.tileLayer(`https://maps.geoapify.com/v1/tile/osm-carto/{z}/{x}/{y}.png?apiKey=${encodeURIComponent(mapKey)}`, {
        maxZoom: 20,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors | <a href="https://www.geoapify.com/">Geoapify</a>',
      }).on("tileerror", () => setError("Không tải được lớp bản đồ. Có thể tiếp tục bằng tìm địa chỉ.")).addTo(instance);
      map.current = instance;
      layers.current = L.layerGroup().addTo(instance);
      instance.on("click", event => select.current?.(event.latlng.lat, event.latlng.lng));
      setReady(true);
    }).catch(() => setError("Không tải được bản đồ."));
    return () => { disposed = true; map.current?.remove(); map.current = null; layers.current = null; };
  }, [mapKey]);
  useEffect(() => {
    if (!ready) return;
    let disposed = false;
    import("leaflet").then(L => {
      if (disposed || !map.current || !layers.current) return;
      const group = layers.current;
      group.clearLayers();
      for (const [point, color, label] of [[pickup, "#166534", "Điểm đón"], [destination, "#b45309", "Điểm đến"]] as const) {
        if (point) L.circleMarker([point.latitude, point.longitude], { radius: 8, color, fillOpacity: 1 }).bindTooltip(label).addTo(group);
      }
      if (route) L.polyline(route.coordinates.map(p => [p[1], p[0]] as [number, number]), { color: "#16836d", weight: 5 }).addTo(group);
      if (simulation) {
        const points = simulation.route.coordinates;
        L.polyline(points.map(p => [p[1], p[0]] as [number, number]), { color: "#b45309", weight: 4, dashArray: "6 5" }).addTo(group);
        const lengths = points.slice(1).map((p, i) => L.latLng(points[i][1], points[i][0]).distanceTo(L.latLng(p[1], p[0])));
        let remaining = lengths.reduce((a, b) => a + b, 0) * simulation.progress;
        let position = points[points.length - 1];
        for (let i = 0; i < lengths.length; i++) {
          if (lengths[i] > 0 && remaining <= lengths[i]) {
            const fraction = remaining / lengths[i];
            position = [points[i][0] + (points[i + 1][0] - points[i][0]) * fraction, points[i][1] + (points[i + 1][1] - points[i][1]) * fraction];
            break;
          }
          remaining -= lengths[i];
        }
        if (position) L.circleMarker([position[1], position[0]], { radius: 11, color: "#1e40af", fillOpacity: 1 }).bindTooltip("Xe").addTo(group);
      }
      const signature = JSON.stringify([pickup, destination, route?.coordinates.length]);
      const selected = [pickup, destination].filter((point): point is Point => point !== null);
      if (signature !== fitted.current && selected.length > 0) {
        map.current.fitBounds(L.latLngBounds(selected.map(p => [p.latitude, p.longitude])), { padding: [35, 35], maxZoom: 15 });
        fitted.current = signature;
      }
    });
    return () => { disposed = true; };
  }, [ready, pickup, destination, route, simulation]);
  return <section className="card overflow-hidden" aria-label="Bản đồ hành trình">
    <div ref={container} className="h-[380px] sm:h-[480px] relative z-0" />
    {(!mapKey || error) && <p role="status" className="p-4 text-sm">{error || "Bản đồ chưa sẵn sàng. Dùng tìm địa chỉ để đặt xe."}</p>}
    <p className="p-4 text-xs text-[#62766b]">Xanh lá: điểm đón · Cam: điểm đến · Xanh dương: xe. {onSelect && "Chạm bản đồ để chọn vị trí."}</p>
  </section>;
}
