export type Point = { latitude: number; longitude: number; label: string };
export type Trip = {
  id: string; riderId: string; pickup: Point; destination: Point; driverId: string | null;
  status: string; method: "Card" | "Wallet"; version: number; reason: string | null;
  createdAt: string; deadline: string | null; rejections: number;
  quote: null | { id: string; amount: number; currency: string; expiresAt: string;
    breakdown: { distanceKm: number; durationMinutes: number; perKm: number; perMinute: number; surge: number; toll: number; promotion: number; tax: number } };
  history: { status: string; at: string; reason: string | null }[];
};
export type Payment = { id: string; amount: number; status: string; reason: string | null; method: string };
export type Driver = { id: string; name: string; available: boolean; busy: boolean; latitude: number; longitude: number };
export const labels: Record<string, string> = {
  Requested: "Đang tìm tài xế", AwaitingRiderConfirmation: "Chờ xác nhận giá", ReservingDriver: "Đang giữ chỗ",
  AwaitingDriverAcceptance: "Chờ tài xế nhận", AcceptingDriver: "Đang xác nhận tài xế", DriverAccepted: "Tài xế đã nhận",
  EnRoute: "Tài xế đang đến", PickedUp: "Đang di chuyển", DroppedOff: "Đã trả khách", PaymentPending: "Đang thanh toán",
  PaymentReview: "Thanh toán chờ xử lý", Paid: "Chuyến hoàn tất", Cancelled: "Đã hủy", Failed: "Không tìm được xe",
};
export const terminal = new Set(["Paid", "Cancelled", "Failed", "PaymentReview"]);
export const money = (value: number) => new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 0 }).format(value);
export const places: Point[] = [
  { label: "Nhà hát Thành phố", latitude: 10.7769, longitude: 106.7009 },
  { label: "Chợ Bến Thành", latitude: 10.7725, longitude: 106.6980 },
  { label: "Đại học Sài Gòn", latitude: 10.7599, longitude: 106.6822 },
  { label: "Bưu điện Thành phố", latitude: 10.7798, longitude: 106.6990 },
  { label: "Thảo Cầm Viên", latitude: 10.7877, longitude: 106.7051 },
];
export async function api<T>(path: string, body?: unknown, method = "POST", key?: string): Promise<T> {
  const response = await fetch(`/api/backend/${path}`, { method: body === undefined ? "GET" : method,
    cache: "no-store", headers: { "Content-Type": "application/json", ...(key ? { "Idempotency-Key": key } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body) });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(`${problem.detail ?? (response.status === 401 ? "Phiên hết hạn. Đăng nhập lại." : `Yêu cầu lỗi (${response.status}).`)}${problem.correlationId ? ` Mã tra cứu: ${problem.correlationId}` : ""}`);
  }
  return response.status === 204 ? undefined as T : response.json();
}
