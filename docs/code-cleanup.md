# Dọn mã theo phạm vi mô phỏng

## Công nghệ không còn sử dụng

| Công nghệ                            | Thay thế hiện tại                                 | Kết quả rà soát                                                                |
| ------------------------------------ | ------------------------------------------------- | ------------------------------------------------------------------------------ |
| Keycloak, OIDC/SSO                   | Tài khoản seed, JWT nội bộ và Auth.js Credentials | Không còn cấu hình OIDC, realm hoặc service Keycloak                           |
| SQL Server                           | PostgreSQL cho TripDB và PayDB                    | Không còn driver, kết nối hoặc service trong Compose hiện tại                  |
| VNPAY, cổng thẻ và tunnel thanh toán | Ví và COD mô phỏng nội bộ                         | Không còn provider, callback/IPN, query/reconcile, webhook hoặc công cụ tunnel |

Grafana, OpenTelemetry, Prometheus, Loki và Tempo vẫn phục vụ profile observability.
Auth.js, JWT, PostgreSQL, MongoDB, RabbitMQ/MassTransit, EF Core, Geoapify và Leaflet vẫn được dùng.
Các dependency gián tiếp trong lockfile và provider đi kèm thư viện không phải tích hợp đã bật.

## Phần đã loại bỏ

- API/UI VNPAY, callback, query/reconciliation nhà cung cấp và công cụ tunnel thanh toán cũ.
- Keycloak, đăng ký công khai, quản trị/Ops và duyệt rút thủ công.
- Web Driver, thao tác nhận chuyến, heartbeat, định vị, điều khiển xe, thu/hoàn COD trong web.
  Driver API và worker mô phỏng vẫn phục vụ đúng đặc tả/Postman.
- Các DTO không còn nơi sử dụng: `ApprovalRequest`, `RejectionRequest`, `SuspensionRequest`,
  `TopUpRequest`. Nạp hiện tại dùng hợp đồng của nghiệp vụ ví mô phỏng.
- `ITripReader.Statistics` và triển khai thống kê toàn hệ thống không còn được gọi.
- Kiểu/frontend API client, test và script cũ gắn với chức năng đã bỏ trong các lượt chuyển phạm vi.
- Cấu hình `Payments__Provider` không còn được đọc và HTTP client `payment` không có caller.
- Cờ API `RequiresCheckout` luôn `false`; metadata `CheckoutReference` chuyển khỏi Domain sang
  EF shadow property, giữ nguyên cột và index lịch sử.
- Trường `paymentToken: null` không cần thiết trong request mẫu Postman và script mô phỏng.

## Phần cần giữ có chủ đích

- `HistoricalPaymentRows.cs`: bốn hình dạng bản ghi cũ, chỉ phục vụ EF và dữ liệu lịch sử.
  Đã chuyển khỏi Domain. Giữ tên entity giúp chạy lại toàn bộ migration trên DB mới và dùng DB cũ
  mà không phát sinh đổi schema. Không có workflow checkout/provider đang hoạt động.
- Migrations, snapshot, journal, Inbox/Outbox và test hồi quy: phục vụ lưu dữ liệu, tính nhất quán,
  kiến trúc và nghiệm thu; không phải code thừa chỉ vì ít thao tác từ giao diện.
- `PaymentMethod.Card` và `PaymentToken` trong hợp đồng cũ: giữ khả năng đọc lịch sử/nhận message
  đã lưu và từ chối yêu cầu thẻ/token tại biên API; không có provider thẻ đang hoạt động.
- Container, volume và thông tin đăng nhập của triển khai cũ ngoài Git: giữ để phục hồi dữ liệu;
  không thuộc stack hoạt động hiện tại.
- Các layer/ports/pattern phục vụ yêu cầu kiến trúc. Không gộp ba service hoặc bỏ ranh giới DB.
- `node_modules` là thư viện cài cục bộ, đã nằm trong `.gitignore`; không phải mã nguồn nộp Git.

## Kết quả kiểm tra lượt dọn này

- 37 unit/model tests đạt, gồm kiểm tra EF snapshot của PayDb và TripDb khớp model hiện tại.
- Kiểm thử frontend, kiểm tra kiến trúc, Compose, giới hạn RAM và JSON request Postman đạt.
- CSharpier và Prettier kiểm tra các file đã sửa đạt; không sửa migration hoặc snapshot.
- Build Docker tuần tự đạt; tám container bắt buộc healthy, observability tắt.
- Đăng nhập web và proxy đọc lịch sử thanh toán đạt. Chuyến Wallet và COD hoàn tất `Paid`,
  số dư Rider/Driver và journal cân bằng được script kiểm tra. Card, payment token và phương thức
  lạ vẫn bị API từ chối với HTTP 400.
- Bật `noUnusedLocals` và `noUnusedParameters` để TypeScript chặn biến/import/tham số thừa.
- Rà các khai báo kiểu và hàm public bằng tham chiếu mã nguồn; xem lại từng kết quả trước khi xóa.
  Extension methods, endpoint registration và entity mapping không được xem là thừa chỉ vì tên lớp
  không có lời gọi trực tiếp.
- Cache `bin`, `obj`, `.next` được Git bỏ qua và được tạo lại khi build; không phải mã nguồn
  của công nghệ đã loại bỏ.

Không chạy toàn bộ collection Postman hoặc kiểm tra giao diện bằng trình duyệt trong lượt này.
Bằng chứng nghiệm thu nghiệp vụ được ghi riêng trong [scope-verification.md](scope-verification.md).
