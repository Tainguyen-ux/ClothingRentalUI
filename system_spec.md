# Tài liệu Đặc tả & Phân tích Hệ thống Quản lý Thuê & Bán Trang phục

Tài liệu này đặc tả toàn bộ yêu cầu nghiệp vụ, cơ sở dữ liệu, các tính năng và quy trình hoạt động của hệ thống dựa trên tài liệu mô tả chi tiết (`Mota.rtf`).

---

## 1. Phân tích Tác nhân & Phân quyền (Actors & Roles)

Hệ thống phân chia rõ ràng trách nhiệm của từng nhóm người dùng để đảm bảo tính bảo mật và đối soát dòng tiền:

1. **Admin (Chủ shop / Quản trị viên):** 
   - Quản lý tài khoản và phân quyền cho nhân viên.
   - Cấu hình hệ thống (Danh mục loại mặt hàng, Danh mục loại giá, thiết lập quy tắc tính phí phát sinh, định mức cảnh báo tồn kho tối thiểu).
   - Xem báo cáo doanh thu chi tiết, doanh số theo tài khoản, tồn kho và hao hụt.
   - Quyền đặc biệt: Mở khóa đơn hàng đã lưu, chỉnh sửa đơn xuất bán đã đóng.
2. **Staff (Nhân viên cửa hàng):**
   - Thực hiện các nghiệp vụ hằng ngày: Nhập kho, tạo đơn thuê/bán, quét barcode, cập nhật phát sinh, xử lý trả hàng, thanh lý sản phẩm.

---

## 2. Database (Cơ sở dữ liệu)

### a. Danh mục (DM) loại mặt hàng
- **Tên loại** (Ví dụ: Váy cưới, Vest, Áo dài...)
- **Tiếp đầu ngữ mã hàng** (Ví dụ: VC, VS, AD...)

### b. Danh mục (DM) loại giá
- **Tên loại**
- **Giá tiền** (Giá thuê cơ bản)
- **Tiền cọc**

### c. Mặt hàng (Sản phẩm)
- **Mã hàng:** Công thức sinh mã tự động: `[Tiếp đầu ngữ mã hàng] + [năm] + [tháng] + [ngày] + [4 số thứ tự]` (Bắt buộc).
- **Tên hàng:** Chuỗi ký tự (Bắt buộc).
- **Số lượng:** Mặc định `0` (Bắt buộc).
- **Màu sắc:** Chuỗi ký tự (Không bắt buộc).
- **Mô tả:** Chuỗi ký tự (Không bắt buộc).
- **Giá nhập:** Mặc định `0` (Bắt buộc).
- **Giá cho thuê 1 ngày:** Chọn liên kết từ *DM loại giá* (Bắt buộc).
- **Hình ảnh:** Nếu có, tải lên và lưu trữ về Google Drive với cấu trúc thư mục phát sinh tự động theo tên *"DM loại giá"* (Không bắt buộc).
- **Trạng thái:** Còn sử dụng hay không.

### d. Đơn hàng cho thuê
- **Mã đơn hàng:** Công thức sinh mã tự động: `[năm] + [tháng] + [ngày] + [4 số thứ tự]` (Bắt buộc).
- **Tên khách hàng:** (Bắt buộc).
- **Số điện thoại:** (Bắt buộc).
- **Có CCCD:** Có hoặc không (Bắt buộc).
- **Danh sách mặt hàng thuê (Chi tiết đơn thuê):**
  - Mã hàng
  - Tên hàng
  - Giá cho thuê (áp dụng tại thời điểm thuê)
  - Tiền cọc (áp dụng tại thời điểm thuê)
  - Số ngày thuê (mặc định ban đầu là 1 cho từng mặt hàng)
  - Ngày gia hạn (điền tay nếu có)
  - Chi phí phát sinh (tiền phạt hoặc chi phí phát sinh riêng lẻ của từng mặt hàng)
  - Lý do phát sinh (điền tay, có thể trống)
- **Tổng giá tiền:** Tổng tiền thuê của tất cả mặt hàng (Bắt buộc).
- **Tổng tiền cọc:** Tổng tiền cọc của tất cả mặt hàng (Bắt buộc).
- **Tổng tiền phát sinh:** Tổng tiền phạt hoặc phát sinh thêm của toàn bộ đơn hàng (Bắt buộc).
- **Tổng tiền đơn hàng thanh toán cuối cùng.**
- **Trạng thái:** Mở hoặc Đóng (Bắt buộc).
- **Ngày tạo đơn:** Mặc định ngày hiện tại (Bắt buộc).
- **Ngày đóng đơn:** Ghi nhận khi khách trả hết đồ và hoàn tất thanh toán (Bắt buộc).

### e. Lịch sử đơn hàng
Ghi nhận toàn bộ biến động tiền phát sinh của từng sản phẩm từ lúc tạo đơn đến khi khách hàng trả hàng và xác nhận đóng đơn hàng.
- **Quy tắc tính phí thuê & phát sinh:**
  - Giá thuê áp dụng cho 1 ngày: `ngày thuê + 1` (khách trả trong ngày hôm sau không bị tính thêm tiền).
  - Phát sinh trễ hạn: `+ 10,000 VND / ngày`.
  - Qua ngày thứ 4 (ngày thuê + 4): Hệ thống tự động cộng thêm lại giá cho thuê cơ bản ban đầu của mặt hàng đó.
- **Ghi nhận thông tin nhân viên thao tác:**
  - Người tạo đơn.
  - Người ghi nhận phát sinh đơn hàng.
  - Người đóng đơn hàng.

---

## 3. Các Tính Năng Chi Tiết

### a. Hệ thống & Xác thực
- Tạo tài khoản, phân quyền người dùng.
- Đăng nhập, đăng xuất, đổi mật khẩu.

### b. Quản lý Kho & Nhập hàng tồn kho
- Nhập sản phẩm mới theo danh mục mặt hàng và loại giá.
- Hỗ trợ tải hình ảnh từ điện thoại, máy tính bảng hoặc tải tệp trực tiếp từ máy tính.
- **UX cải tiến:** Giữ nguyên thông tin lựa chọn danh mục mặt hàng và loại giá cho lượt lưu trước đó để giảm bớt thao tác chọn lại khi nhập kho liên tục.
- **In Barcode:** Cho phép in lại mã barcode của mặt hàng ngay tại giao diện danh sách sản phẩm bên ngoài để thao tác nhanh.
- **Tìm kiếm:** Tìm sản phẩm nhanh chóng bằng cách quét mã barcode tại màn hình danh sách sản phẩm.

### c. Quản lý Đơn hàng Cho thuê

#### 1) Tạo đơn hàng cho khách:
- Mã đơn hàng tự động sinh.
- Chọn mặt hàng bằng quét mã barcode dán trên sản phẩm.
- Tiền cọc tự nhảy theo loại giá, cho phép chỉnh sửa (có cấu hình bật/tắt tính năng cho phép sửa tiền cọc).
- Tiền mặt hàng tự nhảy theo cấu hình.
- Điền tên và số điện thoại của khách hàng.
- Điền số ngày cho thuê (mặc định tự động là 1 cho từng mặt hàng) -> Hệ thống tự động tính số tiền tương ứng theo quy tắc. Cho phép sửa nhập số ngày khác.
- Mục xác nhận CCCD: Chọn `Yes` hoặc `No`, nếu chọn `Yes` thì hiển thị ô nhập thông tin CCCD của khách.
- Hỗ trợ chụp ảnh trực tiếp sản phẩm bằng điện thoại/tablet hoặc upload file ảnh đính kèm vào đơn hàng.
- Hỗ trợ bước **Lưu nháp** và **Lưu kết thúc**.
- Sau khi nhấn **Lưu kết thúc**: Giữ nguyên màn hình và hiển thị nút **"In đơn hàng"** và **"In phiếu thuê đồ"**.

#### 2) Danh sách đơn thuê:
- Bộ lọc theo thời gian (Từ ngày - Đến ngày) dựa trên ngày tạo đơn.
- Tìm kiếm nhanh bằng: Mã đơn hàng, SĐT, CCCD, Tên khách hàng.
- Xem nhanh hình ảnh chụp đính kèm đơn hàng.
- Nút chức năng: In đơn hàng, In phiếu thuê đồ.
- **Khóa dữ liệu:** Sau khi đơn hàng lưu thành công, dữ liệu sẽ được khóa lại. Nút **Cập nhật phát sinh** sẽ hiển thị. Chỉ có tài khoản được phân quyền mới có thể mở lại đơn hàng sau khi lưu.

#### 3) Ghi nhận phát sinh (Khi khách đang thuê):
- Chọn mã đơn hàng cần chỉnh sửa trong danh sách.
- Nhân viên có thể cập nhật thủ công các thông tin cho TỪNG mặt hàng:
  - Cập nhật số ngày gia hạn thuê (nhập tay).
  - Cập nhật chi phí phát sinh thêm (nhập tay).
  - Cập nhật lý do phát sinh (nhập tay, có thể bỏ trống).

#### 4) Trả đồ & Đóng đơn hàng:
- Tìm kiếm đơn hàng của khách khi đến hẹn trả đồ.
- Nhân viên kiểm tra (check) từng sản phẩm thực tế nhận lại.
- Hệ thống báo tổng số tiền cần hoàn trả hoặc thu thêm từ khách hàng.
- Xác nhận đóng đơn hàng. Hệ thống tự động cộng lại số lượng vào tồn kho.

### d. Đơn xuất bán (Bán đứt sản phẩm)
Dành cho trường hợp khách mua đứt sản phẩm thay vì thuê.
- **Tạo đơn xuất bán:**
  - Chọn mặt hàng bằng cách quét mã barcode, nhập mã tay hoặc tìm theo tên.
  - Nhập số lượng cần xuất bán.
  - Nhập giá bán.
  - Nhập thông tin đơn vị xuất bán.
  - Hỗ trợ **Lưu nháp** và **Lưu thành công**.
  - Sau khi lưu thành công, hệ thống tự động trừ tồn kho và đóng đơn. Chỉ tài khoản có phân quyền mới được quyền chỉnh sửa đơn xuất bán này.
- **Danh sách đơn xuất bán:**
  - Lọc theo khoảng thời gian (Từ ngày - Đến ngày).
  - Tìm kiếm theo đơn vị xuất bán hoặc theo nội dung sản phẩm xuất.

### e. Xuất thanh lý (Ngừng sử dụng sản phẩm hết hạn)
Dành cho các trang phục quá cũ, hỏng hóc hoặc không còn sử dụng được nữa.
- **Danh sách thanh lý:**
  - Hiển thị các sản phẩm còn tồn kho.
  - Tìm kiếm sản phẩm bằng cách quét barcode, nhập mã hoặc nhập tên.
  - Xem được **Giá nhập** và **Tổng tiền cho thuê** tích lũy tính đến thời điểm hiện tại của sản phẩm đó (giúp đánh giá hiệu quả đầu tư).
  - Đánh dấu check chọn "Ngưng sử dụng".
  - Điền số lượng thanh lý (mặc định là 1, có thể chỉnh sửa).
  - Khi lưu, hệ thống tự động trừ tồn kho tương ứng.
- **Lịch sử thanh lý:**
  - Cho phép xem danh sách và lịch sử các sản phẩm đã ngừng sử dụng trong khoảng thời gian (Từ ngày - Đến ngày).

---

## 4. Báo cáo & Thống kê

Hỗ trợ bộ lọc thống kê theo khoảng thời gian (Từ ngày - Đến ngày) với các chỉ số:
1. **Doanh thu từ đơn hàng đã đóng:** Chi tiết tiền thuê, tiền cọc phát sinh...
2. **Doanh thu ước tính từ đơn hàng chưa đóng (đang mở):** Thống kê tiền thuê, tiền cọc đang giữ...
3. **Danh sách CCCD đã nhận:** Xem thông tin CCCD của khách hàng từ cả đơn hàng đã đóng và chưa đóng.
4. **Doanh thu theo tài khoản thao tác:** Thống kê hiệu suất bán hàng của từng nhân viên.
5. **Cảnh báo tồn kho dưới hạn mức:** Liệt kê các mặt hàng có số lượng tồn kho nhỏ hơn định mức được thiết lập trong phần cấu hình hệ thống.

---

## 5. Đề xuất Công nghệ & Thiết kế Hệ thống

- **Frontend:** React.js / Vite với thiết kế responsive, tối ưu cho việc quét barcode nhanh qua webcam/camera điện thoại bằng `html5-qrcode` hoặc kết nối máy quét cầm tay chuyên dụng.
- **Backend:** Node.js (NestJS/Express) hoặc Python (FastAPI).
- **Database:** Relational Database (PostgreSQL/MySQL) nhằm đảm bảo tính chính xác và an toàn tuyệt đối cho dòng tiền, lịch sử cọc và báo cáo.
- **Lưu trữ ảnh:** Google Drive API tích hợp để lưu ảnh theo cấu trúc thư mục loại giá (DM loại giá), giảm tải dung lượng cho máy chủ chính.
