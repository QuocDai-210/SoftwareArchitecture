# Kiểm tra tự động bằng GitHub Actions

Workflow nằm tại [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml).
Chạy mỗi lần push lên một nhánh, khi mở/cập nhật PR vào `main`, hoặc chạy thủ công qua
**Actions → SwiftRide CI → Run workflow** sau khi workflow đã có trên nhánh mặc định.
Push vào nhánh đang có PR có thể tạo hai lượt: một lượt kiểm tra nhánh và một lượt kiểm tra PR.

## Nội dung kiểm tra

Một job `Architecture, backend and frontend` chạy tuần tự:

1. Kiểm tra công cụ setup và tự tạo `.env` riêng trên runner với mật khẩu ngẫu nhiên.
2. Kiểm tra ranh giới kiến trúc, cấu hình Compose và giới hạn RAM khai báo tối đa 12 GB.
3. Build toàn bộ solution .NET 10; chạy unit/model tests, xuất báo cáo TRX.
4. Cài frontend từ lockfile, kiểm tra TypeScript và chạy kiểm tra logic frontend.
5. Build production Next.js.

Runner dùng Ubuntu, Python 3.12, Node.js 24.11.1 và .NET SDK 10.0.x.
Build backend dùng một MSBuild worker; Node giới hạn heap 1.536 MB. Không khởi động
container ứng dụng hoặc observability trong job này. Kiểm tra giới hạn Compose là kiểm tra
cấu hình, không phải phép đo RAM thực tế của ứng dụng khi chạy.

Không cần khai báo repository secrets hoặc upload `.env` của thành viên.
Không xuất `.env`, token hoặc khóa bản đồ ra log/artifact; chỉ lưu báo cáo test TRX trong 7 ngày.

## Xem kết quả

- Trong PR, mở **Checks → Architecture, backend and frontend** để xem bước nào đạt/thất bại.
- Trong **Actions → SwiftRide CI**, chọn lượt chạy đúng commit để đọc log từng bước.
- Báo cáo backend nằm ở mục **Artifacts** của lượt chạy nếu bước test đã tạo báo cáo.
- Khi có lỗi, sửa trên cùng nhánh và push lại; PR sẽ tự cập nhật kết quả.

Chủ repository có thể đặt check `Architecture, backend and frontend` làm điều kiện bắt buộc
trước khi merge vào `main` trong quy tắc bảo vệ nhánh/ruleset. Workflow không tự bật quy tắc này.

## Giới hạn

CI này không chạy integration tests có nhãn `Category=Integration`, Postman end-to-end,
trình duyệt, Geoapify, thử lỗi RabbitMQ hoặc build Docker image. Những ca đó cần hệ thống
và dữ liệu kiểm thử riêng, theo [hướng dẫn Postman](postman-guide.md) và
[hồ sơ nghiệm thu](scope-verification.md). CI xanh không thay thế nghiệm thu nghiệp vụ.
