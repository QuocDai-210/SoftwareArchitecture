# SwiftRide

SwiftRide là đồ án mô phỏng đặt xe tại TP. Hồ Chí Minh. Dự án giữ Next.js, .NET 10, API Gateway,
Trip Service, Matching Service, Payment Service, PostgreSQL, MongoDB và RabbitMQ. Tài khoản,
Driver, chuyến đi và tiền đều là dữ liệu mô phỏng. Chỉ phần bản đồ gọi dịch vụ Geoapify bên ngoài.

## Chạy trên Windows

Cần Docker Desktop ở chế độ Linux containers và Python 3. Tại thư mục gốc repo, chạy:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 setup
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 build
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 up
```

Trong `.env`, đặt `GEOAPIFY_API_KEY` và `NEXT_PUBLIC_GEOAPIFY_MAP_KEY` nếu muốn dùng tìm địa chỉ
và bản đồ. Không đưa `.env` lên Git. Mở web tại <http://localhost:3000>.

## Tài khoản mô phỏng

Khi `FIXTURES_ENABLED=true`, Trip Service tạo tối đa 100 Rider và Matching Service tạo tối đa
1.000 Driver. Seed có ID/vị trí tái lập, không nhân đôi dữ liệu khi khởi động lại và không ghi đè
trạng thái chuyến hay sổ cái. Tài khoản và hồ sơ được lưu tại database do từng service sở hữu.

- Web: chọn `rider001` đến `rider100` trong danh sách rồi nhấn **Đăng nhập**, không nhập mật khẩu.
  Đây là chế độ chọn tài khoản mô phỏng; bất kỳ người truy cập web nào cũng có thể chọn Rider.
  Server web dùng `RIDER_PASSWORD` để xác thực với backend, không gửi mật khẩu xuống trình duyệt.
- Đăng nhập Driver để gọi API bằng Postman: `driver0001` đến `driver1000`.
- Mật khẩu chung của Rider nằm trong `RIDER_PASSWORD`; mật khẩu Driver nằm trong `DRIVER_PASSWORD`
  của file `.env`.

Đăng nhập API qua `POST http://localhost:8080/auth/login` với JSON:

```json
{ "username": "driver0001", "password": "<giá trị DRIVER_PASSWORD trong .env>" }
```

Sao chép `accessToken` từ phản hồi vào Bearer Token trong Postman. Web nhận token qua backend,
lưu phiên trong cookie HttpOnly và không đưa token vào URL. API tự lấy role từ tài khoản; client
không được chọn role trong request.

Để seed dữ liệu sau khi đã bật hệ thống cũ, đổi `FIXTURES_ENABLED=true` trong `.env`, rồi chạy:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 build
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 up
```

Không xóa volume để tạo lại dữ liệu. Khởi động lại service sẽ bổ sung các tài khoản/Driver seed
còn thiếu mà giữ nguyên bản ghi đã tồn tại.

## Quản lý service

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 status
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 logs
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 stop
```

Chỉ chạy nhóm service cần thiết. Build nặng chạy tuần tự sau khi dừng container dự án; tổng RAM
dành cho dự án không vượt 12 GB. Observability chỉ bật khi cần.

## Postman API demo

Import `postman/SwiftRide.postman_collection.json` and `postman/SwiftRide.local.postman_environment.json`.
Set the Rider and Driver password variables from local `.env` (do not commit those values), select
`SwiftRide Local`, and follow [the Postman guide](docs/postman-guide.md). The collection covers
login, maps/quotes, COD and wallet rides, Driver API actions, wallet top-ups, and automatic simulated
withdrawals. It uses `http://localhost:8080`; the Rider web is `http://localhost:3000`.

## Scope

Only Rider and Driver user roles are seeded. The web app is Rider-only; Driver workflows use API/Postman.
Payments and withdrawals are internal simulations. Geoapify is the only external integration. No
Keycloak, VNPAY, bank transfer, Ops approval, public registration, or browser geolocation is active.
See [architecture](docs/architecture.md) and the [acceptance checklist](docs/acceptance.md).
