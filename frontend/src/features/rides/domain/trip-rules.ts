import type { Trip } from "./models";
export const terminal = new Set(["Paid", "Cancelled", "Failed"]);
export function isActiveTrip(trip: Pick<Trip, "status" | "method">): boolean {
  return !terminal.has(trip.status);
}
export function latestTrip(...snapshots: (Trip | null | undefined)[]): Trip | undefined {
  return snapshots.reduce<Trip | undefined>((latest, trip) => {
    return trip && (!latest || trip.version > latest.version) ? trip : latest;
  }, undefined);
}
export function canReviewTrip(trip: Trip): boolean {
  return (
    ["PaymentPending", "PaymentReview", "Paid"].includes(trip.status) &&
    trip.history.some((change) => change.status === "DroppedOff")
  );
}
export type TripAction = "confirm-quote" | "cancel";
export function availableActions(trip: Trip, role: "rider" | "observer"): TripAction[] {
  if (role === "rider") {
    if (trip.status === "AwaitingRiderConfirmation") return ["confirm-quote", "cancel"];
    if (
      trip.status === "PaymentReview" &&
      trip.method === "Wallet" &&
      !trip.history.some((change) =>
        ["DriverAccepted", "EnRoute", "PickedUp", "DroppedOff", "PaymentPending", "Paid"].includes(
          change.status,
        ),
      )
    ) {
      return ["cancel"];
    }
    return ["Requested", "AwaitingDriverAcceptance", "DriverAccepted", "EnRoute"].includes(
      trip.status,
    )
      ? ["cancel"]
      : [];
  }
  return [];
}
