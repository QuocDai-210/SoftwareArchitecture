import type { Trip, Point } from "../domain/models";
import { availableActions, type TripAction } from "../domain/trip-rules";
import type { Booking, RideGateway } from "./RideGateway";
export class RideService {
  private pending: { payload: string; key: string } | null = null;
  constructor(private readonly gateway: RideGateway) {}
  quote(pickup: Point, destination: Point) {
    return this.gateway.quote(pickup, destination);
  }
  search(text: string) {
    return this.gateway.search(text);
  }
  reverse(latitude: number, longitude: number) {
    return this.gateway.reverse(latitude, longitude);
  }
  profile() {
    return this.gateway.profile();
  }
  saveProfile(name: string, phone: string) {
    return this.gateway.saveProfile(name, phone);
  }
  history(page: number, status?: string) {
    return this.gateway.history(page, status);
  }
  ledger(id: string) {
    return this.gateway.ledger(id);
  }
  payments(page: number) {
    return this.gateway.payments(page);
  }
  paymentStatistics() {
    return this.gateway.paymentStatistics();
  }
  trips() {
    return this.gateway.trips();
  }
  screen(id: string) {
    return this.gateway.screen(id);
  }
  act(trip: Trip, action: TripAction, role: "rider" | "observer", reason?: string) {
    if (!availableActions(trip, role).includes(action))
      throw new Error("Thao tác không hợp lệ với trạng thái chuyến.");
    return this.gateway.act(trip, action, reason);
  }
  review(trip: Trip, rating: number, comment: string) {
    return this.gateway.review(trip, rating, comment);
  }
  async book(booking: Booking) {
    if (
      booking.pickup.latitude === booking.destination.latitude &&
      booking.pickup.longitude === booking.destination.longitude
    )
      throw new Error("Điểm đón và điểm đến phải khác nhau.");
    const payload = JSON.stringify(booking);
    if (this.pending?.payload !== payload) this.pending = { payload, key: crypto.randomUUID() };
    const trip = await this.gateway.book(booking, this.pending.key);
    this.pending = null;
    return trip;
  }
}
