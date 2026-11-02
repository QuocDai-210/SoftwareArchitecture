import type { Simulation } from "../../domain/models";
import type { RideService } from "../../application/RideService";

export function SimulationControls({ simulation, service, run }: {
  simulation: Simulation;
  service: RideService;
  run: (action: () => Promise<void>) => Promise<void>;
}) {
  function update(speed: number, paused: boolean) {
    void run(async () => { await service.control(simulation.driverId, speed, paused); });
  }
  return <section className="card p-5 flex flex-wrap gap-4 items-center">
    <div><h2 className="font-bold">Xe giả lập</h2><p className="text-sm" role="status">{simulation.arrived ? "Đã đến nơi" : `${Math.floor(simulation.progress * 100)}% hành trình`}</p></div>
    <label className="text-sm">Tốc độ
      <select className="field" value={simulation.speed} onChange={e => update(Number(e.target.value), simulation.paused)}>
        {[1, 5, 10, 20].map(speed => <option key={speed} value={speed}>{speed}×</option>)}
      </select>
    </label>
    <button className="btn btn-secondary" onClick={() => update(simulation.speed, !simulation.paused)}>{simulation.paused ? "Tiếp tục" : "Tạm dừng"}</button>
    <p className="text-xs">Tài xế xác nhận đón/trả khách khi xe cách điểm đến tối đa 100 m.</p>
  </section>;
}
