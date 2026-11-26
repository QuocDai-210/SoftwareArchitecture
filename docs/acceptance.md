# Nghiệm thu backend SwiftRide

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
