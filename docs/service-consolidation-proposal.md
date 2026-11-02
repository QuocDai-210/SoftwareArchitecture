# Đề xuất gom chức năng để review

Trạng thái: chờ duyệt. Giữ nguyên cách triển khai microservice hiện tại.

PDF §2.4 yêu cầu Trip Service, Matching & Pricing Service và Payment Service.
Gateway là điểm vào riêng; mỗi service giữ database, transaction và các tầng Clean
Architecture riêng. Payment giữ phần lõi hiện có; tích hợp VNPAY tạm ngoài ưu tiên.

| Nhóm chức năng                                   | Nơi đề xuất gom                             | Ranh giới giữ nguyên                                 |
| ------------------------------------------------ | ------------------------------------------- | ---------------------------------------------------- |
| Tìm địa chỉ, chọn tọa độ, định tuyến Geoapify    | Matching Service, khu vực `Maps`            | Không tạo Map Service riêng                          |
| Vị trí Driver, nhận chuyến, duyệt hồ sơ qua Ops  | Matching Service, khu vực `Drivers`         | Dữ liệu Driver thuộc MongoDB; Keycloak giữ danh tính |
| Mô phỏng xe, tốc độ, tạm dừng và tiếp tục        | Matching Service, khu vực `Simulation`      | Không tạo Simulator Service riêng                    |
| Hồ sơ User, đặt xe, trạng thái và lịch sử chuyến | Trip Service                                | Chỉ Trip Service ghi TripDB                          |
| User, Driver, Ops trên web                       | Một ứng dụng Next.js, màn hình theo vai trò | Không tạo ba ứng dụng hoặc ba BFF riêng              |
| API quản trị Ops                                 | Đặt tại service sở hữu nghiệp vụ            | Không tạo Ops Service truy cập chéo database         |
| Bật/tắt hệ thống và các nhóm phục vụ công việc   | Một lệnh quản lý gọi Docker Compose         | Container của ba microservice và Gateway vẫn riêng   |
| Metrics, logs, traces                            | Profile `observability` hiện có             | Mặc định tắt để tiết kiệm RAM                        |

Phần Maps, Drivers và Simulation đã cùng nằm trong Matching; đề xuất giữ cách tổ
chức này và làm rõ trách nhiệm trong tài liệu. Phần cần duyệt trước khi bổ sung là
lệnh quản lý nhóm service. Không đề xuất gộp ba database, gộp Gateway vào Trip hoặc
chạy nhiều microservice chung container: các thay đổi đó làm khó đánh giá ranh giới
và khả năng triển khai độc lập theo PDF.
