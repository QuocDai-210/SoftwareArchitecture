# Rà soát SwiftRide theo đề của giảng viên

Ngày rà soát: 05/11/2026.

Nguồn yêu cầu: `RideLite-KienTrucPhanMem.pdf` do người dùng cung cấp, 11 trang,
tại `C:/Users/My Name Is Duong/Downloads/RideLite-KienTrucPhanMem.pdf`.
Số mục dưới đây theo PDF; trang in trong tài liệu bắt đầu sau bìa.

## Kết luận

Mã nguồn có phần lớn cấu trúc kiến trúc và luồng chuyến đi được mô tả trong đề.
Chưa đủ cơ sở kết luận toàn bộ hệ thống đã nghiệm thu trên máy Windows này:
khởi động đang vướng `postgres-init`, và phiên rà soát không truy cập được Docker API.

Khác biệt nghiệp vụ lớn nhất là chỉ chạy COD. Đề mô tả Payment trừ tiền người đi
và minh họa Factory bằng Card/Wallet. Nếu giảng viên chưa chấp thuận thay đổi này,
cần hoàn thiện một luồng thanh toán ví hoặc provider giả lập để bám sát đề.
Đề không bắt buộc tích hợp ngân hàng thật hay VNPAY.

## Những điểm cần xử lý, theo mức ưu tiên

### 1. Làm rõ hoặc khắc phục khác biệt thanh toán COD

- **Yêu cầu:** §2.2 bước 7 mô tả trừ tiền rider; §5.3 yêu cầu giao dịch, sổ cái,
  hoàn tiền, bồi thường và idempotency; §7 minh họa Factory bằng Card/Wallet.
- **Hiện trạng:** `TripCommands.Create` và `PaymentCommands.Charge` từ chối mọi
  phương thức khác `Cod`. DI chỉ đăng ký `CashMethod`.
- **Hệ quả:** có mã `CardMethod`/`WalletMethod` và kiểm thử adapter, nhưng không thể
  dùng chúng qua luồng ứng dụng đang chạy. Không nên trình bày các file này như
  tính năng thanh toán online đã hoạt động.
- **Hoàn tiền hiện tại:** Ops xác nhận đã trả tiền mặt ngoài hệ thống, sau đó ghi
  bút toán đảo. `CashMethod.Refund` không thực hiện chuyển tiền. Đây là mô hình
  ghi nhận COD hợp lý, nhưng khác với hoàn tiền ví/provider.
- **Bồi thường:** Saga đã có giải phóng reservation. Nhánh bồi thường thanh toán
  sau capture trong `PaymentCommands` không được kích hoạt bởi `CashMethod`, vì
  capture COD luôn trả Pending. Không thể dùng demo COD để chứng minh nhánh này.
- **Đề xuất:** nếu chưa có chấp thuận COD, nối Wallet hoặc provider giả lập vào
  runtime; thêm lựa chọn phương thức và kiểm thử thiếu tiền, retry lỗi tạm thời,
  gửi trùng, hoàn tiền, bồi thường. Không chỉ đăng ký adapter: còn phải điều chỉnh
  các điều kiện chỉ cho COD và kiểm tra lại giao dịch, quyền, giao diện.

Bằng chứng: [TripCommands](../backend/src/TripService/Application/Commands/TripCommands.cs),
[PaymentCommands](../backend/src/PaymentService/Application/Commands/PaymentCommands.cs),
[đăng ký Payment](../backend/src/PaymentService/Infrastructure/Composition/ServiceRegistration.cs),
[CashMethod](../backend/src/PaymentService/Application/Methods/CashMethod.cs),
[kiểm thử adapter](../backend/tests/SwiftRide.Checks/ApplicationChecks.cs).

### 2. Chưa có đường chạy nghiệm thu đầy đủ trực tiếp trên Windows

- `scripts/manage.ps1` hỗ trợ setup/build/up/stop và các nhóm service; chưa có lệnh check.
- Bộ nghiệm thu tổng vẫn là Bash `scripts/verify.sh`, dùng cú pháp shell Linux.
- `scripts/check-frontend.mjs` gọi trực tiếp file shim `node_modules/.bin/tsc`;
  cách này chưa tương thích việc thực thi chương trình trên Windows.
- Browser runner mặc định tìm Chrome ở các đường dẫn Linux; trên Windows phải
  cung cấp `CHROME_BIN` và kiểm tra lại toàn bộ runner.
- Trong lần khởi động hiện tại, `deploy/postgres/init.sh` có CRLF. Đã chuyển các
  script `.sh` sang LF và thêm `.gitattributes` để giữ LF qua checkout. Đây là
  nguyên nhân phù hợp với lỗi shell exit 2, nhưng chưa đọc được log container
  để khẳng định đó là nguyên nhân duy nhất.

Đề xuất: bổ sung runner kiểm thử PowerShell hoặc container kiểm thử; chạy tuần tự,
giữ tổng RAM dưới 12 GB, dừng service được bật riêng cho kiểm thử và giữ volume.
Trước mắt cần chạy lại `manage.ps1 up` để xác nhận PostgreSQL khởi tạo thành công.

Bằng chứng: [manage.ps1](../scripts/manage.ps1), [verify.sh](../scripts/verify.sh),
[frontend checks](../scripts/check-frontend.mjs), [browser checks](../scripts/browser-check.mjs),
[PostgreSQL init](../deploy/postgres/init.sh), [quy tắc xuống dòng](../.gitattributes).

### 3. Cần cập nhật bằng chứng nghiệm thu và tài liệu bảo vệ

- `docs/acceptance.md` ghi kết quả của các phiên cũ và đường dẫn Linux bên ngoài repo.
  Chúng là thông tin lịch sử, không phải kết quả chạy lại trên máy hiện tại.
- Cần một lần kiểm tra end-to-end có thể tái hiện: tạo chuyến → nhận giá → xác nhận
  → driver nhận → đón/trả → thanh toán → Paid; kèm hủy/từ chối và gửi lặp.
- Với các phần mở rộng đã chọn triển khai, cần minh họa broker ngừng/restart,
  Outbox phục hồi, message trùng, sổ cái cân bằng và quyền truy cập chéo bị chặn.
- Tài liệu kiến trúc có câu không hỗ trợ hủy sau nhận chuyến; code hiện cho phép
  hủy ở `DriverAccepted` và `EnRoute`. Nên sửa mô tả thành hủy trước khi đón khách.
- Factory nhận adapter qua DI là bằng chứng tốt. Tuy nhiên không nên tuyên bố
  thêm phương thức thanh toán chỉ cần đăng ký DI, vì use case còn chặn khác COD.

Đề xuất: lưu bảng ca kiểm thử, kết quả, phiên bản mã và ảnh minh họa ngoài repo
theo quy định dự án; trong báo cáo chỉ dẫn tới bằng chứng còn truy cập được.

Bằng chứng: [nghiệm thu](acceptance.md), [kiến trúc](architecture.md),
[TripState](../backend/src/TripService/Domain/TripState.cs),
[PaymentMethodFactory](../backend/src/PaymentService/Application/Methods/PaymentMethodFactory.cs).

## Những phần đã có bằng chứng trong mã nguồn

| Yêu cầu PDF | Đối chiếu hiện tại | Kết luận rà soát tĩnh |
| --- | --- | --- |
| §2.4: ba service, Payment bắt buộc | Trip, Matching, Payment có API/project riêng | Đã có |
| §3.3: dữ liệu riêng theo service | TripDB, MatchDB, PayDB; tài khoản tách quyền | Phù hợp thiết kế |
| §3: client qua Gateway | Next.js proxy → YARP → service | Đã có |
| §3: JWT, rate limit, aggregation | Gateway cấu hình JWT, hạn mức, ScreenEndpoints | Đã có |
| §2.2/§4: vòng đời chuyến | TripState, xác nhận giá, từ chối/tìm lại, đón/trả | Đã có |
| §5.2: tài xế gần nhất | Mongo geo-query, lọc khả dụng, giữ chỗ nguyên tử | Đã có |
| §2.3: công thức giá | Standard → Surge → Promo, cộng thuế và làm tròn cuối | Đã có |
| §5.3: sổ cái, gửi lặp, refund | Transaction, khóa, unique journal, bút toán đảo COD | Có, khác phạm vi thanh toán |
| §6/§8.1: SOLID, phân tầng | Domain/Application/Infrastructure/Api và các port | Đã có cấu trúc |
| §7/§9.2: Saga, Outbox, Pub/Sub | TripWorkflow, MassTransit, EF Outbox/Inbox | Đã có mã/cấu hình |
| §9.2: retry và timeout | HTTP timeout, backoff/jitter, retry consumer hữu hạn | Đã có mã/cấu hình |
| §9.3: quan sát | CorrelationId, OTel, dashboard LGTM theo profile | Đã có mã/cấu hình |
| §8: xUnit + Moq | CoreTests, WorkflowTests, StorageTests | Có bộ kiểm thử |

Rà soát tĩnh không thay thế chạy tích hợp. Ví dụ, có cấu hình Outbox chưa chứng minh
message thực sự phục hồi sau restart trên môi trường đang gặp lỗi khởi động.

## Những điểm không nên coi là thiếu yêu cầu

- **Không dùng SQL Server cho Trip:** §3.3 cho phép cùng loại CSDL nếu tách schema/database
  theo service. TripDB và PayDB cùng PostgreSQL vẫn hợp lệ; không cần đổi lại chỉ để đủ đề.
- **Có nhiều hơn ba container:** giới hạn áp dụng cho service nghiệp vụ. Gateway, database,
  broker và Keycloak là thành phần hạ tầng, không phải service nghiệp vụ thứ tư.
- **Chưa có GPS thật, chat, thông báo đẩy, HA/Kubernetes hoặc cổng ngân hàng thật:** PDF
  không đặt chúng thành tiêu chí bắt buộc. Không nên ưu tiên trước luồng cốt lõi.
- **Observability mặc định tắt:** phù hợp chạy theo nhu cầu và giới hạn RAM. §1 cho phép
  luồng đơn giản; broker, Outbox và observability được nêu là phần mở rộng.
- **Surge/khuyến mãi là cấu hình:** công thức và Strategy có; PDF chưa yêu cầu thuật toán
  tự tính surge theo cung/cầu hoặc màn hình quản trị khuyến mãi.

## Kiểm tra thực hiện trong phiên này

- Đọc nội dung đủ 11 trang PDF và đối chiếu mã, DI, API, schema, runner kiểm thử.
- `check-architecture.py`: đạt, 17 project backend và 34 file frontend được kiểm tra.
- `check-resources.py`: đạt, tổng hạn mức tất cả profile là 10,07/12 GB.
  Đây là tổng hạn mức cấu hình, không phải số đo RAM thực tế; bao gồm profile tùy chọn.
- Kiểm tra byte và Git EOL: các script `.sh` đã dùng LF, có quy tắc giữ LF.
- Chưa chạy lại build, xUnit, trình duyệt hay lỗi broker trong phiên rà soát.
- Chưa xác nhận PostgreSQL hoạt động sau sửa LF: Docker API từ phiên công cụ trả
  `permission denied`; cần kết quả chạy lại từ terminal người dùng.

## Thứ tự hoàn thiện đề xuất

1. Xác nhận toàn bộ hệ thống khởi động và dùng được trên Windows.
2. Chốt phạm vi COD với giảng viên; nếu không được chấp thuận, hoàn thiện thanh toán giả lập.
3. Hoàn thiện runner Windows và kiểm thử các nhánh thành công, lỗi, gửi lặp, hoàn tiền.
4. Cập nhật tài liệu, sơ đồ và bằng chứng demo đúng với phiên bản thực tế.
