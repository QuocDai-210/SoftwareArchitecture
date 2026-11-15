# Dọn mã theo phạm vi mô phỏng

## Phần đã loại bỏ

- API/UI VNPAY, callback, query/reconciliation nhà cung cấp và công cụ tunnel thanh toán cũ.
- Keycloak, đăng ký công khai, quản trị/Ops và duyệt rút thủ công.
- Web Driver, thao tác nhận chuyến, heartbeat, định vị, điều khiển xe, thu/hoàn COD trong web.
  Driver API và worker mô phỏng vẫn phục vụ đúng đặc tả/Postman.
- Các DTO không còn nơi sử dụng: `ApprovalRequest`, `RejectionRequest`, `SuspensionRequest`,
  `TopUpRequest`. Nạp hiện tại dùng hợp đồng của nghiệp vụ ví mô phỏng.
- `ITripReader.Statistics` và triển khai thống kê toàn hệ thống không còn được gọi.
- Kiểu/frontend API client, test và script cũ gắn với chức năng đã bỏ trong các lượt chuyển phạm vi.

## Phần cần giữ có chủ đích

- `HistoricalPaymentRows.cs`: bốn hình dạng bản ghi cũ, chỉ phục vụ EF và dữ liệu lịch sử.
  Đã chuyển khỏi Domain. Giữ tên entity giúp chạy lại toàn bộ migration trên DB mới và dùng DB cũ
  mà không phát sinh đổi schema. Không có workflow checkout/provider đang hoạt động.
- Migrations, snapshot, journal, Inbox/Outbox và test hồi quy: phục vụ lưu dữ liệu, tính nhất quán,
  kiến trúc và nghiệm thu; không phải code thừa chỉ vì ít thao tác từ giao diện.
- Các layer/ports/pattern phục vụ yêu cầu kiến trúc. Không gộp ba service hoặc bỏ ranh giới DB.
- `node_modules` là thư viện cài cục bộ, đã nằm trong `.gitignore`; không phải mã nguồn nộp Git.

## Kết quả kiểm tra lượt dọn này

- 37 unit/model tests đạt, gồm kiểm tra EF snapshot khớp model sau di chuyển entity lịch sử.
- Build frontend, kiểm thử frontend, kiểm tra kiến trúc và cấu hình RAM đạt.
- Bật `noUnusedLocals` và `noUnusedParameters` để TypeScript chặn biến/import/tham số thừa.
- Rà các khai báo kiểu và hàm public bằng tham chiếu mã nguồn; xem lại từng kết quả trước khi xóa.
  Extension methods, endpoint registration và entity mapping không được xem là thừa chỉ vì tên lớp
  không có lời gọi trực tiếp.
- Đã đo dung lượng thư mục build `bin`, `obj`, `.next` và xác nhận chúng được Git bỏ qua.
  Bước xóa hàng loạt cache bị bộ kiểm tra tự động chặn (`blocked by policy`), nên chưa thực hiện.
  Các thư mục này được tạo lại khi build; không phải mã nguồn của chức năng.

Lượt này không chạy lại Postman hoặc integration DB vì không sửa nghiệp vụ/schema.
Bằng chứng nghiệm thu nghiệp vụ được ghi riêng trong [scope-verification.md](scope-verification.md).
