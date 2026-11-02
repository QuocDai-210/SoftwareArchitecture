export const labels: Record<string, string> = {
  Requested: "Đang tìm tài xế",
  AwaitingRiderConfirmation: "Chờ xác nhận giá",
  ReservingDriver: "Đang giữ chỗ",
  AwaitingDriverAcceptance: "Chờ tài xế nhận",
  AcceptingDriver: "Đang xác nhận tài xế",
  DriverAccepted: "Tài xế đã nhận",
  EnRoute: "Tài xế đang đến",
  PickedUp: "Đang di chuyển",
  DroppedOff: "Đã trả khách",
  PaymentPending: "Đang thanh toán",
  PaymentReview: "Thanh toán chờ xử lý",
  Paid: "Chuyến hoàn tất",
  Cancelled: "Đã hủy",
  Failed: "Không tìm được xe",
};
export const money = (value: number) =>
  new Intl.NumberFormat("vi-VN", {
    style: "currency",
    currency: "VND",
    maximumFractionDigits: 0,
  }).format(value);
