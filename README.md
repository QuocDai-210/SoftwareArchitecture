# SwiftRide

Đồ án theo `RideLite-KienTrucPhanMem.pdf`: Next.js/Tailwind, .NET 10, YARP,
Trip Service/SQL Server, Matching & Pricing/MongoDB, Payment Service/PostgreSQL,
RabbitMQ, Saga và Outbox. Giữ riêng container, mã nguồn và database từng service.

Ưu tiên hiện tại: bản đồ thật Geoapify, đặt xe, hành trình và ba vai trò User/Driver/Ops.
Thanh toán giữ lõi cũ để bám PDF; VNPAY và giao diện thanh toán tạm hoãn.
[Đề xuất gom chức năng](docs/service-consolidation-proposal.md) đang chờ review;
chưa gộp microservice hoặc container.

## Chạy hệ thống

Cần Docker Engine/Compose có quyền truy cập, mạng tải package/image và Geoapify key.
Build/check trên host cần .NET SDK 10 và Node.js 24 trở lên. SQL Server cần x86_64.

1. Chạy `python3 scripts/setup.py`. Script tạo secret nội bộ, bổ sung biến thiếu,
   giữ nguyên giá trị đã có và đặt `.env` quyền 0600.
2. Tạo project tại [Geoapify My Projects](https://myprojects.geoapify.com/).
   Điền key phía server vào `GEOAPIFY_API_KEY`; key bản đồ trình duyệt vào
   `NEXT_PUBLIC_GEOAPIFY_MAP_KEY` trong `.env`. Key trình duyệt cần cho phép origin
   `http://localhost:3000`; key server cần dùng được cho geocoding/routing.
   `setup.py` không sinh API key bên ngoài. Không đưa `.env` vào Git.
3. Build lần lượt, sau đó khởi động và đồng bộ danh tính:

```bash
docker compose --profile observability stop
for service in rabbitmq trip matching payment gateway web; do
  docker compose build "$service" || exit 1
done
docker compose up --no-build -d --wait --wait-timeout 300
python3 -B scripts/sync-identity.py
```

Mở http://localhost:3000. Dùng các hồ sơ/cửa sổ trình duyệt riêng cho ba vai trò:

- **User:** bấm “Đăng ký tài khoản User”. Chỉ được cấp quyền đặt xe.
- **Ops:** đăng nhập `ops1`, mật khẩu `OPS_PASSWORD` trong `.env`. Nhập hồ sơ Driver,
  xét duyệt và cấp mật khẩu tạm. Có thể từ chối, khóa/mở khóa Driver và xem chuyến.
- **Driver:** đăng nhập tài khoản Ops đã duyệt; đổi mật khẩu tạm lần đầu. Chọn vị trí
  xe, lưu vị trí rồi bật nhận chuyến. Không có đăng ký Driver công khai.

User tìm địa chỉ, dùng định vị hoặc chạm bản đồ để chọn điểm đón/đến. Tuyến đi,
khoảng cách và thời gian lấy từ Geoapify; xác nhận giá rồi Driver nhận chuyến.
Driver bấm “Đang đến đón”, chờ xe đến nơi, xác nhận đón và trả khách.
Xe di chuyển giả lập trên đường thật; Driver/Ops chỉnh tốc độ 1×/5×/10×/20×,
tạm dừng và tiếp tục. User chỉ theo dõi xe của chuyến mình.

Hồ sơ cá nhân; lịch sử và danh sách Driver có phân trang. Driver gửi heartbeat mỗi 10 giây;
vị trí quá 60 giây không được ghép xe. Không thể tự đổi vị trí khi đang phục vụ chuyến.
Keycloak dùng volume `identity-data`, giữ tài khoản qua recreate container.
`sync-identity.py` giữ subject/mật khẩu cũ, chuyển quyền Rider cũ sang User và tắt
client kiểm thử password grant cũ. Đăng xuất web xóa phiên web; Keycloak có thể còn SSO.

| Địa chỉ                            | Nội dung                                      |
| ---------------------------------- | --------------------------------------------- |
| http://localhost:3000              | Web User/Driver/Ops                           |
| http://localhost:8080/health/ready | Readiness Gateway và ba service               |
| http://localhost:8081              | Keycloak; `admin` / `KEYCLOAK_ADMIN_PASSWORD` |
| http://localhost:15672             | RabbitMQ; `RABBIT_USER` / `RABBIT_PASSWORD`   |
| http://localhost:3001/d/swiftride  | Grafana khi bật observability                 |

## RAM và vòng đời service

Tổng RAM dự án, gồm build và kiểm thử, tối đa **12 GB**. Build nặng chạy tuần tự,
dừng container dự án trước build. Hạn mức container mặc định **7,38 GB**;
bật thêm observability là **9,53 GB**, không dùng swap. Hạn mức không gồm OS/IDE.
`python3 scripts/check-resources.py` kiểm tra cấu hình này.

Observability mặc định tắt. Chỉ bật khi cần:

```bash
OTEL_SDK_DISABLED=false docker compose --profile observability up --no-build -d --wait
docker compose stop observability
OTEL_SDK_DISABLED=true docker compose up --no-build -d --wait
```

Chỉ bật service cần dùng và dependency bắt buộc, ví dụ `docker compose up -d mongo`.
Kết thúc công việc chạy `docker compose --profile observability stop` để giải phóng RAM;
giữ nguyên volume. Không dùng `down -v` khi cần giữ tài khoản, chuyến và sổ cái.

## Mã nguồn và kiểm tra

Mỗi microservice có `Domain/`, `Application/`, `Infrastructure/`, `Api/` thành project
riêng. Tầng trong không tham chiếu framework. Frontend có
`features/rides/{domain,application,infrastructure,presentation}`; `app/` giữ route,
`composition/` ghép service/adapter. Xem [bản đồ mã nguồn](docs/source-guide.md),
[kiến trúc và SOLID](docs/architecture.md), [đối chiếu PDF](docs/acceptance.md).
Format mục tiêu 100 ký tự, mỗi câu lệnh một dòng; nhóm ngoặc ngắn gom cùng dòng.

```bash
# Kiến trúc và assert nghiệp vụ; không cần Docker
./scripts/verify.sh --core

# Build, core/frontend checks, Docker và Chrome kiểm tra User/Driver/Ops + bản đồ
./scripts/verify.sh

# Khi hệ thống đã chạy và đã sync identity; cần Chrome/Chromium, có thể đặt CHROME_BIN
node scripts/browser-check.mjs
# Thêm kiểm tra refresh sau 300 giây
node scripts/browser-check.mjs --refresh
```

`verify.sh` dừng service trước build và sau kiểm thử, kể cả khi lỗi. Muốn xem web sau
kiểm thử: `docker compose up --no-build -d --wait`. Runner trình duyệt tạo tài khoản,
hồ sơ và chuyến kiểm tra có tên `check-*`; ảnh/log nằm ngoài repo trong thư mục tạm.
Bộ kiểm thử payment/fault cũ vẫn giữ trong source, cần fixture và cấu hình cũ;
không nằm trong nghiệm thu bản đồ/User/Driver/Ops hiện tại.

Giới hạn: xe giả lập, chạy local một instance, chưa HA/load test, chưa nghiệm thu
VNPAY. Ba database nghiệp vụ và Payment Service vẫn giữ theo PDF. Backend/frontend
có lockfile; dùng `dotnet restore --locked-mode`, `npm ci` khi cài dependency.
