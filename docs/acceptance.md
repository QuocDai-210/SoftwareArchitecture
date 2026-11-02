# Đối chiếu PDF và nghiệm thu

Nguồn: `RideLite-KienTrucPhanMem.pdf`, 11 trang. Phạm vi hiện tại ưu tiên Geoapify
và ba vai trò User/Driver/Ops; VNPAY tạm hoãn theo yêu cầu. Payment Service cùng
PostgreSQL và lõi sổ cái vẫn giữ vì PDF yêu cầu.

## Ranh giới kiến trúc

| PDF      | Yêu cầu                                      | Mã nguồn / cách kiểm tra                                                                          |
| -------- | -------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| §1, §2.4 | Ba service nghiệp vụ, Payment bắt buộc       | Trip, Matching & Pricing, Payment; container và database riêng trong Compose                      |
| §2.2     | Tạo chuyến trong SQL Server                  | `TripCommands`, EF migrations, Saga bền vững                                                      |
| §2.2–§4  | Ghép gần nhất, Driver từ chối, k=3           | MongoDB 2dsphere/reservation nguyên tử; `MatchingPolicy`, `TripState`; lần từ chối thứ 4 thất bại |
| §2.2     | User xác nhận hoặc từ chối báo giá           | `confirm-quote`, `cancel`, hạn báo giá, giao diện chi tiết giá                                    |
| §2.2     | Đến đón, đón, trả khách                      | State, `TripCommands.Move`, `SimulationWorkflow.EnsureArrival`                                    |
| §2.2     | Thanh toán và đóng chuyến                    | `RideCompleted`, `PaymentSettled`, Payment/Wallet/ledger cũ; VNPAY chưa nghiệm thu                |
| §2.3     | Công thức giá đầy đủ                         | `FareCalculator`: `((cd*d+ct*t)*s+toll)*promotion*(1+tax)`; assert 91.575 VND                     |
| §3       | Presentation, Edge, Application, Integration | Next.js/Tailwind, YARP, Application/Domain, adapter HTTP/RabbitMQ                                 |
| §3       | Polyglot persistence                         | Trip/SQL Server, Matching/MongoDB, Payment/PostgreSQL; không ghi chéo DB                          |
| §3, §5   | Gateway, JWT, rate limit, aggregation        | Next server proxy; YARP; service tự kiểm quyền; `/screens/trips/{id}`                             |
| §5       | API Trip, match, quote, payment/refund       | Endpoint tại `<Service>/Api/Endpoints/`; OpenAPI cho Ops qua Gateway                              |
| §6       | SOLID                                        | Port ở Application, adapter ở Infrastructure; Strategy giá, State, port theo use case             |
| §7       | State, Strategy, Repository, Factory         | TripState, pricing decorators, repositories, PaymentMethodFactory                                 |
| §7       | Pub/Sub, Gateway, Saga, Outbox               | RabbitMQ/MassTransit, YARP, process manager, EF transactional Outbox                              |
| §8, §8.1 | .NET, EF, MongoDB, layout phân tầng          | 17 project sản phẩm; Domain/Application không có framework; frontend chia tầng                    |
| §9       | Nhất quán, retry, idempotency, DLQ           | Transaction nội bộ, event liên service, fencing token, retry giới hạn, error queue/replay         |
| §9       | Bảo mật, quan sát                            | Keycloak OIDC/PKCE, token ở server, ownership, CorrelationId; OTel/LGTM theo profile              |

[Kiến trúc](architecture.md) giải thích chiều phụ thuộc và trade-off;
[bản đồ mã nguồn](source-guide.md) chỉ folder/file theo chức năng.
[Đề xuất gom chức năng](service-consolidation-proposal.md) chờ review;
không gộp service, container hoặc database trong lần này.

## Bổ sung theo phạm vi môn học

- User đăng ký công khai; Driver phải do Ops nhập và duyệt hồ sơ. Driver đổi mật khẩu
  tạm khi đăng nhập lần đầu. Ba vai trò không kế thừa quyền nhau.
- Keycloak giữ dữ liệu trong volume; script đồng bộ giữ subject và mật khẩu cũ.
- Geoapify autocomplete/reverse/routing; Leaflet tiles có attribution, chọn điểm
  trên bản đồ hoặc dùng định vị trình duyệt. Sửa địa chỉ xóa tọa độ đã chọn.
- Xe giả lập trên đường thật; tốc độ 1/5/10/20, pause/resume, lưu tiến độ trong MongoDB.
  Không cộng thời gian downtime; optimistic revision tránh ghi đè giữa tick/control.
- User theo dõi xe của chuyến mình; Driver/Ops điều khiển. Chặn đón/trả khi chưa đến.
- Xe chưa có hành trình trả JSON `null`, tránh Gateway đọc body rỗng. Giao diện cập
  nhật từ phản hồi điều khiển và bỏ kết quả polling cũ để giữ tốc độ/pause vừa chọn.
- Hồ sơ cá nhân; lịch sử và quản trị hồ sơ Driver có phân trang phía server.
- Driver mới ghi rõ `Suspended=false`; khởi động Matching bổ sung trường thiếu cho
  bản ghi cũ, giữ nguyên quyết định khóa đã có để bộ lọc ghép xe hoạt động đúng.
- Gateway tách hạn mức đặt xe/đọc/ghi để polling không chặn lệnh Driver; vẫn giữ
  10 yêu cầu đặt xe/phút theo danh tính.
- RAM dự án tối đa 12 GB; build tuần tự, chỉ bật service cần thiết, tắt sau kiểm thử.
- Format 100 cột; mỗi câu lệnh một dòng; nhóm ngoặc ngắn gom một dòng.

## Kết quả lần cập nhật hiện tại

`./scripts/verify.sh` chạy thành công từ đầu đến cuối. Kiểm thử gồm 160 lượt đọc
trước khi điều khiển xe; container kiểm thử đã tự dừng, giữ nguyên volume.

| Kiểm tra             | Kết quả                                                                                                                                                                    |
| -------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Backend solution     | Build thành công, 0 warning, 0 error                                                                                                                                       |
| Nghiệp vụ / adapter  | 144 assert qua; có BSON roundtrip của simulation/Driver và retry lỗi bản đồ                                                                                                |
| Frontend             | TypeScript strict, production build, assert service qua                                                                                                                    |
| Kiến trúc            | 17 project backend, 31 file frontend qua kiểm tra chiều phụ thuộc                                                                                                          |
| RAM Compose          | 12 service, tổng hạn mức 9,53/12 GB; mặc định 7,38 GB                                                                                                                      |
| Setup                | Giữ giá trị cũ, chỉ sinh secret nội bộ, key bên ngoài để trống, quyền 0600                                                                                                 |
| Geoapify             | Autocomplete và routing thật trả HTTP 200                                                                                                                                  |
| Docker / trình duyệt | PASS: container healthy, User đăng ký, Ops duyệt Driver, bản đồ thật, trọn hành trình, hủy, hồ sơ, 360 px, bàn phím, logout; không có lỗi hiển thị hoặc exception/HTTP 5xx |

Log build/check và ảnh trình duyệt nằm trong thư mục tạm hệ điều hành, không đưa
vào repo. `0 warning/0 error` là kết quả build; không có nghĩa mọi log vận hành đều
không có cảnh báo. Chưa chạy tải, HA hoặc thiết bị di động thật.

Các kết quả payment/fault/observability từ phiên bản demo trước không được tính là
nghiệm thu lại phiên bản hiện tại. Runner cũ còn trong source để đối chiếu, cần cấu
hình fixture/client cũ nếu chạy; `verify.sh` hiện kiểm tra luồng bản đồ/User/Driver/Ops.
Chưa tuyên bố nghiệm thu toàn bộ PDF khi phần thanh toán thật đang hoãn.

## Trình tự bảo vệ

1. Giải thích ba service và quyền sở hữu ba database; chỉ chiều phụ thuộc Clean Architecture.
2. Đăng ký User; Ops tạo, duyệt Driver; Driver đổi mật khẩu và bật nhận chuyến.
3. Chọn địa chỉ/bản đồ/định vị; xem tuyến và báo giá; xác nhận hoặc hủy.
4. Driver nhận, đến đón, pause/resume/tăng tốc, xác nhận đón và trả; User theo dõi xe.
5. Xem lịch sử, hồ sơ, thống kê và quản trị Driver; kiểm tra màn hình 360 px.
6. Chỉ mã minh họa SOLID/pattern, Outbox/Saga và giới hạn payment đang hoãn.
