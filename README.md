# Hệ thống cân xe Client–Server (Offline-First, đồng bộ hai chiều)

Angular (mỗi máy) → Local Client Service + SQLite (mỗi máy) ⇄ Central API + SQL Server (máy chủ).
Chi tiết kỹ thuật: [docs/architecture.md](docs/architecture.md).

> **Trạng thái kiểm chứng:** mã nguồn được viết trong môi trường **không có .NET SDK, không có mạng** nên **chưa từng được compile hay chạy test**.
> Hãy làm bước "Build & test" bên dưới trước tiên; nếu có lỗi compile, gửi lại log để sửa.

## 1. Yêu cầu
- Cả 3 máy (Server, A, B): **.NET 8 SDK**. Máy A, B: **Node.js 20+**. Máy Server: **SQL Server** (Express/Developer) hoặc truy cập được tới một instance.
- Windows PowerShell. Trong thư mục dự án chạy `powershell -ExecutionPolicy Bypass -File scripts\setup.ps1` (restore NuGet + `npm install`).

## 2. Build & test (làm đầu tiên)
```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```
Chạy `dotnet build`, `dotnet test` (test dùng SQLite cho Central API, **không cần SQL Server**), rồi `ng build`.

## 3. Máy Server
1. Tạo DB: không cần làm tay — Central API tự chạy migration khi khởi động (`Database:AutoMigrate=true`), hoặc chạy `database/01-create-sqlserver.sql` trong SSMS.
2. Sinh API key (≥16 ký tự), ví dụ: `[guid]::NewGuid().ToString('N')`.
3. Mở cổng: `scripts\open-firewall.ps1` (Admin).
4. Chạy:
```powershell
scripts\run-server.ps1 -ApiKey "<API_KEY>" -ConnectionString "Server=localhost\SQLEXPRESS;Database=WeighingDb;Trusted_Connection=True;TrustServerCertificate=True"
```
Kiểm tra: `http://<IP-SERVER>:5000/api/health` → `{"success":true,...}`.
Nếu dùng SQL login, truyền mật khẩu qua `-ConnectionString` lúc chạy, **không** ghi vào file.

## 4. Máy A và máy B (mỗi máy 2 cửa sổ PowerShell)
```powershell
# Cửa sổ 1 - Local Client Service  (máy B thì đổi thành 'Machine B')
scripts\run-local.ps1 -ClientId "Machine A" -ServerUrl "http://<IP-SERVER>:5000" -ApiKey "<API_KEY>"
# Cửa sổ 2 - Giao diện
scripts\run-frontend.ps1      # mở http://localhost:4200
```
SQLite nằm ở `%LOCALAPPDATA%\WeighingSystem\weighing-local.db` (bền vững qua các lần khởi động). ClientId gắn với file DB; đổi ClientId trên DB cũ sẽ bị từ chối.
Nếu Local Service dùng cổng khác 5100, sửa `src/app/config.ts`.

## 5. Kiểm tra LAN
Trên máy A/B: `Test-NetConnection <IP-SERVER> -Port 5000` → `TcpTestSucceeded : True`, hoặc mở `http://<IP-SERVER>:5000/api/health`.

## 6. Kịch bản demo trên 2 máy
1. **A→B:** máy A lưu phiếu → trong ≤10 giây (hoặc bấm *Đồng bộ ngay*) máy B tự thấy phiếu (cột "Máy" = Machine A).
2. **B→A:** làm ngược lại.
3. **Mất mạng:** rút LAN/tắt Wi-Fi máy A (hoặc dừng Central API). Nhập 5 phiếu → thấy banner "Mất kết nối server...", 5 phiếu trạng thái *Chờ đồng bộ*, "Chưa đồng bộ: 5".
4. **Có mạng lại:** cắm lại → tự đồng bộ (backoff tối đa 60 giây, hoặc bấm *Đồng bộ ngay*); 5 phiếu thành *Đã đồng bộ* và xuất hiện trên máy B.
5. **Cả hai offline:** A nhập 3, B nhập 4 → nối lại → SQL Server đủ 7 bản ghi.
6. **Khởi động lại:** khi còn phiếu *Chờ đồng bộ*, tắt Local Service + trình duyệt, mở lại → dữ liệu còn, tiếp tục đồng bộ.

## 7. Kiểm tra dữ liệu
```sql
-- SQL Server
SELECT ServerSequence, LicensePlate, WeightKg, ClientId, WeighedAt FROM WeighingRecords ORDER BY ServerSequence;
SELECT COUNT(*) AS Total, COUNT(DISTINCT Id) AS DistinctIds FROM WeighingRecords;   -- phải bằng nhau
```
```powershell
# SQLite (cần sqlite3.exe)
sqlite3 "$env:LOCALAPPDATA\WeighingSystem\weighing-local.db" "SELECT LicensePlate,SyncStatus,RetryCount,LastSyncError FROM WeighingRecords; SELECT * FROM SyncState;"
```
(`SyncStatus`: 0 = Chờ, 1 = Đã đồng bộ, 2 = Lỗi.)

## 8. Cấu trúc
```
backend/Weighing.Shared      DTO, enum, validator dùng chung
backend/Weighing.Server.Api  Central API (Controllers, Services/SyncService, Data/EF + migration, Auth)
backend/Weighing.Local.Api   Local Service (Controllers, Services, Sync/SyncEngine + BackgroundService, Data/SQLite + migration)
backend/Weighing.Tests       unit + integration test (Test 1–9 trong SyncScenarioTests.cs)
frontend/weighing-angular    Angular 18 standalone, signals, reactive forms
database/  scripts/  docs/
```
Migration được viết tay, trung lập provider, **chưa có model snapshot**. Nếu sau này đổi model, tạo lại baseline:
xoá thư mục `Data/Migrations` của project tương ứng rồi `dotnet ef migrations add InitialCreate -o Data/Migrations` (cần `dotnet tool install -g dotnet-ef`).

## 9. Cấu hình (biến môi trường)
`Auth__ApiKey`, `ConnectionStrings__Default`, `Urls` (server) · `Local__ClientId`, `Local__ServerUrl`, `Local__ServerApiKey`, `Local__DatabasePath`, `Local__SyncIntervalSeconds` (client).
