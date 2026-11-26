# Danh sách API endpoint SwiftRide

Danh sách đối chiếu với route đang khai báo trong mã nguồn. Có 46 endpoint nghiệp vụ tại
Trip, Matching, Payment và 1 endpoint tổng hợp tại Gateway; health, OpenAPI và API của web
liệt kê riêng bên dưới. Mỗi cặp HTTP method + path tính là một endpoint.

## Địa chỉ và quy ước

| Thành phần       | Base URL                | Phạm vi                                    |
| ---------------- | ----------------------- | ------------------------------------------ |
| API Gateway      | `http://localhost:8080` | Gọi từ Postman, script hoặc client API.    |
| Web / Next.js    | `http://localhost:3000` | Đăng nhập web, session và proxy cho Rider. |
| Trip Service     | `http://trip:8080`      | Nội bộ mạng Docker Compose.                |
| Matching Service | `http://matching:8080`  | Nội bộ mạng Docker Compose.                |
| Payment Service  | `http://payment:8080`   | Nội bộ mạng Docker Compose.                |

- Endpoint nghiệp vụ bên dưới dùng path tại service; Gateway chuyển tiếp nguyên path.
  Riêng `/screens/trips/{id}` do Gateway xử lý trực tiếp.
- API backend dùng `Authorization: Bearer <accessToken>`, trừ login và health.
  `Rider`, `Driver`, `Service` tương ứng claim role `rider`, `driver`, `service`.
  `Đăng nhập` nghĩa là route yêu cầu JWT; ghi chú nêu thêm kiểm tra ở tầng nghiệp vụ.
- `Service` là token gọi giữa các service, không phải tài khoản đăng nhập web.
  `POST /auth/login` chỉ cấp token Rider hoặc Driver.
- Request có body dùng JSON, tên trường dạng camelCase. `{id}` của trip/payment là GUID;
  ID Driver là chuỗi. ID payment hiện dùng cùng giá trị với ID trip.
- Endpoint ghi có ghi chú `Key` bắt buộc header `Idempotency-Key`, tối đa 100 ký tự.
  Retry cùng thao tác phải giữ nguyên key và nội dung; tái dùng key với nội dung khác có
  thể trả `409`. Riêng tạo payment có mẫu key cố định, ghi tại mục Payment.
- `V` trong bảng nghĩa là body bắt buộc `expectedVersion`, lấy từ `version` mới nhất của
  trip. Version cũ trả `409`; đọc lại trip trước khi gửi thao tác mới.
- Có thể gửi header `CorrelationId` dạng GUID để nối dấu vết request. Backend tạo mới nếu
  thiếu/không hợp lệ, trả lại trong response. Proxy web tự tạo GUID cho mỗi request.
- Các API history trả `{ items, page, pageSize, total }`, mặc định `page=1`, 10 mục/trang.
  Trip/payment nhận page từ 1–10.000; wallet nhận page từ 1–100.000.
- Tiền tính bằng VND. Ví, COD, nạp và rút tiền đều là mô phỏng nội bộ.

## Trip Service — 17 endpoint

Nguồn: [TripEndpoints.cs](../backend/src/TripService/Api/Endpoints/TripEndpoints.cs).

### Đăng nhập và hồ sơ

| Method | Path           | Quyền        | Ghi chú                                                  |
| ------ | -------------- | ------------ | -------------------------------------------------------- |
| POST   | `/auth/login`  | Công khai    | Body: `username`, `password`; trả JWT và thông tin user. |
| GET    | `/profiles/me` | Đăng nhập    | Hồ sơ tài khoản hiện tại.                                |
| PUT    | `/profiles/me` | Rider/Driver | Body: `name`, `phone`; cập nhật hồ sơ của mình.          |

Login trả `{ accessToken, expiresAt, user: { id, name, role } }`; `expiresAt` là Unix time
tính bằng giây, token có thời hạn 8 giờ. Không nhận role do client tự chọn. Mật khẩu tài
khoản mô phỏng lấy từ `.env`, không ghi vào tài liệu/request chia sẻ.

Phải có họ tên và số điện thoại trong hồ sơ trước khi đặt chuyến.

### Đặt xe, tra cứu và thao tác Rider

| Method | Path                        | Quyền                   | Ghi chú                                           |
| ------ | --------------------------- | ----------------------- | ------------------------------------------------- |
| POST   | `/trips`                    | Rider                   | Key. Tạo chuyến: `201` nếu mới, `200` nếu replay. |
| GET    | `/trips`                    | Đăng nhập               | Tối đa 50 chuyến gần nhất tài khoản được xem.     |
| GET    | `/trips/history`            | Đăng nhập               | Query: `page?`, `status?`; lịch sử của tài khoản. |
| GET    | `/trips/{id}`               | Rider/Driver của chuyến | Chi tiết, quote, version và lịch sử trạng thái.   |
| POST   | `/trips/{id}/confirm-quote` | Rider của chuyến        | V; thêm `quoteId`, quote phải còn hạn.            |
| POST   | `/trips/{id}/cancel`        | Rider/Driver của chuyến | V; thêm `reason?`; phụ thuộc trạng thái.          |
| POST   | `/trips/{id}/review`        | Rider của chuyến        | V; thêm `rating`, `comment?`; sau trả khách.      |

Body tạo chuyến:

```json
{
  "pickup": { "latitude": 10.7769, "longitude": 106.7009, "label": "Điểm đón" },
  "destination": { "latitude": 10.782, "longitude": 106.695, "label": "Điểm đến" },
  "method": "Wallet",
  "pickupNote": "Đợi tại cổng chính"
}
```

- `method` chỉ nhận `Wallet` hoặc `Cod` cho chuyến mới. Bỏ `paymentToken` hoặc gửi `null`;
  `Card` và token thanh toán khác null bị từ chối.
- `pickup`, `destination` dùng latitude từ -90 đến 90, longitude từ -180 đến 180,
  `label` có nội dung tối đa 120 ký tự. Hai điểm phải khác nhau; `pickupNote` tối đa 240 ký tự.
- Tạo chuyến chưa đồng nghĩa đã có Driver. Worker tiếp tục tìm xe, tạo quote và chờ Rider
  xác nhận. Quote của trip phải lấy từ trip hiện tại, không dùng quote xem trước từ `/quote`.
- Xác nhận quote Wallet bắt đầu giữ tiền bất đồng bộ; COD tiếp tục bước giữ Driver.
  Đọc lại trip để biết kết quả, tránh gửi lặp thao tác với version cũ.
- Driver chỉ xem chuyến đã được giao, sau bước giữ Driver. Rider chỉ xem chuyến của mình.
- Hủy phụ thuộc trạng thái chuyến. Driver chỉ hủy khi `DriverAccepted` hoặc `EnRoute`;
  lời mời chưa nhận dùng `driver-reject`. Khi đã nhận chuyến/đang đến đón, phải có lý do
  tối đa 240 ký tự. Wallet đang `WalletHoldPending` phải chờ kết quả giữ tiền trước khi hủy.
- Đánh giá nhận 1–5 sao, nhận xét tối đa 500 ký tự. Chuyến phải có mốc `DroppedOff` và đang
  `PaymentPending`, `PaymentReview` hoặc `Paid`; không đổi sang đánh giá khác sau khi đã lưu.
- `status` trong history dùng tên `TripStatus`: `Requested`, `AwaitingRiderConfirmation`,
  `WalletHoldPending`, `ReservingDriver`, `AwaitingDriverAcceptance`, `AcceptingDriver`,
  `DriverAccepted`, `EnRoute`, `PickedUp`, `DroppedOff`, `PaymentPending`, `PaymentReview`,
  `Paid`, `Cancelled`, `Failed`.

### Nhận chuyến và hành trình Driver

| Method | Path                        | Quyền             | Ghi chú                                       |
| ------ | --------------------------- | ----------------- | --------------------------------------------- |
| GET    | `/drivers/me/offers`        | Driver            | Lời mời đang chờ Driver hiện tại nhận.        |
| POST   | `/trips/{id}/driver-accept` | Driver của chuyến | V; lời mời phải còn hạn.                      |
| POST   | `/trips/{id}/driver-reject` | Driver của chuyến | V; từ chối và tìm Driver khác.                |
| POST   | `/trips/{id}/en-route`      | Driver của chuyến | V; bắt đầu đến điểm đón.                      |
| POST   | `/trips/{id}/pickup`        | Driver của chuyến | V; kiểm tra xe đã tới điểm đón.               |
| POST   | `/trips/{id}/dropoff`       | Driver của chuyến | V; kiểm tra tới điểm trả, bắt đầu thanh toán. |
| POST   | `/trips/{id}/retry-payment` | Driver của chuyến | V; chỉ COD đang `PaymentReview`.              |

Những route chỉ ghi `V` nhận body như `{ "expectedVersion": 12 }`. `driver-accept` bắt đầu
bước `AcceptingDriver`; worker xác nhận reservation rồi mới thành `DriverAccepted`.
Các route hành trình phải gọi đúng trạng thái, cập nhật version sau mỗi bước.

## Matching Service — 16 endpoint

Nguồn: [MatchingEndpoints.cs](../backend/src/MatchingService/Api/Endpoints/MatchingEndpoints.cs),
[DriverSimulationEndpoints.cs](../backend/src/MatchingService/Api/Endpoints/DriverSimulationEndpoints.cs).

### Giá và bản đồ

| Method | Path            | Quyền     | Ghi chú                                                        |
| ------ | --------------- | --------- | -------------------------------------------------------------- |
| POST   | `/quote`        | Đăng nhập | Body: `pickup`, `destination`; giá xem trước, hạn 120 giây.    |
| POST   | `/maps/route`   | Đăng nhập | Body: `pickup`, `destination`; tuyến đường, km và thời gian.   |
| GET    | `/maps/search`  | Đăng nhập | Query bắt buộc `text`; địa chỉ từ 3–120 ký tự sau trim.        |
| GET    | `/maps/reverse` | Đăng nhập | Query bắt buộc `latitude`, `longitude`; tra địa chỉ từ tọa độ. |

`pickup` và `destination` cùng cấu trúc `Point` như body tạo chuyến. Quote xem trước không
giữ Driver hoặc giữ tiền. Geoapify là dịch vụ bản đồ bên ngoài; có thể lỗi nếu thiếu key
hoặc provider không sẵn sàng. Tọa độ tuyến đường dùng thứ tự `[longitude, latitude]`.

### Trạng thái Driver

| Method | Path                       | Quyền  | Ghi chú                                                  |
| ------ | -------------------------- | ------ | -------------------------------------------------------- |
| GET    | `/drivers/me`              | Driver | Thông tin, vị trí, available, busy, thời điểm hoạt động. |
| PUT    | `/drivers/me/location`     | Driver | Body: `latitude`, `longitude`; trả `204`.                |
| PUT    | `/drivers/me/availability` | Driver | Body: `available` kiểu boolean; trả `204`.               |
| PUT    | `/drivers/me/heartbeat`    | Driver | Không cần body; cập nhật hiện diện, trả `204`.           |

Cập nhật vị trí, availability và heartbeat yêu cầu Driver đã được duyệt/chưa bị khóa.
`/drivers/me/offers` thuộc Trip Service, Gateway ưu tiên route đó trước `/drivers/*`.

### Mô phỏng di chuyển

| Method | Path                      | Quyền                 | Ghi chú                                          |
| ------ | ------------------------- | --------------------- | ------------------------------------------------ |
| GET    | `/simulations/{driverId}` | Driver sở hữu/Service | Trả mô phỏng, hoặc JSON `null` nếu chưa có.      |
| PUT    | `/simulations/{driverId}` | Driver sở hữu/Service | Body: `speed`, `paused`; chỉnh mô phỏng hiện có. |

`speed` chỉ nhận `1`, `5`, `10`, `20`; `paused` là boolean. Route yêu cầu đăng nhập, sau đó
nghiệp vụ kiểm tra Driver sở hữu hoặc Service. Rider không gọi trực tiếp; web nhận mô phỏng
qua endpoint tổng hợp `/screens/trips/{id}`.

### API nội bộ tìm xe và giữ Driver

| Method | Path                    | Quyền   | Ghi chú                                                            |
| ------ | ----------------------- | ------- | ------------------------------------------------------------------ |
| POST   | `/match`                | Service | Body: `pickup`, `excludedDrivers`; tìm Driver gần phù hợp.         |
| POST   | `/reservations`         | Service | Body reservation; thử giữ Driver.                                  |
| POST   | `/reservations/confirm` | Service | Body reservation; xác nhận giữ Driver.                             |
| POST   | `/reservations/release` | Service | Body reservation; giải phóng, trả `204`.                           |
| POST   | `/arrivals`             | Service | Body: `driverId`, `tripId`, `target`; kiểm tra đến nơi, trả `204`. |
| GET    | `/drivers/{id}/contact` | Service | Thông tin liên hệ Driver cho màn hình tổng hợp.                    |

Body reservation gồm `tripId`, `reservationId` dạng GUID và `driverId` dạng chuỗi.
Reserve/confirm trả `{ acquired, expiresAt }`; `acquired=false` nghĩa là chưa giữ được xe,
không nên suy luận chỉ từ HTTP `200`. Release dùng `tripId`, `reservationId` để giải phóng.
`/match` trả `{ driver }`, trong đó `driver` có thể là `null`.

`target` của `/arrivals` chỉ nhận `PickedUp` hoặc `DroppedOff`; đây là kiểm tra trước thao
tác pickup/dropoff, không phải API tự chuyển trạng thái trip. Gateway vẫn định tuyến các
API nội bộ này, nhưng yêu cầu role `service`.

## Payment Service — 13 endpoint

Nguồn: [PaymentEndpoints.cs](../backend/src/PaymentService/Api/Endpoints/PaymentEndpoints.cs),
[WalletEndpoints.cs](../backend/src/PaymentService/Api/Endpoints/WalletEndpoints.cs).

### Thanh toán và sổ cái

| Method | Path                          | Quyền                 | Ghi chú                                    |
| ------ | ----------------------------- | --------------------- | ------------------------------------------ |
| GET    | `/payments/history`           | Rider/Driver          | Query: `page?`; giao dịch của tài khoản.   |
| GET    | `/payments/statistics`        | Rider/Driver          | Thống kê của tài khoản.                    |
| POST   | `/payments`                   | Service               | Key; tạo/xử lý payment nội bộ.             |
| GET    | `/payments/{id}`              | Chủ giao dịch/Service | Rider sở hữu hoặc Driver được giao chuyến. |
| GET    | `/payments/{id}/ledger`       | Chủ giao dịch/Service | Các journal và dòng ghi sổ của payment.    |
| POST   | `/payments/{id}/confirm-cash` | Driver của chuyến     | Key; body: `received: true`, `reason?`.    |
| POST   | `/payments/{id}/refund`       | Driver của chuyến     | Key; body: `reason`, `cashReturned: true`. |

- `confirm-cash` xác nhận đã thu COD; lý do tối đa 200 ký tự. Rider không được xác nhận hộ.
- `refund` chỉ hoàn COD đã `Settled`, phải có lý do tối đa 200 ký tự và xác nhận đã trả tiền.
  Không hỗ trợ hoàn payment Wallet đã thanh toán qua route này.
- Thu/hoàn COD là ghi nhận mô phỏng; không tích hợp cổng thanh toán hay ngân hàng.
- `/payments/{id}` có thể trả `404` khi sự kiện đang chờ xử lý, dù trip đã `PaymentPending`.
- Giữ/trả tiền Wallet của trip đi qua RabbitMQ, không có HTTP endpoint giữ/trả ví riêng.

`POST /payments` dành cho service, không dùng để Rider tự báo giá hoặc tự thanh toán:

| Trường/header                   | Nội dung                                            |
| ------------------------------- | --------------------------------------------------- |
| `Idempotency-Key`               | Bắt buộc đúng `trip:{tripId}:charge`.               |
| `tripId`, `riderId`, `driverId` | Chuyến và hai bên liên quan.                        |
| `amount`, `method`              | Số tiền VND nguyên; `Wallet` hoặc `Cod`.            |
| `correlationId`                 | Handler gán từ header `CorrelationId`.              |
| `attempt`                       | Mặc định `0`; dùng khi retry COD.                   |
| `walletHoldId`                  | ID khoản giữ ví khi xử lý Wallet.                   |
| `paymentToken`                  | Chỉ để tương thích hợp đồng cũ; bỏ hoặc gửi `null`. |

### Ví, nạp và rút tiền

| Method | Path                   | Quyền        | Ghi chú                                                   |
| ------ | ---------------------- | ------------ | --------------------------------------------------------- |
| GET    | `/wallets/me`          | Rider/Driver | Số dư available, held, pendingEarnings và quyền thao tác. |
| GET    | `/wallets/history`     | Rider/Driver | Query: `page?`; biến động ví của mình.                    |
| GET    | `/wallets/topups`      | Rider/Driver | Query: `page?`; lịch sử nạp ví của mình.                  |
| POST   | `/wallets/topups`      | Rider        | Key; body: `amount`; nạp ví mô phỏng.                     |
| GET    | `/wallets/withdrawals` | Rider/Driver | Query: `page?`; lịch sử và trạng thái rút tiền.           |
| POST   | `/wallets/withdrawals` | Rider/Driver | Key; body: `amount`; giữ tiền chờ worker xử lý.           |

Nạp/rút nhận số nguyên từ 10.000 đến 5.000.000 VND. Driver không nạp ví qua API này.
Service không được thao tác ví cá nhân. Tất cả route ví chỉ truy cập ví của tài khoản hiện tại.

Rút tiền trả yêu cầu `Pending`; worker chuyển sang `Completed` hoặc `Rejected` theo cấu hình
mô phỏng `WALLET_WITHDRAWAL_OUTCOME`. Đọc lại lịch sử để biết kết quả; không có endpoint duyệt
rút tiền thủ công hay chuyển khoản thật. Xem thêm [nạp ví](wallet-topup.md),
[rút ví](wallet-withdrawals.md).

## API Gateway — tổng hợp và chuyển tiếp

Nguồn: [ScreenEndpoints.cs](../backend/src/ApiGateway/Endpoints/ScreenEndpoints.cs),
[ServiceRegistration.cs](../backend/src/ApiGateway/Composition/ServiceRegistration.cs).

| Method | Path                  | Quyền                   | Ghi chú                                     |
| ------ | --------------------- | ----------------------- | ------------------------------------------- |
| GET    | `/screens/trips/{id}` | Rider/Driver của chuyến | Tổng hợp trip, payment, simulation, driver. |

Response gồm `{ trip, payment, simulation, driver, paymentUnavailable }`. Các phần bổ sung
có thể `null`; thông tin Driver chỉ hiển thị ở các trạng thái được phép. Nếu Payment lỗi
tạm thời, màn hình vẫn có thể trả trip với `paymentUnavailable=true`.

| Path/prefix                                                             | Đích chuyển tiếp | Ghi chú                               |
| ----------------------------------------------------------------------- | ---------------- | ------------------------------------- |
| `POST /auth/login`                                                      | Trip             | Công khai.                            |
| `/drivers/me/offers`                                                    | Trip             | Ưu tiên trước nhóm `/drivers/*`.      |
| `/trips`, `/profiles` và path con                                       | Trip             | JWT; kiểm tra quyền tiếp tại service. |
| `/drivers`, `/quote`, `/maps`, `/simulations` và path con được cấu hình | Matching         | JWT; theo quyền từng endpoint.        |
| `/match`, `/reservations`, `/arrivals` và path con                      | Matching         | Bắt buộc role `service` tại Gateway.  |
| `/payments`, `/wallets` và path con                                     | Payment          | JWT; theo quyền từng endpoint.        |

Prefix định tuyến không tạo thêm endpoint nghiệp vụ; path/method không được service khai
báo vẫn không gọi được. Riêng `/quote` được cấu hình khớp path chính xác.

Gateway giới hạn theo tài khoản/IP mỗi phút: 10 request đặt chuyến `POST /trips`, 360 request
GET và 60 request ghi khác, không có hàng đợi. Vượt giới hạn trả `429`.

## Health và OpenAPI — mọi backend service

Nguồn: [ApiPipeline.cs](../backend/src/Shared/SwiftRide.Infrastructure/Http/ApiPipeline.cs).
Ba route dùng chung sau được đăng ký tại Gateway, Trip, Matching và Payment:

| Method | Path               | Quyền     | Ghi chú                                                 |
| ------ | ------------------ | --------- | ------------------------------------------------------- |
| GET    | `/health/live`     | Công khai | `200`, `{ "status": "live" }`; kiểm tra tiến trình.     |
| GET    | `/health/ready`    | Công khai | `200` nếu sẵn sàng; `503` nếu dependency chưa sẵn sàng. |
| GET    | `/openapi/v1.json` | Đăng nhập | OpenAPI của service đang gọi.                           |

Từ máy host, `http://localhost:8080/health/*` kiểm tra Gateway. Không có route Gateway kiểu
`/trip/health/*`; health của từng service gọi tại base URL nội bộ trong mạng Compose.
`/openapi/v1.json` tại Gateway mô tả endpoint trực tiếp của Gateway; xem ba service qua:

| Method | Path tại Gateway         | Quyền     | Đích nội bộ                            |
| ------ | ------------------------ | --------- | -------------------------------------- |
| GET    | `/openapi/trip.json`     | Đăng nhập | `http://trip:8080/openapi/v1.json`     |
| GET    | `/openapi/matching.json` | Đăng nhập | `http://matching:8080/openapi/v1.json` |
| GET    | `/openapi/payment.json`  | Đăng nhập | `http://payment:8080/openapi/v1.json`  |

Không có Swagger UI được đăng ký. Metrics/logs/traces dùng OpenTelemetry export, không có
route `/metrics` trong API ứng dụng hiện tại.

## Web / Next.js — health, đăng nhập và proxy

Base URL: `http://localhost:3000`. Những route này dùng session cookie của web, không phải
Bearer token do browser tự quản lý.

### Health và proxy backend

Nguồn: [health/route.ts](../frontend/src/app/api/health/route.ts),
[backend/route.ts](../frontend/src/app/api/backend/[...path]/route.ts),
[backend-proxy.ts](../frontend/src/infrastructure/http/backend-proxy.ts).

| Method | Path                     | Quyền         | Ghi chú                                         |
| ------ | ------------------------ | ------------- | ----------------------------------------------- |
| GET    | `/api/health`            | Công khai     | `{ "status": "live" }`; không kiểm tra backend. |
| GET    | `/api/backend/{...path}` | Session Rider | Chuyển tiếp request đọc và query tới Gateway.   |
| POST   | `/api/backend/{...path}` | Session Rider | Chuyển tiếp JSON; kiểm tra Origin.              |
| PUT    | `/api/backend/{...path}` | Session Rider | Chuyển tiếp JSON; kiểm tra Origin.              |

- Chỉ chấp nhận prefix `trips`, `payments`, `wallets`, `quote`, `screens`, `maps`, `profiles`.
  Ví dụ: `GET /api/backend/screens/trips/{id}` hoặc `POST /api/backend/trips`.
- Không proxy `drivers`, `simulations`, `match`, `reservations`, `arrivals`, `auth`, `health`
  hoặc `openapi`; Driver dùng Gateway trực tiếp qua Postman/script.
- Segment path chỉ có chữ ASCII, số hoặc dấu `-`. Body tối đa 16.384 byte.
- POST/PUT yêu cầu header `Origin` trùng origin của `AUTH_URL`.
- Proxy đọc JWT từ cookie HttpOnly, thêm Bearer token, tạo `CorrelationId`, chuyển tiếp
  `Idempotency-Key`; không trả access token cho client.
- Quyền Rider tại proxy không bỏ qua quyền nghiệp vụ: thao tác chỉ dành cho Driver/Service
  vẫn bị backend từ chối, dù prefix nằm trong danh sách cho phép.
- Lỗi proxy: `400` path sai, `401` phiên hết hạn, `403` sai Origin/role, `413` body quá lớn,
  `503` không kết nối được Gateway. Route chỉ export GET, POST, PUT; Next.js tự xử lý
  HEAD/OPTIONS theo quy ước framework, không có handler nghiệp vụ riêng.

### Auth.js / NextAuth

Nguồn: [auth/route.ts](../frontend/src/app/api/auth/[...nextauth]/route.ts),
[auth.ts](../frontend/src/infrastructure/auth/auth.ts).

Route khai báo là `GET` và `POST /api/auth/{...nextauth}`. Auth.js phân phối action bên
dưới; provider duy nhất đang cấu hình là `credentials`, chỉ cho Rider đăng nhập web.

| Method | Path                             | Ghi chú                                                     |
| ------ | -------------------------------- | ----------------------------------------------------------- |
| GET    | `/api/auth/providers`            | Danh sách provider đang cấu hình.                           |
| GET    | `/api/auth/csrf`                 | Lấy CSRF token và cookie dùng cho thao tác Auth.js.         |
| GET    | `/api/auth/session`              | Session hiện tại; chưa đăng nhập trả `null`.                |
| POST   | `/api/auth/session`              | Action cập nhật/đọc lại session của Auth.js; yêu cầu CSRF.  |
| GET    | `/api/auth/signin`               | Trang đăng nhập mặc định của Auth.js.                       |
| POST   | `/api/auth/signin/credentials`   | Action khởi tạo flow; xác thực ở callback bên dưới.         |
| POST   | `/api/auth/callback/credentials` | Nhận `username`, `password`, `csrfToken`; tạo cookie phiên. |
| GET    | `/api/auth/signout`              | Trang xác nhận đăng xuất của Auth.js.                       |
| POST   | `/api/auth/signout`              | Đăng xuất, xóa cookie phiên; yêu cầu CSRF.                  |
| GET    | `/api/auth/error`                | Trang lỗi đăng nhập do Auth.js xử lý.                       |
| GET    | `/api/auth/verify-request`       | Trang mặc định của thư viện; không có flow email đang bật.  |

POST đăng nhập trực tiếp vào Auth.js cần CSRF token khớp cookie; thường dùng form hoặc
helper Auth.js sẵn có. Callback gọi server tới `POST /auth/login` tại Gateway rồi lưu token
trong phiên HttpOnly; `/api/auth/session` không đưa backend access token ra browser.
Phiên có thời hạn tối đa 8 giờ và bị loại khi backend token hết hạn.

`POST /api/auth/signin` không kèm provider cũng do thư viện tiếp nhận và quay về trang
đăng nhập. OAuth callback GET, email và WebAuthn không có provider hoạt động trong dự án;
không dùng các action đó như chức năng đăng nhập được hỗ trợ. Những action không hợp lệ
đi qua catch-all có thể trả lỗi hoặc redirect của Auth.js, không phải API nghiệp vụ mới.

## Mã lỗi và lưu ý khi thử API

| HTTP status | Trường hợp                                                       |
| ----------- | ---------------------------------------------------------------- |
| 400         | Dữ liệu/JSON/query sai hoặc vi phạm điều kiện đầu vào.           |
| 401         | Thiếu, sai, hết hạn token/session hoặc sai thông tin đăng nhập.  |
| 403         | Sai role, sai chủ sở hữu hoặc Origin không hợp lệ ở proxy web.   |
| 404         | Không có bản ghi/path; payment có thể chưa được tạo bất đồng bộ. |
| 405         | Method không được route hỗ trợ.                                  |
| 409         | Version cũ, key xung đột, sai trạng thái hoặc xung đột dữ liệu.  |
| 413         | Body vượt giới hạn tại proxy web.                                |
| 429         | Vượt giới hạn request của Gateway.                               |
| 500         | Lỗi chưa xử lý; dùng CorrelationId tra cứu.                      |
| 503         | Service/dependency chưa sẵn sàng hoặc request quá hạn.           |

Lỗi nghiệp vụ backend qua pipeline thường trả Problem Details với `detail`, `status`,
`correlationId`; lỗi xác thực/framework và proxy có thể có body khác. Dựa vào cả HTTP status
và body, không suy luận trạng thái thanh toán chỉ từ HTTP `200`.

Danh sách bao gồm endpoint ứng dụng tự đăng ký và action Auth.js liên quan; không liệt kê
trang web, static asset, Next.js Server Actions hoặc API của phần mềm bên ngoài như Geoapify,
RabbitMQ Management, Grafana. Event RabbitMQ không phải HTTP endpoint. Không có API đăng ký
công khai, refresh token, duyệt Ops hay webhook cổng thanh toán đang hoạt động.

Thử luồng Rider/Driver theo [Postman guide](postman-guide.md). Thao tác ghi tạo dữ liệu mô
phỏng được lưu lại; chỉ đọc tài liệu/OpenAPI không tạo chuyến hay thay đổi số dư.
