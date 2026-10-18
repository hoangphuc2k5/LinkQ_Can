# Chạy bằng quyền Administrator trên máy SERVER để cho phép máy client truy cập cổng 5000.
New-NetFirewallRule -DisplayName 'Weighing Central API' -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow -Profile Private,Domain
