import type { Driver, Trip, DriverRegistration } from "../domain/models";
import { availableActions, type TripAction } from "../domain/trip-rules";
import type { Booking, RideGateway } from "./RideGateway";
export class RideService {
  private pending: { payload: string; key: string } | null = null;
  constructor(private readonly gateway: RideGateway) {}
  search(text: string) { return this.gateway.search(text); }
  reverse(latitude: number, longitude: number) { return this.gateway.reverse(latitude, longitude); }
  heartbeat() { return this.gateway.heartbeat(); }
  simulation(driverId: string) { return this.gateway.simulation(driverId); }
  control(driverId: string, speed: number, paused: boolean) { return this.gateway.control(driverId, speed, paused); }
  profile() { return this.gateway.profile(); }
  saveProfile(name: string, phone: string) { return this.gateway.saveProfile(name, phone); }
  applications() { return this.gateway.applications(); }
  registerDriver(application: DriverRegistration) { return this.gateway.registerDriver(application); }
  approveDriver(id: string, temporaryPassword: string) { return this.gateway.approveDriver(id, temporaryPassword); }
  rejectDriver(id: string, reason: string) { return this.gateway.rejectDriver(id, reason); }
  suspendDriver(subject: string, suspended: boolean) { return this.gateway.suspendDriver(subject, suspended); }
  history(page: number) { return this.gateway.history(page); }
  payments(page: number) { return this.gateway.payments(page); }
  paymentStatistics() { return this.gateway.paymentStatistics(); }
  tripStatistics() { return this.gateway.tripStatistics(); }
  checkout(id: string) { return this.gateway.checkout(id); }
  topUp(amount: number, key: string) { return this.gateway.topUp(amount, key); }
  reconcile(id: string) { return this.gateway.reconcile(id); }
  paymentByReference(reference: string) { return this.gateway.paymentByReference(reference); }
  refund(id: string, reason: string, key: string) { return this.gateway.refund(id, reason, key); }
  retryPayment(trip: Trip) { return this.gateway.retryPayment(trip); }
  trips() {
    return this.gateway.trips();
  }
  driver() {
    return this.gateway.driver();
  }
  balance() {
    return this.gateway.balance();
  }
  screen(id: string) {
    return this.gateway.screen(id);
  }
  location(latitude: number, longitude: number) {
    return this.gateway.location(latitude, longitude);
  }
  async toggleAvailability(driver: Driver, latitude: number, longitude: number) {
    if (!driver.available) await this.gateway.location(latitude, longitude);
    await this.gateway.availability(!driver.available);
  }
  act(trip: Trip, action: TripAction, role: "rider" | "driver" | "observer") {
    if (!availableActions(trip, role).includes(action))
      throw new Error("Thao tác không hợp lệ với trạng thái chuyến.");
    return this.gateway.act(trip, action);
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
