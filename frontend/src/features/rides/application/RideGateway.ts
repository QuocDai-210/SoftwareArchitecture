import type {
  Driver,
  Journal,
  AssignedDriver,
  Payment,
  Point,
  Trip,
  Simulation,
  Profile,
  DriverApplication,
  DriverRegistration,
  Page,
  PaymentStatistic,
  TripStatistic,
  RoadRoute,
} from "../domain/models";
import type { TripAction } from "../domain/trip-rules";
export type Booking = {
  pickup: Point;
  destination: Point;
  method: "Cod";
  paymentToken: string | null;
  pickupNote?: string;
};
export interface RideGateway {
  route(pickup: Point, destination: Point): Promise<RoadRoute>;
  quote(pickup: Point, destination: Point): Promise<NonNullable<Trip["quote"]>>;
  search(text: string): Promise<Point[]>;
  reverse(latitude: number, longitude: number): Promise<Point>;
  heartbeat(): Promise<void>;
  simulation(driverId: string): Promise<Simulation | null>;
  control(driverId: string, speed: number, paused: boolean): Promise<Simulation>;
  profile(): Promise<Profile>;
  saveProfile(name: string, phone: string): Promise<Profile>;
  applications(page: number): Promise<Page<DriverApplication>>;
  registerDriver(application: DriverRegistration): Promise<DriverApplication>;
  approveDriver(id: string, temporaryPassword: string): Promise<DriverApplication>;
  rejectDriver(id: string, reason: string): Promise<DriverApplication>;
  suspendDriver(subject: string, suspended: boolean): Promise<void>;
  history(page: number, status?: string): Promise<Page<Trip>>;
  ledger(id: string): Promise<Journal[]>;
  payments(page: number): Promise<Page<Payment>>;
  paymentStatistics(): Promise<PaymentStatistic[]>;
  tripStatistics(): Promise<TripStatistic[]>;
  confirmCash(id: string, reason: string | undefined, key: string): Promise<Payment>;
  refund(id: string, reason: string, key: string): Promise<Payment>;
  retryPayment(trip: Trip): Promise<void>;
  trips(): Promise<Trip[]>;
  driver(): Promise<Driver>;
  screen(
    id: string,
  ): Promise<{
    trip: Trip;
    payment: Payment | null;
    driver: AssignedDriver | null;
    paymentUnavailable: boolean;
    simulation: Simulation | null;
  }>;
  book(booking: Booking, key: string): Promise<Trip>;
  act(trip: Trip, action: TripAction, reason?: string): Promise<void>;
  review(trip: Trip, rating: number, comment: string): Promise<void>;
  location(latitude: number, longitude: number): Promise<void>;
  availability(available: boolean): Promise<void>;
}
