export type Point = { latitude: number; longitude: number; label: string };
export type Trip = {
  id: string;
  riderId: string;
  pickup: Point;
  destination: Point;
  driverId: string | null;
  status: string;
  method: "Card" | "Wallet" | "Cod";
  version: number;
  reason: string | null;
  createdAt: string;
  deadline: string | null;
  rejections: number;
  quote: null | {
    route?: RoadRoute | null;
    id: string;
    amount: number;
    currency: string;
    expiresAt: string;
    breakdown: {
      distanceKm: number;
      durationMinutes: number;
      perKm: number;
      perMinute: number;
      surge: number;
      toll: number;
      promotion: number;
      tax: number;
    };
  };
  history: { status: string; at: string; reason: string | null }[];
  riderName: string;
  riderPhone: string | null;
  pickupNote: string;
  rating: number | null;
  review: string | null;
  ratedAt: string | null;
};
export type Payment = {
  isTopUp: boolean;
  requiresCheckout: boolean;
  createdAt: string;
  id: string;
  amount: number;
  status: string;
  reason: string | null;
  method: string;
  driverId: string | null;
  collectedBy: string | null;
  collectedAt: string | null;
  collectionReason: string | null;
  refundedBy: string | null;
  refundedAt: string | null;
};
export type AssignedDriver = {
  id: string;
  name: string;
  phone: string;
  plate: string;
  vehicle: string;
};
export type Driver = {
  id: string;
  name: string;
  available: boolean;
  busy: boolean;
  latitude: number;
  longitude: number;
};
export type RoadRoute = { distanceKm: number; durationMinutes: number; coordinates: number[][] };
export type Simulation = {
  currentPosition: Point;
  driverId: string;
  tripId: string;
  stage: string;
  route: RoadRoute;
  progress: number;
  speed: number;
  paused: boolean;
  arrived: boolean;
  remainingMinutes: number;
};
export type Profile = { id: string; name: string; phone: string };
export type DriverApplication = {
  id: string;
  username: string;
  name: string;
  email: string;
  phone: string;
  plate: string;
  vehicle: string;
  status: string;
  subject: string | null;
  reason: string | null;
};
export type DriverRegistration = Pick<
  DriverApplication,
  "username" | "name" | "email" | "phone" | "plate" | "vehicle"
>;
export type Page<T> = { items: T[]; page: number; pageSize: number; total: number };
export type PaymentStatistic = {
  status: string;
  isTopUp: boolean;
  method: string;
  count: number;
  amount: number;
};
export type Journal = {
  id: string;
  kind: string;
  createdAt: string;
  lines: { account: string; side: "Debit" | "Credit"; amount: number }[];
};
export type TripStatistic = { status: string; count: number };
