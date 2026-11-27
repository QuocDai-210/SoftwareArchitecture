# Nghiệm thu SwiftRide

## Bản web Rider/Driver và Gateway tổng hợp

Ngày 27/11/2026 trên Linux, dùng project Compose riêng `swiftride-pdf-uicheck`. Giới hạn RAM của 11 thành phần khi bật toàn bộ profile là 7,92/12 GB.

| Hạng mục | Kết quả |
|---|---|
| Build và test | 17 project backend qua kiểm tra kiến trúc, build 0 lỗi và 0 cảnh báo. 11/11 test đơn vị và 1/1 test tích hợp database đạt. Build production của Next.js thành công. |
| Trình duyệt Chromium | Hai phiên độc lập đăng nhập Keycloak bằng Rider và Driver. Rider đặt, xác nhận giá. Driver ở kích thước 390 × 844 nhận chuyến, đang đến, đón và trả khách. Rider thấy biên nhận Wallet ở trạng thái Paid. Driver không nhận dữ liệu biên nhận. Đã thử đăng xuất. |
| Từ chối và hủy trên web | Rider tự nhập địa điểm bằng tọa độ. Driver từ chối lời mời. Rider nhận báo giá mới và hủy thành công. |
| Postman | Newman chạy hai chuyến Card/Wallet, gồm đọc màn hình tổng hợp. 47 request, 65 assertion, 0 lỗi trong lần chạy này. Số request thay đổi theo số lần polling. |
| Phân quyền và sự cố | API màn hình trả 403 cho Rider khác. Các kịch bản tranh tài xế, từ chối, timeout, broker dừng, restart, phát lại và hoàn một khoản thu sai đều đạt. |
| Quan sát | Tempo tìm được span `trip.workflow` trong cùng trace với request đặt xe. Prometheus ghi nhận bốn loại kết quả xử lý thanh toán: `settled`, `declined`, `faulted` và `mismatch`. |

Giao diện Driver được kiểm thử trên Chromium với viewport di động. Hướng dẫn chạy web và bộ kiểm thử nằm trong [README](../README.md).

## Nghiệm thu backend trước khi tích hợp web

Kiểm thử trên Linux ngày 27/11/2026 với project Compose `swiftride-pdf`. PostgreSQL, MongoDB, RabbitMQ và Keycloak dùng volume riêng được tạo cho backend này. Lần nghiệm thu cuối dùng cùng bộ image trong project tạm `swiftride-pdf-acceptance`, khởi động từ bốn volume trống. Các lần restart sau đó dùng lại đúng volume của lần chạy.

| Kiểm tra | Kết quả quan sát |
|---|---|
| Build .NET 10 | 17 project backend và project test build thành công, 0 cảnh báo, 0 lỗi. 11 test đơn vị đạt. Bốn image API build tuần tự. |
| Đăng nhập | Hai Rider và bốn Driver đăng nhập qua Authorization Code + PKCE S256. Service lấy token qua client credentials. Các lời gọi qua Gateway và từ Trip tới Matching dùng cùng issuer Keycloak. |
| Postman | Newman chạy collection qua Gateway. Cả Card và Wallet đi từ `Requested` đến `Paid`. 64 assertion đạt trong lần chạy từ volume trống, gồm kiểm tra một giao dịch, một journal cân bằng, đúng giá và số dư sau chuyến. Số assertion phụ thuộc số lần polling. |
| Matching và phân quyền | Hủy khi chờ xác nhận giá, không có ứng viên, bốn Driver khác nhau từ chối và hai Rider tranh cùng tài xế đều cho trạng thái dự kiến. Sai Driver nhận chuyến hoặc cập nhật hành trình trả 403. Phiên bản cũ trả 409. |
| Lỗi thanh toán | Thẻ bị từ chối và ví thiếu tiền đưa Trip vào `PaymentReview`, ví giữ số dư, không có bút toán. Với thẻ timeout, log ghi lần giao ban đầu và ba lần retry. Phát lại với cùng khóa ở lần thanh toán thứ hai đưa Trip về `Paid`. Test đơn vị kiểm tra lỗi cũ đến muộn. |
| Idempotency và Outbox | Cả Card và Wallet đều trả lại kết quả khi gọi thu hoặc hoàn lặp. Mỗi giao dịch hoàn có một journal Charge và một journal Refund, tổng theo từng tài khoản bằng 0. Wallet trở về số dư trước chuyến. |
| Restart và retry | Khi RabbitMQ dừng, dropoff vẫn ghi `PaymentPending`. Sau khi Trip và Payment khởi động lại, Payment gặp lỗi PostgreSQL 40001 trong lần thu đầu và thành công ở lần giao tiếp theo. Giao lại `RideCompleted` qua RabbitMQ giữ đúng một lần thu. |
| Bồi thường Saga | Thu thừa 1 VND trong kịch bản kiểm thử dẫn tới một bút toán đảo và `PaymentReview`. Yêu cầu hủy sau trả khách trả 409. |
| Quyền database và seed | Test tích hợp xác nhận tài khoản Trip và Payment chỉ kết nối được database nghiệp vụ của mình. PostgreSQL từ chối số dư âm. MongoDB có 4 tài xế seed và chỉ mục `Location: 2dsphere`. |
| Observability | Profile Grafana hoạt động. Prometheus trả 7 capture thành công và 3 từ chối, trùng kết quả nhóm từ PayDB trên project `swiftride-pdf` sau các lần chạy và hoàn tiền. |

## Đối chiếu một chuyến

Chuyến Wallet `41566b06-99da-4ae9-8590-61a1247fd596` có giá 27.781 VND. Số dư ví từ 500.000 VND xuống 472.219 VND. Chuyến Card ngay trước đó có cùng giá và giữ nguyên số dư ví. Log của Gateway, Trip, Matching và Payment đều có CorrelationId sau.

```sh
docker compose -p swiftride-pdf-acceptance logs gateway trip matching payment | rg -F 'd1687c79-852b-4f1e-a965-a78609f1aec6'
```

Kết thúc nghiệm thu, số dư hai ví lần lượt là 472.219 VND và 0 VND. Đã dừng toàn bộ container của cả hai project kiểm thử, gồm Grafana. Các volume cũ vẫn có mặt. Tổng giới hạn RAM của cấu hình Compose khi bật mọi profile là 7,52 GB.

Lệnh chạy lại có trong [README](../README.md#kiểm-thử). Windows PowerShell và giao diện Postman Desktop chưa được chạy trực tiếp.
