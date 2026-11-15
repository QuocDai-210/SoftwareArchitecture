import type {
  Journal,
  AssignedDriver,
  Payment,
  Point,
  Trip,
  Simulation,
  Profile,
  Page,
  PaymentStatistic,
} from "../domain/models";
import type { TripAction } from "../domain/trip-rules";
export type Booking = {
  pickup: Point;
  destination: Point;
  method: "Cod" | "Wallet";
  pickupNote?: string;
};
export interface RideGateway {
  quote(pickup: Point, destination: Point): Promise<NonNullable<Trip["quote"]>>;
  search(text: string): Promise<Point[]>;
  reverse(latitude: number, longitude: number): Promise<Point>;
  profile(): Promise<Profile>;
  saveProfile(name: string, phone: string): Promise<Profile>;
  history(page: number, status?: string): Promise<Page<Trip>>;
  ledger(id: string): Promise<Journal[]>;
  payments(page: number): Promise<Page<Payment>>;
  paymentStatistics(): Promise<PaymentStatistic[]>;
  trips(): Promise<Trip[]>;
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
}
