export function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="card px-5 py-4">
      <p className="text-xs text-[#62766b] mb-2">{label}</p>
      <p className="text-lg font-bold truncate">{value}</p>
    </div>
  );
}
