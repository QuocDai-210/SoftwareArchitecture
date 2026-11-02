# Quản lý chung, giữ ranh giới microservice

Đã triển khai quản lý chung qua `Makefile` tại gốc repo. Giữ nguyên container,
database, transaction và cách triển khai từng microservice.

PDF §2.4 yêu cầu Trip Service, Matching & Pricing Service và Payment Service.
Gateway là điểm vào riêng; mỗi service giữ database, transaction và các tầng Clean
Architecture riêng. Payment giữ phần lõi hiện có; tích hợp VNPAY tạm ngoài ưu tiên.

| Nhóm chức năng                                       | Nơi quản lý chức năng                       | Ranh giới giữ nguyên                                 |
| ---------------------------------------------------- | ------------------------------------------- | ---------------------------------------------------- |
| Tìm địa chỉ, chọn tọa độ, định tuyến Geoapify        | Matching Service, khu vực `Maps`            | Không tạo Map Service riêng                          |
| Vị trí Driver, hồ sơ, duyệt/khóa qua Ops             | Matching Service, khu vực `Drivers`         | Dữ liệu Driver thuộc MongoDB; Keycloak giữ danh tính |
| Mô phỏng xe, tốc độ, tạm dừng và tiếp tục            | Matching Service, khu vực `Simulation`      | Không tạo Simulator Service riêng                    |
| Hồ sơ User, đặt/nhận/từ chối xe, trạng thái, lịch sử | Trip Service                                | Chỉ Trip Service ghi TripDB                          |
| User, Driver, Ops trên web                           | Một ứng dụng Next.js, màn hình theo vai trò | Không tạo ba ứng dụng hoặc ba BFF riêng              |
| API quản trị Ops                                     | Đặt tại service sở hữu nghiệp vụ            | Không tạo Ops Service truy cập chéo database         |
| Bật/tắt hệ thống và các nhóm phục vụ công việc       | `Makefile` gọi Docker Compose               | Container của ba microservice và Gateway vẫn riêng   |
| Metrics, logs, traces                                | Profile `observability` hiện có             | Mặc định tắt để tiết kiệm RAM                        |

Maps, Drivers và Simulation cùng thuộc Matching. Makefile chỉ điều phối vận hành;
không tham gia xử lý nghiệp vụ, truy vấn dữ liệu hoặc DI của ứng dụng.

- Microservice: Trip/Matching/Payment giữ API, database và tài khoản DB riêng; trao đổi
  qua HTTP/sự kiện. Dừng một nhóm không xóa volume hoặc dữ liệu của nhóm khác.
- Clean Architecture: `Domain` giữ luật nghiệp vụ, `Application` giữ use case/port,
  `Infrastructure` triển khai adapter, `Api` ánh xạ HTTP và ghép dependency.
- SOLID: quản lý container nằm ngoài ứng dụng; API/use case/domain không phụ thuộc
  Makefile. Không thêm lớp quản lý chung vào nghiệp vụ hoặc repository truy cập chéo DB.

`make up`, `make stop`, `make status`, `make logs`, `make build`, `make check` là
các lệnh chung. `make trip`, `make matching`, `make payment` chuyển sang nhóm phát
triển tương ứng, dừng các container dự án cũ để giải phóng RAM rồi bật API/database
của nhóm cùng Keycloak/RabbitMQ. `make up` bật đầy đủ luồng liên service và web.
Nhóm Trip riêng phục vụ API/database Trip, chưa đủ cho ghép xe hoặc thanh toán.

Build tuần tự, tổng ngân sách dự án tối đa 12 GB; observability mặc định tắt.
`make up` và nhóm phát triển đồng bộ cấu hình Keycloak bằng script hiện có, giữ
subject/mật khẩu đã lưu. Makefile tắt parallel execution kể cả khi gọi `make -j`.
