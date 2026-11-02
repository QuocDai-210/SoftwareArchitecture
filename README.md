# SwiftRide

Đồ án theo `RideLite-KienTrucPhanMem.pdf`: Next.js + Tailwind, .NET 10, YARP, 3 microservice, SQL Server / MongoDB / PostgreSQL, RabbitMQ, Saga, Outbox và OpenTelemetry.

**Trạng thái kiểm chứng:** 127 assert lõi nghiệp vụ và hợp đồng adapter; backend/frontend build thành công; npm audit 0 lỗ hổng; kiểm thử API và năm kịch bản lỗi container đều PASS. Outbox qua restart khi broker tắt và crash sau provider capture đã kiểm chứng. Chrome đã chạy trọn luồng Rider/Driver, 360 px, refresh token sau 5 phút và đăng xuất. Xem [bảng đối chiếu PDF](docs/acceptance.md).

## Mã nguồn

```text
backend/src/
  Shared/
    SwiftRide.SharedKernel/       Giá trị chung, hình học, lỗi nghiệp vụ
    SwiftRide.Contracts/          DTO HTTP và sự kiện liên service
    SwiftRide.Application/        Actor và port dùng chung
    SwiftRide.Infrastructure/     Auth, HTTP, Outbox, telemetry
  TripService/                   Chuyến đi, trạng thái, Saga
    Domain/                      Luật nghiệp vụ thuần C#
    Application/                 Commands, Queries, Workflows, Ports
    Infrastructure/              Persistence, Clients, Messaging, Workers
    Api/                         Endpoints, Program.cs
  MatchingService/               Domain/Application/Infrastructure/Api
  PaymentService/                Domain/Application/Infrastructure/Api
  ApiGateway/                    Endpoints, Composition, Infrastructure
backend/tests/SwiftRide.Checks/   Console/assert
frontend/src/
  app/                           Next.js routes và server actions
  composition/                   Ghép service với HTTP adapter phía client
  features/rides/
    domain/                      Mô hình, thao tác hợp lệ theo trạng thái
    application/                 RideService, RideGateway
    infrastructure/              HTTP adapter
    presentation/                Hook, layout, từng component giao diện
  infrastructure/                Auth/session và server proxy
scripts/                         Setup, kiểm tra kiến trúc, kiểm thử, replay
deploy/                          Database, Keycloak, RabbitMQ, Grafana
docs/                            Kiến trúc, bản đồ nguồn, nghiệm thu PDF
```

[Bản đồ mã nguồn theo chức năng](docs/source-guide.md) chỉ file cần sửa cho từng yêu cầu. Mỗi tầng backend là project riêng; Domain/Application không tham chiếu framework. `scripts/check-architecture.py` kiểm tra dependency graph và import frontend, được chạy trong `verify.sh`.

## Chạy trên máy

Chạy demo cần Docker Engine/Compose có quyền truy cập và mạng tải image/package. Kiểm thử trên host cần .NET SDK 10.0.100 trở lên trong nhánh 10.0 và Node.js 24 trở lên. Ngân sách RAM dành cho dự án tối đa 12 GB, bao gồm build và kiểm thử; SQL Server cần x86_64.

```bash
python3 scripts/setup.py
docker compose --profile observability stop
COMPOSE_PARALLEL_LIMIT=1 docker compose build
docker compose up --no-build -d --wait --wait-timeout 300
```

Tổng hạn mức RAM của nhóm mặc định là **7,38 GB**, kể cả container `sql-init` chỉ chạy
một lần. Bật thêm observability nâng tổng hạn mức lên **9,53 GB**; từng container có
`mem_limit` và không dùng swap. Đây là hạn mức container, không bao gồm RAM của hệ điều
hành, IDE hoặc Docker daemon. Khi build, dừng container dự án và chạy tuần tự để giữ
ngân sách tổng 12 GB. Kiểm tra cấu hình bằng `python3 scripts/check-resources.py`.

Observability mặc định tắt. Khi cần kiểm tra metrics, logs và traces:

```bash
OTEL_SDK_DISABLED=false docker compose --profile observability up --no-build -d --wait
# Kết thúc kiểm tra; tắt collector và đưa API về chế độ không xuất telemetry.
docker compose stop observability
OTEL_SDK_DISABLED=true docker compose up --no-build -d --wait
```

Chỉ bật riêng service và dependency cần dùng, ví dụ `docker compose up -d mongo`.
Dùng `docker compose stop <service>` khi không cần service đó và không còn service
đang chạy phụ thuộc vào nó. Kết thúc phiên làm việc: `docker compose --profile
observability stop`. Lệnh này giữ container và volume. `scripts/verify.sh` tự dừng
container trước build và sau kiểm thử, kể cả khi có lỗi; muốn xem web sau kiểm thử,
chạy lại `docker compose up --no-build -d --wait`.

`setup.py` sinh `.env` quyền 0600 và không ghi đè file đã có. Đọc mật khẩu trực tiếp trong file này; không đưa vào Git. Backend/frontend có đầy đủ lockfile; Docker restore bằng `--locked-mode`. Frontend dùng `npm ci`. Chỉ chạy `scripts/lock.sh` khi chủ động cập nhật dependency.

| Địa chỉ | Nội dung |
|---|---|
| http://localhost:3000 | Rider / Driver web |
| http://localhost:8080/health/ready | Gateway và readiness ba service |
| http://localhost:8081 | Keycloak; quản trị `admin`, `KEYCLOAK_ADMIN_PASSWORD` |
| http://localhost:15672 | RabbitMQ; `RABBIT_USER` / `RABBIT_PASSWORD` |
| http://localhost:3001/d/swiftride | Grafana khi bật profile `observability`; `admin` / `GRAFANA_PASSWORD` |

Chỉ các cổng trên được bind vào loopback. Microservice và database nằm trong mạng Compose.

| Tài khoản | Vai trò | Biến mật khẩu |
|---|---|---|
| `rider1`, `rider2` | Đặt xe; ví khởi tạo 2.000.000 VND mẫu | `RIDER_PASSWORD` |
| `driver1`–`driver4` | Nhận chuyến, cập nhật hành trình | `DRIVER_PASSWORD` |
| `ops1` | Đọc OpenAPI, hoàn tiền, phục hồi thanh toán | `OPS_PASSWORD` |

Mở hai hồ sơ trình duyệt hoặc cửa sổ riêng: một Rider, một Driver. Driver bật nhận chuyến trước, Rider chọn tuyến, xác nhận giá; Driver nhận, đang đến, đón khách, trả khách. Chờ thanh toán rồi xem lịch sử. Vị trí tài xế phải được cập nhật trong 60 giây gần nhất; trang Driver tự gửi heartbeat mỗi 10 giây.

Đăng xuất web xóa phiên web. Keycloak có thể giữ phiên SSO; dùng cửa sổ riêng khi đổi vai trò. Keycloak demo nhập realm lại khi container khởi động; dữ liệu chuyến và tiền nằm trong volume database, độc lập với phiên đăng nhập.

## Kiểm tra

```bash
# Kiến trúc + nghiệp vụ thuần, không cần Docker
./scripts/verify.sh --core

# Kiến trúc, backend/frontend checks/build, Compose, API tích hợp thật
./scripts/verify.sh

# Gây lỗi container và kiểm tra phục hồi trên dữ liệu demo
python3 -B scripts/faults.py

# Chrome/Chromium có sẵn; có thể đặt CHROME_BIN
node scripts/browser-check.mjs --refresh
```

Ảnh kiểm thử trình duyệt được tạo trong thư mục tạm hệ điều hành; đường dẫn và log in ra terminal, không sinh thêm file trong repo. Kết quả và phạm vi kiểm thử ghi trong [docs/acceptance.md](docs/acceptance.md).

Các kịch bản lỗi có thể chạy riêng: `broker`, `duplicate`, `trip-restart`, `payment-restart`, `dead-letter`. Runner tạo và thay đổi dữ liệu mẫu. Cần môi trường demo không có chuyến đang hoạt động của tài khoản kiểm tra.

`verify.sh` dùng console/assert theo AGENTS.md; không thêm xUnit/Moq. Client `swiftride-check` cho phép password grant **chỉ trong realm demo local**, để tự động hóa kiểm tra. UI dùng Authorization Code + PKCE, không dùng password grant.

## Thanh toán và phục hồi

Không nhập số thẻ hoặc CVV. Ví và thẻ đều giả lập; số dư không phải tiền thật.

| Token demo | Kết quả |
|---|---|
| `demo-success` | Thu tiền bình thường |
| `demo-decline` | Từ chối, chuyến chờ xử lý |
| `demo-transient` | Hai lần timeout trước thu, sau đó thành công |
| `demo-unknown` | Timeout sau khi provider đã ghi nhận thu; retry không thu thêm |
| `demo-compensate` | Thất bại Saga sau thu; tạo bút toán đảo và hoàn provider |
| `demo-unavailable` | Hết lượt retry, vào error queue; cần khôi phục provider rồi replay |

Ví dụ vận hành bằng Python stdlib, không in token:

```bash
python3 - <<'PY'
import sys, uuid
sys.path.insert(0, 'scripts')
from demo import api
trip_id = 'THAY-BANG-TRIP-ID'
print(api('ops1', f'/payments/{trip_id}/refund',
          {'reason': 'Hoàn tiền demo'}, key=str(uuid.uuid4())))
PY
```

Khi yêu cầu không rõ kết quả, giữ nguyên idempotency key. Với thanh toán bị lỗi provider demo:

```bash
python3 - <<'PY'
import sys
sys.path.insert(0, 'scripts')
from demo import api
api('ops1', '/payments/THAY-BANG-TRIP-ID/demo-recover', {}, 'POST')
PY
python3 -B scripts/replay.py --queue ride-completed_error
```

Replay dùng RabbitMQ Shovel với publisher confirms, giữ message ID; không lấy message ra rồi publish theo cách có thể mất dữ liệu. API `POST /trips/{id}/retry-payment` dành cho Ops phát lại bằng event mới và cùng khóa thu tiền. Khoản bị từ chối hoặc đã bồi thường không tự đổi phương thức/thử thu mới.

## Tài liệu và giới hạn đã chọn

- [Bản đồ mã nguồn theo chức năng](docs/source-guide.md).
- [Kiến trúc, API, SOLID, pattern, trade-off](docs/architecture.md).
- [Đối chiếu PDF và checklist nghiệm thu](docs/acceptance.md).
- OpenAPI có xác thực Ops tại `/openapi/trip.json`, `/openapi/matching.json`, `/openapi/payment.json` trên Gateway.
- Tuyến demo dùng Haversine × 1,3 và vận tốc 25 km/h; sơ đồ UI chỉ minh họa.
- Hoàn tiền toàn bộ một lần; chưa có hoàn một phần, cổng tiền thật, định vị nền hoặc cloud.
- Migration đầu tạo schema v1 từ EF model; trước schema v2 phải đóng băng SQL v1, thêm snapshot và migration nâng cấp. Không tự drop database khi model thay đổi; không hỗ trợ rollback xóa dữ liệu tiền.
- Frontend chạy một instance. Trước khi scale ngang, chuyển phối hợp refresh token sang session store chung.
- Tag image và phiên bản package được cố định; dùng `dotnet restore --locked-mode` và `npm ci` trong CI.

Dừng hệ thống bằng `docker compose down`; dữ liệu database và broker còn trong volume. Không dùng `down -v` nếu cần giữ lịch sử và sổ cái.
