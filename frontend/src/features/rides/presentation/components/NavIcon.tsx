export type View = "ride" | "history" | "payments" | "wallet" | "profile";

const paths: Record<View, string> = {
  ride: "M9 17a4 4 0 11-8 0 4 4 0 018 0zm14 0a4 4 0 11-8 0 4 4 0 018 0zM5 17l5-7 5 7H5m8-13h4l2 13M7 9h5",
  history: "M3 12a9 9 0 109-9 9 9 0 00-7 3L2 9m0-6v6h6m4-2v5l3 2",
  payments: "M3 6h18v14H3V6zm0 4h18M7 15h3M5 6V3h14v3",
  wallet: "M3 6h17v14H3V6zm0 0V4h14v2m-2 5h7v5h-7v-5m3 2v1",
  profile: "M20 21v-2a7 7 0 00-14 0v2M17 7a4 4 0 11-8 0 4 4 0 018 0z",
};
export function NavIcon({ name }: { name: View }) {
  return (
    <svg
      width="22"
      height="22"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={paths[name]} />
    </svg>
  );
}
