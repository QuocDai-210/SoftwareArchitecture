import type { Trip } from "./models";
export const terminal = new Set(["Paid", "Cancelled", "Failed"]);
export function isActiveTrip(trip: Pick<Trip, "status" | "method">): boolean {
  return (
    !terminal.has(trip.status) &&
    (trip.method === "Cod" ||
      !["DroppedOff", "PaymentPending", "PaymentReview", "Paid"].includes(trip.status))
  );
}
export type TripAction =
  | "confirm-quote"
  | "cancel"
  | "driver-accept"
  | "driver-reject"
  | "en-route"
  | "pickup"
  | "dropoff";
export function availableActions(trip: Trip, role: "rider" | "driver" | "observer"): TripAction[] {
  if (role === "rider") {
    if (trip.status === "AwaitingRiderConfirmation") return ["confirm-quote", "cancel"];
    return ["Requested", "AwaitingDriverAcceptance", "DriverAccepted", "EnRoute"].includes(
      trip.status,
    )
      ? ["cancel"]
      : [];
  }
  if (role === "driver") {
    switch (trip.status) {
      case "AwaitingDriverAcceptance":
        return ["driver-accept", "driver-reject"];
      case "DriverAccepted":
        return ["en-route", "cancel"];
      case "EnRoute":
        return ["pickup", "cancel"];
      case "PickedUp":
        return ["dropoff"];
    }
  }
  return [];
}
