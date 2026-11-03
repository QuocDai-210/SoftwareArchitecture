# SwiftRide

Đồ án theo `RideLite-KienTrucPhanMem.pdf`: Next.js/Tailwind, .NET 10, YARP,
Trip Service/PostgreSQL, Matching & Pricing/MongoDB, Payment Service/PostgreSQL,
RabbitMQ, Saga và Outbox. Giữ riêng container, mã nguồn và database từng service.

Giao diện mint–trắng tham khảo trải nghiệm đặt xe be: bản đồ làm trung tâm, bảng
thao tác nổi, điều hướng riêng cho User/Driver/Ops và theme Keycloak đồng bộ.
Chuyến mới chỉ thanh toán COD; Payment Service vẫn sở hữu sổ cái PostgreSQL.
[Quản lý chung bằng Makefile](docs/service-consolidation-proposal.md): bật/tắt, build,
log và kiểm tra tại một nơi; API, database và các tầng mã nguồn vẫn độc lập.

## Chạy hệ thống

Nếu đang có dữ liệu Trip trong SQL Server, thực hiện mục “Chuyển dữ liệu Trip từ
SQL Server” trước khi bật phiên bản mới.

Cần Docker Engine/Compose có quyền truy cập, GNU Make, mạng tải package/image và Geoapify key.
Build/check trên host cần .NET SDK 10 và Node.js 24 trở lên. SQL Server chỉ cần khi chuyển dữ liệu cũ.

1. Chạy `python3 scripts/setup.py`. Script tạo secret nội bộ, bổ sung biến thiếu,
   giữ nguyên giá trị đã có và đặt `.env` quyền 0600.
2. Tạo project tại [Geoapify My Projects](https://myprojects.geoapify.com/).
   Điền key phía server vào `GEOAPIFY_API_KEY`; key bản đồ trình duyệt vào
   `NEXT_PUBLIC_GEOAPIFY_MAP_KEY` trong `.env`. Key trình duyệt cần cho phép origin
   `http://localhost:3000`; key server cần dùng được cho geocoding/routing.
   `setup.py` không sinh API key bên ngoài. Không đưa `.env` vào Git.
3. Tại thư mục gốc repo, build tuần tự rồi khởi động và đồng bộ danh tính:

```bash
make build
make up
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
Sau trả khách, khoản COD ở trạng thái chờ thu. Driver tích xác nhận đã nhận đủ tiền,
bấm “Xác nhận đã thu tiền”; Payment ghi sổ rồi Trip hoàn tất và giải phóng Driver.
Ops xem tiền mặt, xác nhận thay có lý do hoặc ghi nhận hoàn tiền đã trả thực tế.
Các thao tác tài chính dùng idempotency key; tải lại/gửi lặp không tạo khoản thu thứ hai.

Điều hướng `#ride`, `#history`, `#payments`, `#operations`, `#profile` tương ứng
bản đồ, lịch sử, tiền mặt, quản lý Driver và hồ sơ. Link `#ride/{tripId}` mở trực tiếp
chuyến được phép xem. Trên điện thoại có thể thu gọn bảng thao tác để xem bản đồ.

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

## Quản lý chung

`make` hiển thị hướng dẫn. Makefile gọi Docker Compose hiện có, quản lý các nhóm
sau, giữ volume và mạng; tài khoản DB được tách theo service:

| Lệnh            | Phạm vi                                                 |
| --------------- | ------------------------------------------------------- |
| `make up`       | Toàn hệ thống, tắt observability, đồng bộ Keycloak      |
| `make trip`     | Trip API + PostgreSQL/postgres-init + Keycloak/RabbitMQ |
| `make matching` | Matching API + MongoDB + Keycloak/RabbitMQ              |
| `make payment`  | Payment API + PostgreSQL + Keycloak/RabbitMQ            |
| `make stop`     | Dừng toàn bộ container dự án, giữ dữ liệu               |
| `make status`   | Trạng thái tất cả container dự án                       |
| `make logs`     | Theo dõi log dự án; 100 dòng gần nhất mỗi container     |
| `make build`    | Kiểm tra kiến trúc/RAM, dừng dự án, build image tuần tự |
| `make check`    | Chạy `scripts/verify.sh`                                |

`make trip`, `make matching`, `make payment` là **chuyển chế độ phát triển**: dừng
các container dự án đang chạy trước khi bật nhóm được chọn và dependency bắt buộc.
Dùng từng lệnh riêng; `make up` phục vụ luồng đặt xe trên web và tích hợp liên service.
Nhóm riêng phục vụ làm việc với API/database của nhóm đó; nhóm Trip chưa đủ để
kiểm tra ghép xe/thanh toán khi Matching/Payment đang tắt. Các lệnh Compose trực tiếp
vẫn dùng được. `make build` và `make check` cũng dừng hệ thống để giữ ngân sách RAM.

## RAM và vòng đời service

Tổng RAM dự án, gồm build và kiểm thử, tối đa **12 GB**. Build nặng chạy tuần tự,
dừng container dự án trước build. Hạn mức container mặc định **5,23 GB**;
bật thêm observability là **7,38 GB**, không dùng swap. Hạn mức không gồm OS/IDE.
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

# Build, checks, Docker/Chrome: ba vai trò, bản đồ, COD, hoàn tiền và lỗi broker
./scripts/verify.sh

# Khi hệ thống đã chạy và đã sync identity; cần Chrome/Chromium, có thể đặt CHROME_BIN
node scripts/browser-check.mjs
# Thêm thử dừng broker, restart Trip/Payment và kiểm tra Outbox
node scripts/browser-check.mjs --faults
# Thêm kiểm tra refresh sau 300 giây
node scripts/browser-check.mjs --refresh
```

`verify.sh` dừng service trước build và sau kiểm thử, kể cả khi lỗi. Muốn xem web sau
kiểm thử: `make up`. Runner trình duyệt tạo tài khoản,
hồ sơ và chuyến kiểm tra có tên `check-*`; ảnh/log nằm ngoài repo trong thư mục tạm.
`verify.sh` dùng runner trình duyệt COD với `--faults`; không cần mở password grant.
Kiểm thử backend dùng xUnit + Moq; kiểm thử đơn vị chạy không cần Docker.
Các runner Wallet/Card cũ (`scripts/demo.py`, `scripts/faults.py`) chỉ lưu tham khảo,
không dùng nghiệm thu COD. Nhánh backend `--integration` cũ đã được thay bằng xUnit
và runner trình duyệt COD.
`replay.py` vẫn hỗ trợ đưa message từ error queue về hàng đợi qua Shovel với publisher confirms.

Giới hạn: một loại xe, xe giả lập, chạy local một instance, chưa HA/load test.
Card/Wallet/VNPAY và nạp ví đã tắt ở runtime; giao dịch cũ giữ nguyên, chỉ đọc và cần
Ops đối soát bên ngoài nếu còn dang dở. Không cần key VNPAY/ngrok để chạy COD.
Ba database nghiệp vụ và Payment Service vẫn giữ theo PDF. TripDB/PayDB dùng chung
instance PostgreSQL, tài khoản `trip`/`payment_app` chỉ được truy cập database mình;
Matching giữ MongoDB. PostgreSQL quản trị dùng tài khoản riêng `swiftride_admin`. Backend/frontend
có lockfile; dùng `dotnet restore --locked-mode`, `npm ci` khi cài dependency.

## Chuyển dữ liệu Trip từ SQL Server

Chạy trong thời gian bảo trì, trước khi mở ghi trên phiên bản mới. Giữ volume
`trip-data`/`pay-data`/`match-data`; không dùng `down -v`. `setup.py` bổ sung
`POSTGRES_ADMIN_PASSWORD`, không đổi secret cũ. PostgreSQL provisioning hỗ trợ cả
volume mới và volume Payment cũ; tài khoản bootstrap `payment` cũ bị tắt đăng nhập.

```bash
make stop
python3 -B scripts/setup.py
docker compose --profile migration up -d --wait sqlserver mongo postgres
docker compose run --rm --no-deps postgres-init
python3 -B scripts/migrate-trip.py export /tmp/swiftride-transfer
python3 -B scripts/migrate-trip.py import /tmp/swiftride-transfer
python3 -B scripts/migrate-trip.py verify /tmp/swiftride-transfer
make build
make up
```

Thư mục xuất phải chưa tồn tại và nằm ngoài repo; chứa backup native ba database,
JSON chuyển Trip và manifest SHA-256, quyền thư mục 0700/file 0600. Công cụ chỉ chạy
khi API/worker dừng, kiểm tra toàn bộ bản ghi trước commit và đối chiếu sau import;
không ghi đè đích đã có dữ liệu khác. Inbox/Outbox giữ ID, sequence được khôi phục;
RowVersion kỹ thuật của SQL Server chuyển sang cơ chế PostgreSQL. UUID được đối chiếu
theo giá trị; thời gian UTC đối chiếu ở độ chính xác microsecond của PostgreSQL.
Backup gốc giữ nguyên độ chính xác 100 ns của SQL Server.

SQL Server thuộc profile `migration`, không bật khi chạy thường. `make stop` dừng cả
profile này. Quay lại phiên bản/config cũ và volume gốc chỉ trước khi mở ghi mới;
sau khi có phát sinh phải đối soát và sao lưu trước khi khôi phục. Nếu quay lại
connection string Payment cũ, quản trị viên phải bật lại LOGIN cho role bootstrap
`payment`; role này vẫn có trong volume nhưng đã bị tắt đăng nhập.
