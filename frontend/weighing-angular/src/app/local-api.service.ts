import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LOCAL_API_URL } from './config';
import { ApiResponse, CreateWeighing, HistoryResult, LocalWeighing, SyncNowResult, SyncStatusInfo } from './models';

export function errorMessage(e: unknown): string {
  if (e instanceof HttpErrorResponse) {
    if (e.status === 0) return 'Không kết nối được dịch vụ cục bộ (Local Client Service). Hãy kiểm tra dịch vụ đang chạy.';
    return (e.error as ApiResponse<unknown> | null)?.error?.message ?? `Lỗi ${e.status}`;
  }
  return e instanceof Error ? e.message : 'Lỗi không xác định';
}

@Injectable({ providedIn: 'root' })
export class LocalApiService {
  private readonly http = inject(HttpClient);

  async create(req: CreateWeighing): Promise<LocalWeighing> {
    const r = await firstValueFrom(this.http.post<ApiResponse<LocalWeighing>>(`${LOCAL_API_URL}/weighing`, req));
    return r.data!;
  }

  async list(opts: { search: string; from?: string; to?: string; page: number; pageSize: number }): Promise<HistoryResult> {
    let params = new HttpParams().set('page', opts.page).set('pageSize', opts.pageSize);
    if (opts.search) params = params.set('search', opts.search);
    if (opts.from) params = params.set('from', opts.from);
    if (opts.to) params = params.set('to', opts.to);
    const r = await firstValueFrom(this.http.get<ApiResponse<HistoryResult>>(`${LOCAL_API_URL}/weighing`, { params }));
    return r.data!;
  }

  async status(): Promise<SyncStatusInfo> {
    const r = await firstValueFrom(this.http.get<ApiResponse<SyncStatusInfo>>(`${LOCAL_API_URL}/sync/status`));
    return r.data!;
  }

  async syncNow(): Promise<SyncNowResult> {
    const r = await firstValueFrom(this.http.post<ApiResponse<SyncNowResult>>(`${LOCAL_API_URL}/sync/now`, {}));
    return r.data!;
  }
}
