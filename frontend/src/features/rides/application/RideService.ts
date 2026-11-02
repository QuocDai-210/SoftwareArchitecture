import type { Driver, Trip, DriverRegistration, Point } from "../domain/models";
import { availableActions, type TripAction } from "../domain/trip-rules";
import type { Booking, RideGateway } from "./RideGateway";
export class RideService {
  private readonly cashKeys = new Map<string, string>();
  private pending: { payload: string; key: string } | null = null;
  constructor(private readonly gateway: RideGateway) {}
  route(pickup: Point, destination: Point) {
    return this.gateway.route(pickup, destination);
  }
  search(text: string) {
    return this.gateway.search(text);
  }
  reverse(latitude: number, longitude: number) {
    return this.gateway.reverse(latitude, longitude);
  }
  heartbeat() {
    return this.gateway.heartbeat();
  }
  simulation(driverId: string) {
    return this.gateway.simulation(driverId);
  }
  control(driverId: string, speed: number, paused: boolean) {
    return this.gateway.control(driverId, speed, paused);
  }
  profile() {
    return this.gateway.profile();
  }
  saveProfile(name: string, phone: string) {
    return this.gateway.saveProfile(name, phone);
  }
  applications(page: number) {
    return this.gateway.applications(page);
  }
  registerDriver(application: DriverRegistration) {
    return this.gateway.registerDriver(application);
  }
  approveDriver(id: string, temporaryPassword: string) {
    return this.gateway.approveDriver(id, temporaryPassword);
  }
  rejectDriver(id: string, reason: string) {
    return this.gateway.rejectDriver(id, reason);
  }
  suspendDriver(subject: string, suspended: boolean) {
    return this.gateway.suspendDriver(subject, suspended);
  }
  history(page: number) {
    return this.gateway.history(page);
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
  tripStatistics() {
    return this.gateway.tripStatistics();
  }
  confirmCash(id: string, reason?: string) {
    const payload = `${id}|${reason ?? ""}`;
    let key = this.cashKeys.get(payload);
    if (!key) {
      key = crypto.randomUUID();
      this.cashKeys.set(payload, key);
    }
    return this.gateway.confirmCash(id, reason, key);
  }
  refund(id: string, reason: string, key: string) {
    return this.gateway.refund(id, reason, key);
  }
  retryPayment(trip: Trip) {
    return this.gateway.retryPayment(trip);
  }
  trips() {
    return this.gateway.trips();
  }
  driver() {
    return this.gateway.driver();
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
