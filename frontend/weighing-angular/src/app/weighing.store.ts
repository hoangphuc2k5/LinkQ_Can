import { Injectable, computed, inject, signal } from '@angular/core';
import { LocalApiService, errorMessage } from './local-api.service';
import { CreateWeighing, LocalWeighing, SyncStatusInfo } from './models';
import { STATUS_POLL_MS } from './config';

export interface Toast { kind: 'ok' | 'warn' | 'err'; text: string; }

@Injectable({ providedIn: 'root' })
export class WeighingStore {
  private readonly api = inject(LocalApiService);

  readonly status = signal<SyncStatusInfo | null>(null);
  readonly localServiceUp = signal(true);
  readonly records = signal<LocalWeighing[]>([]);
  readonly total = signal(0);
  /** 'server' = lịch sử đầy đủ từ server; 'local' = server không khả dụng, chỉ có phiếu của máy này. */
  readonly historySource = signal<'server' | 'local'>('server');
  readonly historyWarning = signal<string | null>(null);
  readonly page = signal(1);
  readonly pageSize = 15;
  readonly search = signal('');
  readonly date = signal('');
  readonly loading = signal(false);
  readonly syncing = signal(false);
  readonly toast = signal<Toast | null>(null);
  readonly syncMessage = signal<{ ok: boolean; text: string } | null>(null);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));
  readonly serverOffline = computed(() => this.localServiceUp() && this.status()?.serverOnline === false);

  private toastTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    void this.refreshStatus();
    void this.loadHistory();
    setInterval(() => void this.refreshStatus(), STATUS_POLL_MS);
  }

  async refreshStatus(): Promise<void> {
    try {
      const prev = this.status();
      const s = await this.api.status();
      this.localServiceUp.set(true);
      this.status.set(s);
      // Nạp lại lịch sử khi: số phiếu chờ đổi, trạng thái server đổi (online/offline), hoặc đang xem nguồn 'local'.
      const serverChanged = !prev || prev.serverOnline !== s.serverOnline;
      if (!prev || prev.pendingCount !== s.pendingCount || serverChanged || this.historySource() === 'local') {
        await this.loadHistory();
      }
    } catch {
      this.localServiceUp.set(false);
    }
  }

  async loadHistory(): Promise<void> {
    this.loading.set(true);
    try {
      const d = this.date();
      let from: string | undefined, to: string | undefined;
      if (d) {
        const start = new Date(`${d}T00:00:00`);
        const end = new Date(start); end.setDate(end.getDate() + 1);
        from = start.toISOString(); to = end.toISOString();
      }
      const r = await this.api.list({ search: this.search(), from, to, page: this.page(), pageSize: this.pageSize });
      this.records.set(r.items); this.total.set(r.total);
      this.historySource.set(r.source); this.historyWarning.set(r.warning);
    } catch (e) {
      this.showToast('err', errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  setFilters(search: string, date: string): void { this.search.set(search); this.date.set(date); this.page.set(1); void this.loadHistory(); }
  goToPage(p: number): void { this.page.set(Math.min(Math.max(1, p), this.totalPages())); void this.loadHistory(); }

  /**
   * Ném lỗi nếu thất bại để form giữ nguyên dữ liệu đã nhập.
   * Chỉ chờ bước ghi SQLite cục bộ; việc tải lại lịch sử/trạng thái (có thể chờ server tới timeout) chạy nền
   * để nút "Đang lưu..." không bị kẹt khi server offline.
   */
  async create(req: CreateWeighing): Promise<void> {
    await this.api.create(req);
    this.showToast(this.serverOffline() ? 'warn' : 'ok',
      this.serverOffline() ? 'Đã lưu trên máy. Dữ liệu sẽ tự đồng bộ khi có mạng.' : 'Đã lưu phiếu cân. Đang đồng bộ lên server...');
    this.page.set(1);
    void this.loadHistory();
    void this.refreshStatus();
  }

  async addSecondWeigh(id: string, secondWeightKg: number): Promise<void> {
    await this.api.addSecond(id, secondWeightKg);
    this.showToast('ok', 'Đã lưu cân lần 2. Trọng lượng hàng = cân lần 1 − cân lần 2.');
    this.page.set(1);
    void this.loadHistory();
    void this.refreshStatus();
  }

  async syncNow(): Promise<void> {
    this.syncing.set(true); this.syncMessage.set(null);
    try {
      const r = await this.api.syncNow();
      if (r.outcome === 'Success') this.syncMessage.set({ ok: true, text: `Đồng bộ thành công (gửi ${r.pushed}, nhận ${r.pulled}).` });
      else if (r.outcome === 'AlreadyRunning') this.syncMessage.set({ ok: true, text: 'Đang có một tiến trình đồng bộ chạy.' });
      else this.syncMessage.set({ ok: false, text: `Đồng bộ thất bại: ${r.error ?? r.outcome}` });
    } catch (e) {
      this.syncMessage.set({ ok: false, text: errorMessage(e) });
    } finally {
      this.syncing.set(false);
      await this.refreshStatus(); await this.loadHistory();
    }
  }

  showToast(kind: Toast['kind'], text: string): void {
    this.toast.set({ kind, text });
    clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => this.toast.set(null), 5000);
  }
}
