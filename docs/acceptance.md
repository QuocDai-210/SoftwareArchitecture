# Đối chiếu PDF và nghiệm thu COD

Nguồn: `RideLite-KienTrucPhanMem.pdf`, 11 trang. Theo yêu cầu cập nhật, phương thức
Card/Wallet được thay bằng COD cho chuyến mới. Payment Service, PostgreSQL, sổ cái,
idempotency và luồng hoàn tiền vẫn giữ. Giao diện mint–trắng tham khảo trải nghiệm
đặt xe be, giữ thương hiệu SwiftRide.

## Đối chiếu yêu cầu

| PDF      | Yêu cầu                           | Mã và hành vi                                                                                               |
| -------- | --------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| §1, §2.4 | Đúng ba service, Payment bắt buộc | Trip/SQL Server, Matching & Pricing/MongoDB, Payment/PostgreSQL; database/container độc lập                 |
| §2.2     | Đặt xe, báo giá, xác nhận         | Geoapify thật, Trip Requested, quote có hạn, User chấp nhận hoặc từ chối                                    |
| §2.2, §4 | Driver từ chối và tìm lại         | Geo-query 2dsphere, reservation nguyên tử, k=3; từ chối lần thứ tư thất bại                                 |
| §2.2     | Đến đón, đón, trả khách           | State machine; xe giả lập trên tuyến thật; server chặn đón/trả trước khi đến                                |
| §2.2     | Thanh toán, đóng chuyến           | RideCompleted tạo COD Pending; Driver xác nhận thu; PaymentSettled đưa Trip sang Paid rồi giải phóng Driver |
| §2.3     | Công thức giá đầy đủ              | `((cd*d+ct*t)*s+toll)*promotion*(1+tax)`; decimal, làm tròn cuối; assert 91.575 VND                         |
| §3       | Client, Gateway, integration      | Next.js/Auth.js, YARP/JWT/rate limit, REST và RabbitMQ; trình duyệt không gọi service trực tiếp             |
| §3.3     | Database-per-service              | Mỗi service có tài khoản và dữ liệu riêng; không truy vấn/ghi chéo database                                 |
| §5       | Payment và refund                 | COD Pending/Settled/Refunded, ledger double-entry; Ops xác nhận tiền đã hoàn thực tế                        |
| §6       | SOLID                             | Port nhỏ theo nhu cầu, adapter DI, State/Strategy; framework chỉ ở tầng ngoài                               |
| §7       | Design pattern                    | State, Strategy, Repository, Factory, Pub/Sub, Gateway, Saga và transactional Outbox                        |
| §8       | .NET và cấu trúc phân tầng        | .NET 10, EF/MongoDB.Driver; 17 project sản phẩm, frontend phân tầng                                         |
| §9       | Retry, idempotency, DLQ           | Lock giao dịch, unique journal, consumer Inbox, Outbox, retry hữu hạn/backoff/jitter và error queue         |
| §9       | Bảo mật, quan sát                 | OIDC/PKCE, token phía server, quyền tài nguyên, CorrelationId, OTel/LGTM theo profile                       |

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
- SQL/PostgreSQL Outbox giữ sự kiện khi RabbitMQ ngừng; restart Trip/Payment vẫn phục hồi.
- Ledger cân bằng và append-only ở database; hoàn COD tạo một bút toán đảo có dấu vết Ops.
- Hồ sơ, phân trang, link trực tiếp chuyến, 360 px, keyboard/focus và đăng xuất.
- Tổng RAM dự án tối đa 12 GB; build tuần tự; tắt dịch vụ kiểm thử, giữ volume.
- Format 100 cột, mỗi câu lệnh một dòng; nhóm ngoặc ngắn gom một dòng.

## Lệnh nghiệm thu

```bash
./scripts/verify.sh --core
./scripts/verify.sh
```

`verify.sh` build Release, kiểm tra TypeScript/production frontend/native assert,
kiểm tra kiến trúc/RAM, build image tuần tự rồi chạy Chrome với `--faults`.
Runner lỗi có chủ đích dừng RabbitMQ, restart Trip/Payment, kiểm tra Inbox/Outbox,
phát message trùng và thử sửa sổ cái sai. Sau khi thành công hoặc lỗi, script dừng
container dự án và giữ volume. Ảnh/log nằm trong `/tmp`, không đưa vào repo.

Khi hệ thống đã chạy, dùng `node scripts/browser-check.mjs` cho luồng thường hoặc
thêm `--faults` cho kiểm tra sự cố. Không chạy thử sự cố trong lúc người khác dùng hệ thống.

## Kết quả phiên COD

Ngày 03/11/2026, `./scripts/verify.sh` hoàn tất thành công:

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
