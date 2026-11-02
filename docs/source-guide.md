# Bản đồ mã nguồn

## Đi từ yêu cầu đến file

Đường dẫn backend trong bảng bắt đầu từ `backend/src/`.

| Việc cần đổi                                     | Bắt đầu tại                                                                                                        |
| ------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------ |
| Endpoint, payload HTTP, policy User/Driver/Ops   | `<Service>/Api/Endpoints/`                                                                                         |
| Luật chuyển trạng thái chuyến                    | `TripService/Domain/Trip.cs`, `TripState.cs`                                                                       |
| Đặt, hủy, xác nhận chuyến                        | `TripService/Application/Commands/TripCommands.cs`                                                                 |
| Quyền tài nguyên chuyến                          | `TripService/Application/Security/TripAccess.cs`                                                                   |
| Danh sách/chi tiết chuyến                        | `TripService/Application/Queries/`; DTO tại `ReadModels/`                                                          |
| Ghép xe, retry sau restart                       | `TripService/Application/Workflows/TripWorkflow.cs`                                                                |
| SQL Server, index, optimistic concurrency        | `TripService/Infrastructure/Persistence/`                                                                          |
| Trip gọi Matching qua HTTP                       | `TripService/Infrastructure/Clients/MatchingHttpClient.cs`                                                         |
| Tính giá và decorator                            | `MatchingService/Domain/`; use case tại `Application/Pricing/`                                                     |
| Bán kính/vị trí/giữ chỗ còn hiệu lực             | `MatchingService/Domain/MatchingPolicy.cs`                                                                         |
| Tìm tài xế, vị trí, reservation                  | `MatchingService/Application/{Matching,Drivers,Reservations}/`                                                     |
| GeoJSON, Mongo query, giữ chỗ nguyên tử          | `MatchingService/Infrastructure/Persistence/`                                                                      |
| Tìm địa chỉ, định vị ngược, xem đường đi         | `MatchingService/Application/Maps/MapQueries.cs`, `Infrastructure/Maps/GeoapifyMapProvider.cs`                     |
| Tạo, duyệt, từ chối, khóa Driver                 | `MatchingService/Application/Drivers/DriverAdministration.cs`, `Infrastructure/Identity/KeycloakDriverIdentity.cs` |
| Xe di chuyển, tạm dừng, tốc độ, kiểm tra đến nơi | `MatchingService/Domain/Simulation.cs`, `Application/Simulation/SimulationWorkflow.cs`                             |
| Lưu hành trình giả lập và revision               | `MatchingService/Infrastructure/Persistence/MongoSimulationStore.cs`                                               |
| Hồ sơ User và lịch sử có phân trang              | `TripService/Application/{Commands/ProfileCommands.cs,Queries/TripQueries.cs}`, `Infrastructure/Persistence/`      |
| Số dư, trạng thái thanh toán, cân bằng Nợ/Có     | `PaymentService/Domain/`                                                                                           |
| Thu tiền, hoàn tiền, idempotency                 | `PaymentService/Application/Commands/PaymentCommands.cs`                                                           |
| Card/Wallet và registry lựa chọn                 | `PaymentService/Application/Methods/`                                                                              |
| Cổng thẻ demo, giả lập timeout                   | `PaymentService/Infrastructure/Providers/`                                                                         |
| PostgreSQL, row/advisory lock, trigger sổ cái    | `PaymentService/Infrastructure/Persistence/`                                                                       |
| Sự kiện giữa các service                         | `Shared/SwiftRide.Contracts/Events/`                                                                               |
| Consumer RabbitMQ                                | `<Service>/Infrastructure/Messaging/`                                                                              |
| Đăng ký DI và cấu hình adapter                   | `<Service>/Infrastructure/Composition/ServiceRegistration.cs`                                                      |
| Khởi động host                                   | `<Service>/Api/Program.cs`                                                                                         |
| JWT, chuyển HttpContext thành Actor              | `Shared/SwiftRide.Infrastructure/Security/`                                                                        |
| Inbox/Outbox, retry broker, telemetry            | `Shared/SwiftRide.Infrastructure/{Messaging,Persistence,Resilience,Observability}/`                                |
| Gateway routes, rate limit, upstream             | `ApiGateway/Composition/ServiceRegistration.cs`                                                                    |
| Gom chuyến, vị trí xe và dữ liệu thanh toán cũ   | `ApiGateway/Endpoints/ScreenEndpoints.cs`                                                                          |
| Mô hình và thao tác chuyến trên web              | `frontend/src/features/rides/domain/`                                                                              |
| Retry đặt xe, điều phối thao tác web             | `frontend/src/features/rides/application/RideService.ts`                                                           |
| HTTP web gọi backend proxy                       | `frontend/src/features/rides/infrastructure/HttpRideGateway.ts`                                                    |
| React state, polling/heartbeat                   | `frontend/src/features/rides/presentation/useRides.ts`                                                             |
| Form, chi tiết chuyến, lịch sử, bản đồ           | `frontend/src/features/rides/presentation/components/`                                                             |
| Ghép service và adapter client                   | `frontend/src/composition/RideClient.tsx`                                                                          |
| OIDC, session HttpOnly, refresh token            | `frontend/src/infrastructure/auth/`                                                                                |
| Origin/path/body và chuyển tiếp Gateway          | `frontend/src/infrastructure/http/backend-proxy.ts`                                                                |
| Đăng ký User và đồng bộ quyền, giữ danh tính cũ  | `deploy/keycloak/swiftride-realm.json`, `scripts/sync-identity.py`                                                 |
| Bản đồ web, chọn địa chỉ và điều khiển xe        | `frontend/src/features/rides/presentation/components/{RouteMap,AddressField,SimulationControls}.tsx`               |
| Giao diện quản trị Driver                        | `frontend/src/features/rides/presentation/components/OpsPanel.tsx`                                                 |
| Giới hạn RAM và kiểm thử ba vai trò              | `scripts/check-resources.py`, `scripts/browser-check.mjs`, `scripts/verify.sh`                                     |
| Bảo vệ ranh giới tầng                            | `scripts/check-architecture.py`                                                                                    |
| Kiểm thử nghiệp vụ/hợp đồng adapter              | `backend/tests/SwiftRide.Checks/`; `scripts/check-frontend.mjs`                                                    |

## Quy tắc đặt file

`Domain` chứa luật nghiệp vụ và trạng thái; `Application` chứa use case và port; `Infrastructure` chứa adapter công nghệ; `Api` chứa HTTP và điểm khởi động. Port được đặt tên theo nhu cầu use case, không theo thư viện. Consumer/worker chuyển message, gọi use case và quản lý vòng đời hạ tầng.

SharedKernel giữ kiểu dùng chung: tọa độ, hình học, dữ liệu giá, phương thức thanh toán và lỗi nghiệp vụ. Contracts chứa DTO/sự kiện. Shared Application giữ Actor và port chung, không tham chiếu service Domain. Entity Trip/Payment thuộc Domain của từng service.

Gateway là edge adapter, dùng `Endpoints`, `Composition`, `Infrastructure`. Next.js `app/` giữ routing/server actions và composition; mã tính năng nằm trong `features/`.

## Quy tắc format

- Độ rộng mục tiêu: 100 ký tự, tính cả thụt lề.
- Tham số, đối số, mảng và object trong `()`, `[]`, `{}` gom thành một dòng khi cả dòng còn đủ chỗ. Thuộc tính JSX ngắn áp dụng cùng quy tắc.
- Khi vượt độ rộng, xuống dòng và thụt lề theo cấu trúc ngoặc. Không giữ nhóm ngắn trên nhiều dòng chỉ vì trước đó đã có dấu phẩy cuối.
- Mỗi câu lệnh riêng một dòng; khối lệnh vẫn xuống dòng. Giữ nguyên nội dung chuỗi, SQL, template và chú thích cần thiết.

Ví dụ nhóm ngắn:

```ts
const options = { busy, loading };
await service.act(trip, action, role);
```

Quy tắc được cấu hình tại `.prettierrc.json` cho frontend/JavaScript, `backend/.csharpierrc.json` và `backend/.editorconfig` cho C#, `pyproject.toml` cho Python. Dùng Prettier 3.5 trở lên để hỗ trợ `objectWrap: "collapse"`.

## Khi thêm chức năng

1. Đặt luật bất biến vào Domain; giữ EF, HTTP, MongoDB và broker ở tầng ngoài.
2. Viết use case tại Application. Tận dụng port hiện có; thêm port khi có giao tiếp ra ngoài cần đảo phụ thuộc.
3. Viết adapter tại Infrastructure, đăng ký composition và thêm endpoint mỏng nếu cần HTTP.
4. Thêm assert có ý nghĩa vào runner hiện có; chạy kiểm tra kiến trúc, nghiệp vụ và tích hợp liên quan.

Thêm phương thức thanh toán bằng implementation `IPaymentMethod` và đăng ký DI; factory nhận danh sách implementation, không có switch. Thêm quy tắc giá qua `IPricingStrategy`/decorator và kiểm tra cùng hợp đồng đầu vào/đầu ra.

## Tương thích dữ liệu

Event nằm trong Contracts nhưng giữ namespace `SwiftRide.Core`: đây là tên wire đã triển khai trong MassTransit, không phải project Core cũ. Đổi namespace sẽ đổi message URN/exchange. Migration giữ ID `202611020001_Initial`, tên bảng/collection và tên consumer queue. Refactor không yêu cầu xóa volume hoặc tạo lại database.
