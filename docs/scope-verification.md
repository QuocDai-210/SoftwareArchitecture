# Đối chiếu phạm vi và bằng chứng kiểm thử

Nguồn đối chiếu: [đề bài PDF](../RideLite-KienTrucPhanMem.pdf) và
[phạm vi mô phỏng cập nhật](../chuc-nang-va-luong-hoat-dong.md).
Ngày cập nhật kiểm tra: 26/11/2026. Không dùng trạng thái container `Healthy` thay cho bằng chứng nghiệp vụ.

## Chạy qua Postman và terminal ngày 26/11/2026

- Chạy folder 7 bằng Newman qua Gateway trên bộ volume riêng `swiftride-postman`. Wallet
  đạt 81 request và 106 assertion. COD đạt 84 request và 107 assertion. Cả hai chuyến tới
  `Paid` và có sổ cái cân bằng. Gửi lại yêu cầu hoàn COD cùng key chỉ tạo một journal hoàn tiền.
- Mỗi phản hồi trả đúng `CorrelationId` của request. Khi đọc sổ cái, log Gateway và Payment
  có cùng ID. Luồng API dùng tuyến đường ước lượng, không cần web hoặc Grafana.
- Bốn lần từ chối kết thúc `Failed` và trả hết tiền giữ. Khi hai Rider tranh cùng Driver,
  chỉ một reservation thành công. Điểm đón không có Driver gần kết thúc `Failed`.
- 38 test unit/model và 2 test tích hợp đạt. Ca OIDC dùng khóa RSA và metadata kiểm thử.
  PostgreSQL và MongoDB chạy thật trong hai test tích hợp.

## Cách áp dụng hai tài liệu

- PDF mục 2.4, 3 và 5: giữ đúng ba service nghiệp vụ Trip, Matching/Pricing, Payment;
  Gateway là điểm vào. Không thêm service ngân hàng, ví hay xác thực riêng.
- PDF mục 3.3 cho phép cùng công nghệ CSDL nhưng tách database/schema theo service.
  TripDB và PayDB là hai database/tài khoản riêng trong PostgreSQL; Matching dùng MongoDB.
  Đây là lựa chọn được đề bài cho phép, không phải bỏ ranh giới dữ liệu.
- PDF mục 8 và 9.3: cấu hình `AUTH_AUTHORITY` để Gateway và service xác thực access token
  từ nhà cung cấp OIDC. Với Postman runner mặc định, tài khoản seed nhận JWT cục bộ.
- File cập nhật quy định Rider-only web, Driver qua API/worker, không GPS, không đăng ký,
  không VNPAY/Ops/ngân hàng. Không khôi phục các tính năng này để mở rộng ngoài phạm vi.
- PDF cho phép luồng cơ bản và xem broker/Outbox/observability là mở rộng; phạm vi cập nhật
  vẫn giữ broker và Outbox. Observability chạy theo profile, được bật khi chạy `make demo`.

## Bản đồ yêu cầu đến mã nguồn

| Yêu cầu                           | Vị trí triển khai chính                                                                | Cách kiểm chứng                              |
| --------------------------------- | -------------------------------------------------------------------------------------- | -------------------------------------------- |
| State và workflow chuyến          | `TripService/Domain/Trip.cs`, `Application/Workflows/TripWorkflow.cs`                  | Unit test, API/Postman                       |
| Strategy tính giá                 | `MatchingService/Domain/{FareCalculator,StandardPricing,SurgePricing,PromoPricing}.cs` | Core tests, quote API                        |
| Repository và DIP/ISP             | Ports trong Application, repository trong Infrastructure                               | Architecture check, DI, unit test            |
| Factory phương thức thanh toán    | `PaymentService/Application/Methods/PaymentMethodFactory.cs`                           | COD/ví, payment tests                        |
| Pub/Sub, Outbox, retry            | `Shared/SwiftRide.Infrastructure/Messaging`, các consumer từng service                 | Workflow tests, kiểm thử sự cố riêng         |
| Seed cục bộ, đúng hai role        | `TripAccountSeeder`, `DriverDatabaseInitializer`, `PaymentDatabaseInitializer`         | DB/API, startup lặp không reset dữ liệu      |
| Giữ ví trước lời mời              | `WalletTripWorkflow`, `WalletAccounts`, `TripPaymentNotifications`                     | Ca thiếu tiền, hủy, tìm lại tài xế           |
| Quyết toán và ledger              | `PaymentCommands`, `WalletMethod`, `CashCollection`                                    | Luồng trọn vẹn và đối chiếu số dư/ledger     |
| Nạp/rút giả lập                   | `WalletFunding`, `WalletWithdrawals`, `WalletWithdrawalSimulationWorker`               | Script concurrency và Postman                |
| Điều khiển hành trình             | `SimulationWorkflow`, `Simulation`, `SimulationWorker`                                 | Đợi đến điểm đón/trả, tốc độ mô phỏng        |
| Không gọi dịch vụ ngoài ngoài map | Cấu hình Compose, local auth, payment nội bộ                                           | Kiểm tra mã nguồn/cấu hình; map vẫn cần mạng |

Các đường dẫn service trong bảng tính từ `backend/src/`.

## Bằng chứng đã chạy

- Build các service thay đổi chạy tuần tự; architecture check và kiểm tra cấu hình RAM đạt.
  Giới hạn khai báo Compose là 6,58/12 GB, gồm cả profile tùy chọn.
- Bộ cuối có 37 unit/model tests và 2 integration tests, tất cả đạt, không bỏ qua test.
- DB xác nhận 100 tài khoản Rider, 1.000 tài khoản Driver và 100 hồ sơ Rider.
- API Wallet: giữ tiền trước lời mời, Driver nhận, mô phỏng đến đón/trả, Trip `Paid`,
  số dư Rider/Driver đúng. Sau sửa, ca tìm lại tài xế đã xác minh thêm quyền Driver đọc giao dịch,
  ledger cân bằng và Driver được giải phóng sau thanh toán.
- API COD: `Paid`, ví Driver không tăng; gửi xác nhận thu lặp không thu lần hai.
- Hoàn COD lặp: một journal hoàn, các journal cân bằng.
- COD lỗi có chủ đích: `Declined` → `PaymentReview`; khôi phục fixture, Driver retry,
  xác nhận thu → `Paid`.
- Thiếu tiền: không có lời mời, không giữ tiền, hủy được chuyến; fixture Rider096 để lại số dư 0.
- Bốn request nạp cùng key chạy đồng thời chỉ cộng một lần; bốn request rút cùng key tạo
  một yêu cầu. Đổi body cùng key bị từ chối. Tài khoản kiểm thử không còn khoản giữ sau xử lý.
  Bốn khoản nạp khác key chạy đồng thời cũng cộng đủ, không mất cập nhật.
- Sai mật khẩu/token, Rider gọi Driver/internal API, Driver nạp ví: bị từ chối đúng quyền.
- Worker tự nhận lời mời đã được kiểm tra; cấu hình được trả về chế độ Postman thủ công.
- Từ chối Driver → trả khoản giữ cũ → báo giá/Driver mới → giữ lại → hoàn tất đã chạy đạt.
  Index journal cho phép nhiều chu kỳ giữ/trả; thu tiền, quyết toán, doanh thu và hoàn tiền
  vẫn giữ ràng buộc chống trùng. Khoản giữ được xác định theo từng báo giá.
- Folder Postman runner bản có kiểm tra ledger đã chạy 60 request, 24 assertion, không lỗi.
- Dừng RabbitMQ trước xác nhận ví: chuyến chờ giữ tiền; khôi phục broker thì tiếp tục gửi lời mời.
  Hủy sau phục hồi trả đủ số dư. Lệnh xác nhận đã được sửa để sự kiện giữ tiền và trạng thái
  cùng được lưu trong một lần SaveChanges, có regression test cho ranh giới lưu Outbox.
- Restart Matching lúc chặng đến đón đang tạm dừng: giữ nguyên tuyến, chuyến, tiến độ;
  tiếp tục mô phỏng đến `Paid`, ledger và số dư Rider/Driver đúng.
- Từ chối bốn lần qua API: ba lần đầu lấy Driver/quote mới; lần thứ tư `Failed`, trả hết khoản giữ.
- Hai Rider xác nhận đồng thời cùng Driver: chỉ một lời mời có reservation, chuyến còn lại
  lấy Driver/quote khác. Fixture không có Driver gần kết thúc `Failed`, không có quote/lời mời.
- Luồng COD kiểm tra thêm version cũ, Rider khác đọc chuyến, Rider nhận thay Driver và
  Driver đánh giá thay Rider đều bị từ chối; đánh giá lặp không tăng version, sai sao bị chặn.
- Smoke test web: Rider đăng nhập, map, xem chuyến và ví; cookie HttpOnly, session không lộ
  access token; Driver bị chặn đăng nhập web.

## Kiểm tra lại sau rà soát ngày 15/11/2026

- 39 tests đạt: 37 unit/model tests, ranh giới PostgreSQL và phục hồi vị trí trên MongoDB thật.
- Build tuần tự Trip, Matching, Payment, Gateway, web đạt; TypeScript và kiểm thử frontend đạt.
- Chạy lại concurrency ví, phân quyền và hai chuyến tranh một Driver: đạt; không mất cập nhật,
  không nhân đôi nạp/rút, chỉ một reservation thắng. Không có Driver gần kết thúc `Failed`.
- Postman Wallet: 68 request, 24 assertion; COD: 67 request, 21 assertion; không lỗi.
- Quote hết hạn bị từ chối; lời mời hết hạn tìm Driver mới và trả đủ khoản giữ.
- Fixture rút bị từ chối trả lại đủ số dư. Timeout COD vào `PaymentReview`, giữ trạng thái qua
  restart Trip/Payment, retry về `Paid`; xác nhận thu lặp sau restart vẫn chỉ một journal.
- RabbitMQ dừng rồi khôi phục: Outbox tiếp tục xử lý; hủy chuyến trả đủ tiền giữ.
- Restart Matching khi tạm dừng: giữ tuyến/tiến độ, tiếp tục đến `Paid`; ledger và số dư
  Rider/Driver đúng. Trả khách trước khi xe tới bị từ chối.
- Smoke web trên Chrome: Rider đăng nhập, map, đặt lại vị trí seed, ví, hồ sơ, lịch sử,
  đăng xuất; Driver bị chặn; cookie HttpOnly và chặn mutation khác origin đạt.

## Lượt tiếp tục sau khôi phục lịch sử chat ngày 15/11/2026

- Chạy lại 37 unit/model tests và 2 integration tests: tất cả đạt, không bỏ qua test.
- Integration kiểm chứng ranh giới PostgreSQL và phục hồi vị trí trên MongoDB thật.
- Setup, architecture, giới hạn RAM Compose, TypeScript và script kiểm tra frontend đều đạt.
- Lượt này chưa chạy lại API/Postman, smoke trình duyệt hoặc build image; bằng chứng các mục
  đó ở trên thuộc lượt trước.
- PostgreSQL chỉ bật cho integration test và đã dừng; MongoDB tạm đã được test dọn dẹp.
  Không còn container chạy sau lượt này, giữ nguyên volume dữ liệu.

## Hai lỗi biên đã xử lý trên bản cuối

- **Ví với giá 0:** giữ/giải phóng/quyết toán theo đúng quote và journal, không đổi số dư.
  Migration `202611150001_ZeroFareWalletActivities` chỉ cho phép activity 0 ở nghiệp vụ chuyến;
  nạp/rút vẫn có giới hạn cũ. API thực chạy Rider096 số dư 0 đến `Paid`; Rider078 từ chối
  bốn Driver đến `Failed`, không treo tiền. Cấu hình khuyến mãi đã được khôi phục sau test.
- **Restart giữa lưu tiến độ và vị trí:** Mongo lưu cờ vị trí còn chờ ghi, chỉ xác nhận hoàn tất
  với đúng revision. Worker sửa vị trí cả khi đã tới đích/tạm dừng; kiểm tra đón/trả ghi vị trí
  trước khi cho chuyến tiến tiếp. Integration test tạo đúng trạng thái ghi dở trên MongoDB thật,
  tái tạo repository/workflow, xác nhận vị trí đúng và không chạy lại chuyển động. Test kiểm tra
  thêm 101 hành trình cũ, bản ghi trước nâng cấp và acknowledgement cũ không xóa cập nhật mới.
- Kiểm tra hồi quy sau hai sửa cuối: ví có thu tiền qua restart Matching đến `Paid`, số dư/ledger
  đúng; nạp/rút đồng thời và phân quyền đạt; Postman COD 69 request, 21 assertion, không lỗi.
  Hai image Matching/Payment được build tuần tự; format C#, Python và kiểm tra diff đạt.

Hướng dẫn tái chạy hai ca biên nằm trong [Postman guide](postman-guide.md). Toàn bộ container
dự án và container MongoDB tạm của integration test đã dừng sau kiểm thử; volume dự án được giữ.

Các lỗi đã ghi nhận trong đợt rà soát này đã được sửa và kiểm chứng. Bằng chứng phục hồi ghi dở
dùng lỗi được tái hiện có kiểm soát, không phải thử mọi thời điểm có thể xảy ra khi tiến trình bị tắt.
Các lần thử giữ lại lịch sử giả lập để tra cứu; không xóa volume hoặc sửa trực tiếp số dư để vượt test.
