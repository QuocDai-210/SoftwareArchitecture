export type View = "ride" | "history" | "payments" | "operations" | "profile" | "overview";

const paths: Record<View, string> = {
  ride: "M3 11l2-6h14l2 6v8h-3v-3H6v3H3v-8zm0 0h18M7 13h.01M17 13h.01",
  history: "M3 12a9 9 0 109-9 9 9 0 00-7 3L2 9m0-6v6h6m4-2v5l3 2",
  payments: "M3 6h18v14H3V6zm0 4h18M7 15h3M5 6V3h14v3",
  operations:
    "M16 21v-2a4 4 0 00-4-4H6a4 4 0 00-4 4v2m14-13a4 4 0 010 8m6 5v-2a4 4 0 00-3-4M13 7a4 4 0 11-8 0 4 4 0 018 0z",
  profile: "M20 21v-2a7 7 0 00-14 0v2M17 7a4 4 0 11-8 0 4 4 0 018 0z",
  overview: "M3 3h7v7H3V3zm11 0h7v7h-7V3zM3 14h7v7H3v-7zm11 0h7v7h-7v-7z",
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
