# Đối chiếu PDF và nghiệm thu COD

Nguồn: `RideLite-KienTrucPhanMem.pdf`, 11 trang. Theo yêu cầu cập nhật, phương thức
Card/Wallet được thay bằng COD cho chuyến mới. Payment Service, PostgreSQL, sổ cái,
idempotency và luồng hoàn tiền vẫn giữ. Giao diện mint–trắng tham khảo trải nghiệm
đặt xe be, giữ thương hiệu SwiftRide.

## Đối chiếu yêu cầu

| PDF      | Yêu cầu                           | Mã và hành vi                                                                                                                    |
| -------- | --------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| §1, §2.4 | Đúng ba service, Payment bắt buộc | Trip/PostgreSQL, Matching & Pricing/MongoDB, Payment/PostgreSQL; database/tài khoản riêng; Trip/Payment cùng instance PostgreSQL |
| §2.2     | Đặt xe, báo giá, xác nhận         | Geoapify thật, Trip Requested, quote có hạn, User chấp nhận hoặc từ chối                                                         |
| §2.2, §4 | Driver từ chối và tìm lại         | Geo-query 2dsphere, reservation nguyên tử, k=3; từ chối lần thứ tư thất bại                                                      |
| §2.2     | Đến đón, đón, trả khách           | State machine; xe giả lập trên tuyến thật; server chặn đón/trả trước khi đến                                                     |
| §2.2     | Thanh toán, đóng chuyến           | RideCompleted tạo COD Pending; Driver xác nhận thu; PaymentSettled đưa Trip sang Paid rồi giải phóng Driver                      |
| §2.3     | Công thức giá đầy đủ              | `((cd*d+ct*t)*s+toll)*promotion*(1+tax)`; decimal, làm tròn cuối; assert 91.575 VND                                              |
| §3       | Client, Gateway, integration      | Next.js/Auth.js, YARP/JWT/rate limit, REST và RabbitMQ; trình duyệt không gọi service trực tiếp                                  |
| §3.3     | Database-per-service              | Mỗi service có tài khoản và dữ liệu riêng; không truy vấn/ghi chéo database                                                      |
| §5       | Payment và refund                 | COD Pending/Settled/Refunded, ledger double-entry; Ops xác nhận tiền đã hoàn thực tế                                             |
| §6       | SOLID                             | Port nhỏ theo nhu cầu, adapter DI, State/Strategy; framework chỉ ở tầng ngoài                                                    |
| §7       | Design pattern                    | State, Strategy, Repository, Factory, Pub/Sub, Gateway, Saga và transactional Outbox                                             |
| §8       | .NET và cấu trúc phân tầng        | .NET 10, EF/MongoDB.Driver; 17 project sản phẩm, frontend phân tầng                                                              |
| §9       | Retry, idempotency, DLQ           | Lock giao dịch, unique journal, consumer Inbox, Outbox, retry hữu hạn/backoff/jitter và error queue                              |
| §9       | Bảo mật, quan sát                 | OIDC/PKCE, token phía server, quyền tài nguyên, CorrelationId, OTel/LGTM theo profile                                            |

[Bản đồ mã nguồn](source-guide.md) và [kiến trúc](architecture.md) chỉ từng tầng,
pattern và quyền sở hữu dữ liệu. [Makefile](../Makefile) quản lý chung các nhóm phát
triển mà không gộp API/process/database.

## Luồng và kiểm thử bắt buộc

- User đăng ký công khai; Ops nhập/duyệt/từ chối/khóa Driver; Driver đổi mật khẩu tạm.
- Theme Keycloak và web mint–trắng; màn hình riêng cho bản đồ, lịch sử, tiền mặt và hồ sơ.
- Tìm địa chỉ, reverse geocoding, định vị, route preview; sửa địa chỉ xóa tọa độ cũ.
- Xe giả lập có pause/resume và tốc độ 1/5/10/20, lưu MongoDB; restart không bù downtime.
- Driver không tự đổi vị trí khi bận; không xác nhận đón/trả trước khi đến.
- Trả khách chưa đồng nghĩa đã thu; COD Pending vẫn là chuyến cần xử lý, Driver giữ busy.
- Xác nhận thu cần checkbox thực nhận; Ops xác nhận thay cần lý do. Sai quyền bị chặn.
- Gửi lặp thao tác hoặc message không tạo khoản thu/hoàn thứ hai; số tiền lấy từ server.
- Trip/Payment PostgreSQL Outbox giữ sự kiện khi RabbitMQ ngừng; restart Trip/Payment vẫn phục hồi.
- Ledger cân bằng và append-only ở database; hoàn COD tạo một bút toán đảo có dấu vết Ops.
- Chi tiết chuyến hiển thị timeline trạng thái, thời điểm và kết thúc Hủy/Thất bại.
- Hồ sơ, phân trang, link trực tiếp chuyến, 360 px, keyboard/focus và đăng xuất.
- Tổng RAM dự án tối đa 12 GB; build tuần tự; tắt dịch vụ kiểm thử, giữ volume.
- Format 100 cột, mỗi câu lệnh một dòng; nhóm ngoặc ngắn gom một dòng.

## Kế hoạch chức năng SwiftBike

Phạm vi đã chốt: trải nghiệm đặt xe máy trên web như BeBike, mô phỏng xe trên bản đồ
thật; giữ ba service, Clean Architecture, SOLID và pattern trong PDF. Không phát triển
thêm payment; giữ luồng COD hiện có để không phá vòng đời chuyến.

- [x] Tuyến Geoapify `motorcycle`, giá tham khảo và thời gian trước khi tìm tài xế.
- [x] Điểm đón/đến rõ ràng, đổi chiều tuyến, ghi chú đón khách và liên hệ người đặt xe.
- [x] Tìm/nhận/từ chối chuyến, tìm lại khi tài xế từ chối; không ghép xe bận hoặc mất kết nối.
- [x] Theo dõi xe mô phỏng, ETA từng chặng, nhận biết đã đến và chặn đón/trả sai vị trí.
- [x] Hai phía hủy trước khi đón, xác nhận/lý do hủy, giải phóng đúng reservation.
- [x] Lịch sử có lọc trạng thái, đặt lại chuyến và đánh giá 1–5 sao sau khi trả khách.
- [x] Kiểm tra quyền, dữ liệu đầu vào, concurrency và migration giữ dữ liệu cũ.
- [x] Build 0 error/warning, kiểm thử nghiệp vụ và Chrome desktop/mobile ba vai trò;
      dừng container kiểm thử, tổng RAM dự án dưới 12 GB.

## Lệnh nghiệm thu

```bash
./scripts/verify.sh --core
./scripts/verify.sh
```

`verify.sh` build Release, kiểm tra TypeScript/production frontend/xUnit + Moq,
kiểm tra kiến trúc/RAM, build image tuần tự rồi chạy Chrome với `--faults`.
Runner lỗi có chủ đích dừng RabbitMQ, restart Trip/Payment, kiểm tra Inbox/Outbox,
phát message trùng và thử sửa sổ cái sai. Sau khi thành công hoặc lỗi, script dừng
container dự án và giữ volume. Ảnh/log nằm trong `/tmp`, không đưa vào repo.

Khi hệ thống đã chạy, dùng `node scripts/browser-check.mjs` cho luồng thường hoặc
thêm `--faults` cho kiểm tra sự cố. Không chạy thử sự cố trong lúc người khác dùng hệ thống.

## Kết quả phiên COD trước chuyển PostgreSQL

Kết quả lịch sử trước khi đổi Trip sang PostgreSQL (03/11/2026):

- Backend Release: **0 error, 0 warning**; TypeScript và frontend production build đạt.
- **157 assert nghiệp vụ** đạt; kiểm tra kiến trúc và assert frontend đạt.
- Chrome chạy đủ User/Driver/Ops: đăng ký, duyệt tài xế, bản đồ Geoapify thật,
  mô phỏng hành trình, COD chờ thu/xác nhận thu, hoàn tiền và sổ cái cân bằng.
- Thử dừng RabbitMQ, restart Trip/Payment, phục hồi hai Outbox, phát message trùng
  và chặn sửa sổ cái đều đạt; không tạo bút toán thu/hoàn thứ hai.
- Tải hồ sơ lỗi khóa nhập/lưu; thử lại, lưu hồ sơ, link chuyến trực tiếp, phân trang,
  giao diện 360 px, focus bàn phím và đăng xuất đều đạt. Không có exception trình duyệt
  hoặc HTTP 5xx ngoài thời gian chủ động gây sự cố.
- C#, frontend/JavaScript và Python đạt kiểm tra format theo cấu hình repo.
  Tổng hạn mức container gồm observability: **9,53/12 GB**; observability mặc định tắt.

Log và ảnh nghiệm thu nằm trong `/tmp`, không sinh file log trong repo. Runner tự dừng
container sau kiểm thử, giữ nguyên volume; dùng `make up` để mở lại hệ thống.

## Nghiệm thu sau chuyển Trip sang PostgreSQL

Ngày 03/11/2026, `./scripts/verify.sh` hoàn tất với mã thoát 0 trên cấu hình mới:

- TripDB/PayDB dùng PostgreSQL 17, Matching giữ MongoDB; tài khoản `trip` và
  `payment_app` không có quyền superuser và bị chặn truy cập database của nhau.
- Chuyển đủ **104 chuyến, 6 hồ sơ User**; đối chiếu toàn bộ bản ghi bằng SHA-256
  chuẩn hóa UUID và UTC/microsecond. Inbox/Outbox nguồn đều trống; khả năng giữ
  event mới qua restart được kiểm tra riêng bằng thử lỗi broker.
- Native backup ba database và cấu hình cũ được giữ ngoài repo tại
  `/home/quocdai210/Backups/SwiftRide/2026-11-03-trip-postgresql/`; volume SQL Server
  nguồn giữ nguyên. Nhập dữ liệu vào DB mới, nhập lại và chặn đích sai lệch đều đạt.
- Backend Release **0 error, 0 warning**; TypeScript/production frontend đạt;
  `npm ci` báo **0 vulnerabilities**.
- TripService `--migrate` tạo đủ schema và migration history trên PostgreSQL trống;
  công cụ chuyển dữ liệu kiểm tra bản ghi trong transaction trước commit.
- OpenTelemetry lưu được metric Outbox của Trip/Payment, logs và traces vào LGTM;
  cùng một trace đi qua Gateway/Trip/Matching/Payment, `CorrelationId` phản hồi đúng.
- **7 kiểm thử xUnit + Moq** đạt: bộ hợp đồng nghiệp vụ cũ, State/Strategy/Factory,
  Saga xác nhận reservation/retry/bồi thường, snapshot và quyền database thực tế.
- Chrome đạt ba vai trò, Geoapify, mô phỏng, COD chờ thu/thu/hoàn, chống ghi trùng,
  ledger cân bằng/bất biến, hai Outbox khi RabbitMQ ngừng và Trip/Payment restart.
- Hồ sơ, lịch sử, hủy chuyến, link trực tiếp, dữ liệu online cũ, mobile 360 px,
  focus bàn phím và đăng xuất đạt; không có lỗi trình duyệt ngoài sự cố chủ động.
- Format C#, frontend/JavaScript và Python đạt. Hạn mức container thường **5,23 GB**,
  gồm observability **7,38 GB**, gồm cả profile migration **10,07/12 GB**.

### Đánh giá và bước tiếp theo

- **P0/P1:** không có lỗi còn mở trong phạm vi các kiểm tra trên. Tám pattern PDF
  có bảng đối chiếu mã và kiểm thử trong `architecture.md`.
- **P2:** dọn cụm Card/Wallet cũ sau khi tách helper broker khỏi `demo.py`; giữ
  migration và dữ liệu lịch sử. Chỉ thêm GPS thật, so ETA nhiều Driver hoặc realtime
  khi có nhu cầu; đo tải trước khi thay thuật toán ghép xe hoặc tách instance DB.
- Giới hạn: chạy local một instance, một loại xe, di chuyển giả lập; chưa kiểm thử
  tải/HA. PostgreSQL dùng chung instance là điểm lỗi chung. Không coi nghiệm thu
  đồ án là bằng chứng sẵn sàng vận hành thương mại.

## Nghiệm thu bản đồ tối giản

Ngày 04/11/2026, frontend production và image `web` build thành công, không có lỗi
hoặc cảnh báo build. TypeScript, assert frontend, kiểm tra kiến trúc và format đạt.
Thay đổi thuộc tầng presentation, giữ Leaflet/Geoapify và ba service độc lập.

- Bản đồ Positron toàn màn hình, một tuyến theo chặng; nút zoom/định vị 44 px.
- Bảng thao tác desktop/mobile thu gọn bằng nút hoặc Enter; trạng thái, giá và tiền
  mặt vẫn rõ khi đóng. Bản đồ điều chỉnh theo kích thước bảng, không vẽ chồng hai tuyến.
- Lộ trình đầy đủ, lịch sử, chi tiết giá và điều khiển xe giả lập mở khi cần.
  Biên nhận COD lên đầu; tại 360 × 800 px, Driver thấy ngay checkbox và nút thu tiền.
- Chrome đạt đăng ký User, Ops duyệt Driver, Geoapify thật, mô phỏng, đón/trả đúng
  vị trí, COD chờ thu/thu/hoàn, quyền sở hữu và sổ cái chống ghi trùng.
- Ba vai trò đạt bố cục 360 px, focus bàn phím, link chuyến và đăng xuất; kiểm tra
  thêm bản đồ ở 360 × 600 px. Không có exception trình duyệt hoặc HTTP 5xx.

Kiểm tra bằng `node scripts/browser-check.mjs`; ca sự cố broker không chạy lại trong
phiên chỉnh giao diện này. Ảnh/log ở `/tmp`; container kiểm thử được dừng, giữ dữ liệu.

## Nghiệm thu chức năng SwiftBike

Ngày 04/11/2026, build backend Release đạt **0 error, 0 warning**; frontend production
và image Trip/Matching/web build thành công. TypeScript, assert frontend, kiểm tra
kiến trúc, CSharpier và Prettier đạt; không thêm dependency.

- Geoapify thật trả tuyến `motorcycle`; User xem giá tham khảo, đổi điểm, lưu liên hệ
  và ghi chú đón. Driver được giao chỉ nhận số điện thoại khách từ lúc nhận chuyến
  đến trước trả khách. ETA giảm theo hành trình, kiểm tra đến nơi vẫn thực hiện ở server.
- Chrome kiểm tra Rider hủy ở EnRoute, Driver hủy ở DriverAccepted, xác nhận/lý do,
  chặn tài xế khác và giải phóng reservation. Sau tài xế từ chối, User xác nhận lại
  giá rồi tài xế khác nhận; khi không có xe, chuyến kết thúc Failed.
- User đánh giá sau trả khách ngay khi COD còn Pending; cùng nội dung gửi lại không
  tăng Version, nội dung khác/sai sao/sai quyền bị chặn. PostgreSQL chặn Rating=0.
  Lịch sử lọc từ server; đặt lại chỉ điền địa chỉ, chưa tạo chuyến cho đến khi gửi form.
- **8 kiểm thử xUnit + Moq** đạt, gồm quyền database thật và bồi thường reservation.
  Chrome luồng thường và `--faults` đều đạt; hai Outbox phục hồi sau broker ngừng và
  Trip/Payment restart, thu/hoàn COD và message trùng không tạo giao dịch thứ hai.
- Bố cục ba vai trò đạt 360 px; kiểm tra thêm giá/nút đặt xe và hộp xác nhận hủy,
  focus nằm trong dialog, các nút đủ 44 px; bản đồ thu gọn đạt ở 360 × 600 px.
  Không có exception trình duyệt hoặc HTTP 5xx ngoài thời gian gây sự cố chủ động.
- Migration `20261103183600_BikeExperience` thêm liên hệ, ghi chú và đánh giá;
  giữ đủ **113 chuyến có sẵn trước nâng cấp**. Backup native ở
  `/home/quocdai210/Backups/SwiftRide/bike-20261104-014014/`, không xóa volume.

Giữ ba service và database riêng theo quyền sở hữu; Trip/Payment PostgreSQL,
Matching MongoDB. Hạn mức tất cả profile **10,07/12 GB**; observability/migration
không bật khi kiểm thử chức năng. Container kiểm thử đã dừng, dữ liệu được giữ;
ảnh/log ở `/tmp`. Dùng `make up` rồi mở http://localhost:3000 để chạy lại.

Giới hạn đã chốt: một loại xe máy, mô phỏng trên bản đồ thật; chưa làm GPS thật,
chat, thông báo đẩy hoặc triển khai HA. Giữ COD hiện có, không phát triển online payment.

## Trình tự bảo vệ

1. Chỉ ba service, ba database và chiều phụ thuộc Clean Architecture.
2. Đăng ký User; Ops nhập/duyệt Driver; Driver đổi mật khẩu và bật nhận chuyến.
3. Chọn địa chỉ/bản đồ/định vị; xem tuyến, nhận báo giá và xác nhận.
4. Driver nhận, đến đón, điều khiển xe giả lập, đón và trả khách.
5. Chỉ khoản COD chờ thu; Driver xác nhận thực nhận; xem Trip Paid và Driver được giải phóng.
6. Ops xem biên nhận, ghi nhận hoàn tiền thực tế; kiểm tra journal đảo và lịch sử còn nguyên.
7. Trình bày kết quả kiểm thử lỗi broker/restart/idempotency; chỉ vị trí State/Strategy/Factory/Outbox.

Giới hạn: một loại xe, xe di chuyển giả lập trên bản đồ thật, một instance local;
chưa kiểm thử tải/HA/thiết bị GPS thật. Online payment không hoạt động trong bản COD.
Dữ liệu online cũ giữ nguyên, chỉ đọc và cần Ops đối soát bên ngoài nếu còn dang dở.
