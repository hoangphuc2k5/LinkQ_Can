# Kiến trúc & đảm bảo kỹ thuật

```
Angular (localhost:4200) --HTTP--> Local Client Service (127.0.0.1:5100) --SQLite (file)
                                          |  SyncEngine + BackgroundService
                                          v  HTTP + X-Api-Key
                                   Central API (:5000) --EF Core--> SQL Server
```

## Luồng lưu
1. Angular POST `/api/local/weighing` -> Local Service ghi SQLite với `SyncStatus=Pending` (UUID tạo tại client) -> trả 201.
2. `SyncTrigger` đánh thức `SyncBackgroundService`; engine health-check server rồi **push** các bản ghi Pending, sau đó **pull** theo cursor.
3. Chỉ khi server trả `Accepted`/`Duplicate` cho từng bản ghi thì client mới đặt `Synced`.

## Idempotency / chống trùng
- PK = UUID. Push cùng UUID + cùng nội dung -> `Duplicate` (không ghi thêm). Khác nội dung -> `Conflict` (không ghi đè, client đánh dấu `Error`, UI hiện "Lỗi").
- Server commit nhưng response mất: client vẫn `Pending`, retry nhận `Duplicate` -> `Synced`. Ngoài ra pull cũng tự xác nhận bản ghi của chính mình.

## ServerSequence / cursor không bỏ sót
- Mỗi push là 1 transaction; câu đầu tiên là `UPDATE SyncCounters SET Value=Value WHERE Id=1` => giữ X-lock đến commit.
  Hệ quả: các push bị tuần tự hóa, sequence cấp liên tục và **commit đúng thứ tự cấp**, nên reader thấy sequence N thì mọi sequence < N đã commit.
  (Không dùng IDENTITY/timestamp vì có thể commit sai thứ tự.) Đánh đổi: thông lượng ghi bị tuần tự hóa - phù hợp quy mô bài thực hành.
- Pull: `WHERE ServerSequence > cursor ORDER BY ServerSequence LIMIT n+1`, trả `NextCursor`, `HasMore`.
- Client ghi bản ghi của trang + cập nhật `SyncState.LastServerCursor` trong **cùng một `SaveChanges`** (1 transaction).

## Retry
- Lỗi mạng/5xx: bản ghi giữ `Pending`, `RetryCount++`, `LastSyncError`; vòng lặp nền dùng backoff `min(2·2^(n-1), 60)` giây; nút "Đồng bộ ngay" chạy tức thì.
- `SemaphoreSlim(1,1)` ngăn hai tiến trình đồng bộ chạy chồng nhau trên cùng một máy.

## Bảo mật
- Local Service bắt buộc bind loopback (từ chối khởi động nếu `Local:ListenUrl` không phải loopback), CORS chỉ cho `http://localhost:4200`.
- Local -> Central: header `X-Api-Key` (so sánh thời gian cố định). Khóa và chuỗi kết nối qua biến môi trường, không log khóa.
- HTTPS: đặt `Urls=https://...` + chứng chỉ cho Central API và `ServerUrl=https://...` ở client khi triển khai thật.

## Giới hạn đã biết
- API key dùng chung cho cả hai máy (không phải khóa riêng từng máy / JWT).
- Bản ghi bị `Error` (Conflict/Invalid) không tự retry; cần xử lý thủ công (ngoài phạm vi v1).
- Phiếu cân bất biến: không sửa/xóa.
- Đồng hồ máy client quyết định `WeighedAt`; không có đồng bộ thời gian giữa các máy.
