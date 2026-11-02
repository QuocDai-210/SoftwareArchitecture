import type { Simulation } from "../../domain/models";

export function SimulationControls({
  simulation,
  onChange,
  busy,
}: {
  simulation: Simulation;
  busy: boolean;
  onChange: (speed: number, paused: boolean) => void;
}) {
  return (
    <section className="card p-5 flex flex-wrap gap-4 items-center">
      <div>
        <h2 className="font-bold">Xe giả lập</h2>
        <p className="text-sm" role="status">
          {simulation.arrived
            ? "Đã đến nơi"
            : `${Math.floor(simulation.progress * 100)}% hành trình`}
        </p>
      </div>
      <label className="text-sm">
        Tốc độ
        <select
          disabled={busy}
          className="field"
          value={simulation.speed}
          onChange={(e) => onChange(Number(e.target.value), simulation.paused)}
        >
          {[1, 5, 10, 20].map((speed) => (
            <option key={speed} value={speed}>
              {speed}×
            </option>
          ))}
        </select>
      </label>
      <button
        disabled={busy}
        className="btn btn-secondary"
        onClick={() => onChange(simulation.speed, !simulation.paused)}
      >
        {simulation.paused ? "Tiếp tục" : "Tạm dừng"}
      </button>
      <p className="text-xs">Tài xế xác nhận đón/trả khách khi xe cách điểm đến tối đa 100 m.</p>
    </section>
  );
}
