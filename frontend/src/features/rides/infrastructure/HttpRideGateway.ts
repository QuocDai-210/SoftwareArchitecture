import type { RideGateway, Booking } from "../application/RideGateway";
import type {
  Journal,
  AssignedDriver,
  Payment,
  Trip,
  Point,
  Simulation,
  Profile,
  Page,
  PaymentStatistic,
} from "../domain/models";
import type { TripAction } from "../domain/trip-rules";
async function api<T>(path: string, body?: unknown, method = "POST", key?: string): Promise<T> {
  const response = await fetch(`/api/backend/${path}`, {
    method: body === undefined ? "GET" : method,
    cache: "no-store",
    signal: AbortSignal.timeout(30000),
    headers: { "Content-Type": "application/json", ...(key ? { "Idempotency-Key": key } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(
      `${problem.detail ?? (response.status === 401 ? "Phiên hết hạn. Đăng nhập lại." : `Yêu cầu lỗi (${response.status}).`)}${problem.correlationId ? ` Mã tra cứu: ${problem.correlationId}` : ""}`,
    );
  }
  return response.status === 204 ? (undefined as T) : response.json();
}

export class HttpRideGateway implements RideGateway {
  quote(pickup: Point, destination: Point) {
    return api<NonNullable<Trip["quote"]>>("quote", { pickup, destination });
  }
  search(text: string) {
    return api<Point[]>(`maps/search?text=${encodeURIComponent(text)}`);
  }
  reverse(latitude: number, longitude: number) {
    return api<Point>(`maps/reverse?latitude=${latitude}&longitude=${longitude}`);
  }
  profile() {
    return api<Profile>("profiles/me");
  }
  saveProfile(name: string, phone: string) {
    return api<Profile>("profiles/me", { name, phone }, "PUT");
  }
  history(page: number, status?: string) {
    return api<Page<Trip>>(
      `trips/history?page=${page}${status ? `&status=${encodeURIComponent(status)}` : ""}`,
    );
  }
  ledger(id: string) {
    return api<Journal[]>(`payments/${id}/ledger`);
  }
  payments(page: number) {
    return api<Page<Payment>>(`payments/history?page=${page}`);
  }
  paymentStatistics() {
    return api<PaymentStatistic[]>("payments/statistics");
  }
  trips() {
    return api<Trip[]>("trips");
  }
  screen(id: string) {
    return api<{
      trip: Trip;
      payment: Payment | null;
      driver: AssignedDriver | null;
      paymentUnavailable: boolean;
      simulation: Simulation | null;
    }>(`screens/trips/${id}`);
  }
  book(booking: Booking, key: string) {
    return api<Trip>("trips", booking, "POST", key);
  }
  async act(trip: Trip, action: TripAction, reason?: string) {
    await api(`trips/${trip.id}/${action}`, {
      expectedVersion: trip.version,
      ...(action === "confirm-quote" ? { quoteId: trip.quote?.id } : {}),
      ...(action === "cancel" ? { reason } : {}),
    });
  }
  async review(trip: Trip, rating: number, comment: string) {
    await api(`trips/${trip.id}/review`, { expectedVersion: trip.version, rating, comment });
  }
}
