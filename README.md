# SwiftRide

SwiftRide triển khai luồng đặt xe trong [đề bài](RideLite-KienTrucPhanMem.pdf). Postman gọi
Gateway YARP ở `http://localhost:8080`. Trip quản lý trạng thái trong TripDB, Matching tìm tài xế
và tính giá trên dữ liệu MatchDB (MongoDB), Payment ghi sổ kép trong PayDB. TripDB và PayDB là
hai database PostgreSQL riêng, dùng tài khoản riêng theo phương án được phép ở mục 3.3.
RabbitMQ chuyển sự kiện giữa các service. Mã nguồn nằm trong `backend/src`, chia theo service
và các tầng Domain, Application, Infrastructure, Api.

## Chạy và kiểm thử bằng Postman

Cần Docker Compose và Python 3.

Để dùng bộ dữ liệu kiểm thử riêng trên máy đã có volume SwiftRide, đặt
`export COMPOSE_PROJECT_NAME=swiftride-postman` trước khi build và giữ biến này trong các
terminal chạy Compose. Trên PowerShell, dùng `$env:COMPOSE_PROJECT_NAME = "swiftride-postman"`.
Tên project quyết định bộ volume sử dụng. Nếu cần mở lại dữ liệu cũ, dùng tên project và file
`.env` có mật khẩu tương ứng với các volume đó.

Trên Linux, chạy tại thư mục gốc:

```sh
python3 scripts/setup.py
make build
make up
```

Trên Windows, chạy các lệnh tương ứng:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 setup
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 build
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 up
```

1. Import [collection](postman/SwiftRide.postman_collection.json) và
   [environment](postman/SwiftRide.local.postman_environment.json). Chọn **SwiftRide Local**,
   điền `riderPassword` và `driverPassword` từ file `.env` cục bộ.
2. Chạy riêng folder **7 - Sequential ride runner** trong Collection Runner. Chọn
   `paymentMethod=Wallet` để kiểm tra từ đặt xe, báo giá, nhận chuyến, đón/trả đến khi chuyến
   chuyển sang `Paid` và đọc sổ cái. Chế độ API dùng tuyến đường ước lượng cục bộ.
3. Theo dõi từng request ở terminal thứ hai. Đối chiếu `CorrelationId` trong Postman Console
   với log để xem service, route, mã HTTP và thời gian xử lý.

```sh
docker compose ps
docker compose logs --tail=30 -f gateway trip matching payment
```

Dừng sau khi thử bằng `docker compose --profile "*" stop`. Dữ liệu chuyến và sổ cái được giữ
trong volume. Xem các nhánh từ chối, hết lượt tìm tài xế và lỗi thanh toán trong
[hướng dẫn Postman](docs/postman-guide.md).

## Tài khoản mô phỏng

Khi `FIXTURES_ENABLED=true`, Trip Service tạo tối đa 100 Rider và Matching Service tạo tối đa
1.000 Driver. Seed có ID/vị trí tái lập, không nhân đôi dữ liệu khi khởi động lại và không ghi đè
trạng thái chuyến hay sổ cái. Tài khoản và hồ sơ được lưu tại database do từng service sở hữu.

- Web chỉ đăng nhập Rider: `rider001` đến `rider100`.
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

## OAuth 2.0 / OpenID Connect

Bộ Postman mặc định dùng tài khoản seed. Khi đã có nhà cung cấp OIDC, đặt `AUTH_AUTHORITY`
bằng URL issuer HTTPS mà các container truy cập được, rồi chạy lại `make up`. Ở chế độ này,
Gateway và từng service kiểm tra chữ ký qua discovery/JWKS, issuer, thời hạn và audience
`swiftride-api`. JWT nội bộ chỉ dành cho giao tiếp giữa các service.

Trong Postman, lấy **access token** bằng Authorization Code với PKCE của nhà cung cấp. Cấu hình
nhà cung cấp cấp claim `role` là `rider` hoặc `driver` và `sub` trùng ID của tài khoản tương
ứng trong dữ liệu thử. Dùng token ở các request thủ công. Folder 7 và web demo dùng luồng
đăng nhập seed khi `AUTH_AUTHORITY` để trống.

## Quản lý service

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 status
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 logs
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 stop
```

Chỉ chạy nhóm service cần thiết. Build nặng chạy tuần tự sau khi dừng container dự án; tổng RAM
dành cho dự án không vượt 12 GB.

Nếu cần chạy cả web, dùng `make build-demo` rồi `make demo` trên Linux (gồm Grafana), hoặc
`manage.ps1 build-demo` rồi `manage.ps1 demo` trên Windows. Cấu hình Geoapify trong `.env` cho
bản đồ web. Xem [kiến trúc](docs/architecture.md) và [kiểm thử CI](docs/ci.md).
