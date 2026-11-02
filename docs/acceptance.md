# Đối chiếu yêu cầu PDF và nghiệm thu

Nguồn: `RideLite-KienTrucPhanMem.pdf`, 11 trang, nội dung đánh số 1–9. Bao gồm phần mở rộng messaging, Outbox và observability theo lựa chọn đã chốt; không chỉ luồng tối thiểu.

**Phân biệt:** “Có mã/cấu hình” không đồng nghĩa “đã chạy tích hợp”. Backend, frontend và hạ tầng đã chạy trong Docker. Kết quả được ghi theo từng phép kiểm tra bên dưới.

## Ma trận bao phủ

| PDF | Yêu cầu | Triển khai / bằng chứng kiểm tra |
|---|---|---|
| §1, §2.4 | Tối đa 3 service, Payment bắt buộc | TripService, MatchingService, PaymentService; Compose; sơ đồ container |
| §2.1–2.2 | Kết nối Rider/Driver, trọn luồng đặt xe đến trả tiền | Hai giao diện theo vai trò; API chuyến; integration happy path |
| §2.2 bước 2 | Tạo Requested trong SQL Server | POST /trips + EF migration; assert trạng thái đầu |
| §2.2 bước 3 | Tìm gần nhất, lưu MongoDB | 2dsphere, Near query, vị trí mới, reservation nguyên tử |
| §2.2 bước 4 | Rider đồng ý hoặc từ chối giá | confirm-quote/cancel; UI hiển thị hạn và chi tiết giá |
| §2.2 bước 5, §4 | Driver từ chối và k lần tìm lại | Loại tài xế cũ; báo giá mới; assert + integration 4 lần từ chối với k=3 |
| §2.2 bước 6 | Đang đến, đón, trả khách | State + en-route/pickup/dropoff; cấm chuyển sai thứ tự |
| §2.2 bước 7–8 | Sự kiện thanh toán, ledger PostgreSQL, đóng chuyến sau thanh toán | RideCompleted, PaymentSettled, ledger; chỉ Paid sau settlement khớp |
| §2.3 | Công thức giá | FareCalculator; assert 91.575 VND gồm surge/toll/promo/tax |
| §3.1 | Presentation / Edge / Application / Integration | Next.js/Tailwind, YARP, service sở hữu DB, RabbitMQ; architecture.md |
| §3.1–3.2 | Client qua Gateway, JWT, rate limit, aggregation | Server proxy chỉ gọi Gateway; policy JWT; native rate limiter; /screens/trips/{id} |
| §3.2 | Dùng YARP, phân biệt Gateway/BFF | Gateway chung; không tách Rider BFF/Driver BFF; tài liệu giải thích |
| §3.3 | SQL Server, MongoDB, PostgreSQL; polyglot persistence | Ba database và tài khoản riêng; docs trade-off; Compose không public DB |
| §4 | Flowchart gồm hủy, hết tài xế, retry/payment chờ xử lý | Mermaid và TripState; PaymentReview khi lỗi, không đánh dấu thành công sai |
| §5.1 | API Trip tiêu biểu | POST /trips, /driver-accept, /pickup, /dropoff cùng các endpoint bổ sung |
| §5.2 | POST /match, /quote, Strategy giá | REST nội bộ match; quote đã xác thực; Standard/Surge/Promo |
| §5.3 | POST /payments, /refund; idempotency, ledger, bồi thường | Unique key/trip, advisory locks, reversal, provider idempotency; check integration/faults |
| §5.3 | Không lưu số thẻ/CVV, dùng token | UI chỉ chọn token demo; API không có trường số thẻ/CVV |
| §6.1 | SRP | Tách FareCalculator, Repository, workflow, payment method |
| §6.2 | OCP | IPricingStrategy và các implementation độc lập |
| §6.3 | LSP | Assert chung nhiều distance/promo cho từng Strategy, giá không âm và validation |
| §6.4 | ISP | ITripReader / ITripWriter |
| §6.5 | DIP | DI ASP.NET Core, phụ thuộc interface thay kết nối cụ thể |
| §7 | State | TripState điều khiển transition |
| §7 | Strategy | StandardPricing, SurgePricing, PromoPricing |
| §7 | Repository | TripRepository, DriverRepository, EF DbContext/DbSet tại Payment |
| §7 | Factory | PaymentMethodFactory chọn CardMethod/WalletMethod |
| §7 | Observer/Pub-Sub | RabbitMQ consumers cho Trip, Matching và Payment |
| §7 | API Gateway | YARP routes, auth/rate-limit/aggregation |
| §7 | Saga | Process manager bền vững; release reservation; charge/refund khi compensation |
| §7 | Outbox | MassTransit EF Bus/Consumer Outbox cho Trip/Payment |
| §8 | .NET 10, EF Core 10, MongoDB.Driver | Project references và phiên bản package cố định |
| §8 | RabbitMQ/MassTransit, OAuth/OIDC, Docker Compose, OpenTelemetry | Cấu hình chạy thật, không thay bằng mock in-memory |
| §8 | xUnit/Moq được đề xuất | Thay bằng console/assert theo AGENTS.md; không thêm test framework |
| §8.1 | Layout Domain/Application/Infrastructure/Api | Mỗi service giữ ranh giới trong các file hiện có; Shared Core/Hosting tối thiểu; solution và tests riêng |
| §9.1 | Trade-off, modular monolith, chi phí phân tán | architecture.md; phát triển theo chặng từ lõi đến broker/observability |
| §9.2 | Strong/eventual consistency, Outbox nguyên tử | Transaction trong SQL Server/PostgreSQL; propagation qua event |
| §9.2 | Consumer idempotent, at-least-once | Inbox, unique business key, status guards; duplicate fault scenario |
| §9.2 | Timeout, backoff/jitter, retry giới hạn, DLQ | HTTP handler, MassTransit retry, *_error, Shovel on-confirm replay |
| §9.2 | Saga bồi thường không thay transaction | ReservationReleased và demo-compensate; ledger đảo, docs ranh giới transaction |
| §9.3 | OIDC/JWT, service kiểm tra tài nguyên | Keycloak; Rider/Driver/Service/Ops policy; ownership ở service |
| §9.3 | Secret ngoài source, không log thông tin nhạy cảm | setup.py, .env.example, HttpOnly session, DTO không trả token; không bật sensitive logging |
| §9.3 | CorrelationId/trace context và OTel | GUID header, request scope, HTTP/MassTransit instrumentation, LGTM |
| §9.3 | Các chỉ số vận hành | Grafana dashboard + broker metrics + Outbox gauge |

## Kết quả đã chạy trong phiên bàn giao

| Kiểm tra | Kết quả |
|---|---|
| `./scripts/verify.sh --core` | PASS, 111 assert; build Core và console runner thật bằng .NET 10 |
| `docker compose config --quiet` | PASS; đã sinh .env cục bộ, không in secret |
| Python `py_compile` và Bash `bash -n` | PASS |
| Build toàn bộ .NET solution | PASS, 0 warning, 0 error; đủ bảy package lock |
| EF runtime model SQL Server/PostgreSQL | PASS, validate model và sinh schema thực |
| Migration SQL Server | PASS, chạy migration v1 trên SQL Server container |
| Keycloak và vai trò | PASS, đăng nhập rider1/driver1/ops1; đúng role và audience |
| Build Next.js/Auth.js | PASS, Next.js 16.4.3 và Auth.js beta.32; TypeScript strict; npm audit 0 lỗ hổng |
| API qua Gateway | PASS, 39 kiểm tra tích hợp: Wallet/refund, sáu token thẻ, quyền sở hữu/vai trò, idempotency, k=3, race reservation |
| PostgreSQL guards | PASS, cấm số dư âm, journal mất cân bằng và sửa journal |
| Năm fault scenarios | PASS, broker, duplicate, Trip restart, Payment restart, dead-letter/replay |
| Crash và Outbox bền vững | PASS, restart Trip khi broker tắt vẫn giữ SQL Outbox; SIGKILL Payment sau capture vẫn thu đúng một lần |
| Grafana/OTel | PASS, tất cả sáu PromQL có dữ liệu; log có service_name; trace qua đủ bốn API |
| Trình duyệt Chrome | PASS, OIDC Rider/Driver, danh tính khớp Keycloak sub, trọn hành trình, thanh toán, hủy, 360 px/keyboard, refresh sau 300 giây, logout |
| Kiểm tra biên bổ sung | PASS, JWT sai audience/malformed; Mongo gần nhất/offline/stale/ngoài 5 km; reservation fencing; booking sai dữ liệu, hết tài xế và ví thiếu tiền |
| Secret | PASS, không có mật khẩu/secret đã sinh trong source; `.env` quyền 0600 |

## Chạy lại nghiệm thu

1. Chạy setup và Compose theo README; chỉ tạo lại lockfile khi cập nhật dependency. Xác nhận service healthy và không có lỗi migration/consumer trong log.
2. Chạy `./scripts/verify.sh`; lưu kết quả build và integration, không sửa dữ liệu kiểm tra giữa các bước.
3. Chạy `python3 scripts/faults.py`; ghi nhận trạng thái PaymentPending/PaymentReview, số bút toán và phục hồi Paid.
4. Dùng hai cửa sổ Rider/Driver hoàn thành một chuyến; kiểm tra giá trước xác nhận, hành trình và thanh toán.
5. Mở dashboard, tìm cùng CorrelationId/trace ID qua Gateway, Trip, Payment; xác nhận metrics tăng khi gây lỗi.
6. Lưu ảnh/log mới, đối chiếu bảng bao phủ và bằng chứng bên dưới.

## Bằng chứng bàn giao

Ngày kiểm tra: 02/11/2026. Các ảnh/log nằm trong `artifacts/`, không chứa mật khẩu hoặc token và được `.gitignore` loại.

| Bằng chứng | Nội dung |
|---|---|
| `core-checks.txt` | 111 assert: công thức giá, hợp đồng Strategy/LSP, State, expiry quote, version, k=3, ledger, khóa yêu cầu |
| `backend-build.txt`, `frontend-build.txt`, `npm-audit.json` | Backend 0 warning/error; Next.js production build; npm audit 0 lỗ hổng |
| `integration-checks.txt` | API thật qua Gateway, idempotency, quyền sở hữu/vai trò, Wallet/refund, sáu kịch bản Card, tranh reservation |
| `fault-checks.txt` | Năm ca lỗi container và phục hồi |
| `durable-fault-checks.txt` | SQL Outbox còn event qua Trip restart lúc broker tắt; SIGKILL Payment sau provider capture, không thu lặp |
| `edge-checks.txt` | Booking sai dữ liệu, không có tài xế, ví thiếu tiền, hoàn lại số dư; rate limit trả 429 ở yêu cầu thứ 11 |
| `browser-checks.txt` | OIDC, trọn luồng hai vai trò, không trả token trong session, 360 px/keyboard, refresh sau expiry và logout |
| `rider-desktop.png`, `rider-paid.png`, `driver-active.png` | Giao diện desktop, chuyến hoàn tất và tài xế đang phục vụ |
| `rider-mobile.png`, `driver-mobile.png` | Giao diện 360 px và focus bàn phím |
| `compose-status.jsonl`, `observability-check.json` | Container và truy vấn metrics/logs/traces thật |

Các kiểm tra thủ công bổ sung đã chạy với Python stdlib và CLI database:

- JWT sai định dạng và ID token được Keycloak ký nhưng sai audience đều trả 401.
- Rate limit Gateway trả 429 ở yêu cầu tạo chuyến thứ 11; dùng Driver không có quyền đặt xe để tránh tạo dữ liệu chuyến.
- Fixture MongoDB tách biệt tại (0, 0): chọn tài xế hợp lệ gần nhất; loại vị trí quá 60 giây, offline và ngoài 5 km; fixture được xóa sau kiểm tra.
- Reservation xung đột bị từ chối; release cũ không xóa reservation mới.
- Tài khoản Rider2 thực hiện tuyến dài bằng Wallet; chuyến kế tiếp thiếu tiền vào PaymentReview, không tạo journal hay debit lần hai. Hoàn khoản thu kiểm tra khôi phục số dư ban đầu.
- Quét source với các secret thực tế trong `.env`; không tìm thấy bản sao trong source/config, `.env` có quyền 0600.

Quote hết hạn và các chuyển trạng thái bị cấm được kiểm bằng assert với thời điểm truyền vào; không coi đây là kiểm thử tải hay kiểm thử thiết bị thật. Chưa chạy tải/HA, thiết bị di động thật hoặc cổng tiền/bản đồ thật; đây là demo local với tuyến và tiền mô phỏng đã chọn.

## Kịch bản bảo vệ

1. Trình bày sơ đồ client và ownership ba DB, giải thích vì sao dùng microservices cho học tập.
2. Demo chuyến Wallet thành công và bút toán Nợ/Có.
3. Rider từ chối giá; tài xế từ chối bốn lần; giải thích ranh giới k.
4. Chạy `demo-unknown`, chứng minh timeout không thu lại.
5. Chạy `demo-compensate`, đối chiếu Charge/Refund và PaymentReview.
6. Chạy lỗi broker hoặc dead-letter, chứng minh Outbox và replay.
7. Truy trace, chỉ đúng mã minh họa năm nguyên tắc SOLID và tám pattern.
8. Nêu giới hạn demo, chi phí ba DB và lý do modular monolith có thể phù hợp hơn với sản phẩm nhỏ.
