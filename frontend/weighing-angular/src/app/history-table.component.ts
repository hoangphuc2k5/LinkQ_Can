import { Component, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { WeighingStore } from './weighing.store';
import { LocalWeighing } from './models';

@Component({
  selector: 'app-history-table',
  standalone: true,
  imports: [DatePipe, DecimalPipe],
  template: `
  <section class="card">
    <h2>Lịch sử cân</h2>
    <div class="filters">
      <input type="text" placeholder="Tìm theo biển số..." #q [value]="store.search()" (input)="onFilter(q.value, d.value)">
      <input type="date" #d [value]="store.date()" (input)="onFilter(q.value, d.value)">
      <button class="btn" type="button" (click)="q.value=''; d.value=''; onFilter('', '')">Xóa lọc</button>
    </div>
    @if (store.historySource() === 'local') {
      <div class="banner warn">{{ store.historyWarning() }}</div>
    }
    <div class="tablewrap">
      <table>
        <thead><tr><th>Thời gian cân</th><th>Biển số</th><th>Trọng lượng</th><th>Lượt cân</th><th>Hàng</th><th>Máy</th><th>Đồng bộ</th></tr></thead>
        <tbody>
          @for (r of store.records(); track r.id) {
            <tr>
              <td>{{ r.weighedAt | date: 'dd/MM/yyyy HH:mm:ss' }}</td>
              <td><b>{{ r.licensePlate }}</b></td>
              <td class="num">{{ r.weightKg | number: '1.0-3' }} kg</td>
              <td>{{ r.direction === 'In' ? 'Xe vào' : 'Xe ra' }}</td>
              <td>{{ r.cargoStatus === 'Loaded' ? 'Có hàng' : 'Không có hàng' }}</td>
              <td>{{ r.clientId }}</td>
              <td><span class="badge" [class]="badge(r)" [title]="r.lastSyncError ?? ''">{{ label(r) }}</span></td>
            </tr>
          } @empty {
            <tr><td colspan="7" class="muted">{{ store.loading() ? 'Đang tải...' : 'Chưa có phiếu cân nào.' }}</td></tr>
          }
        </tbody>
      </table>
    </div>
    <div class="pager">
      <span class="muted">Tổng: {{ store.total() }} phiếu</span>
      <span>
        <button class="btn" type="button" (click)="store.goToPage(store.page() - 1)" [disabled]="store.page() <= 1">‹ Trước</button>
        Trang {{ store.page() }} / {{ store.totalPages() }}
        <button class="btn" type="button" (click)="store.goToPage(store.page() + 1)" [disabled]="store.page() >= store.totalPages()">Sau ›</button>
      </span>
    </div>
  </section>`,
})
export class HistoryTableComponent {
  readonly store = inject(WeighingStore);
  private timer: ReturnType<typeof setTimeout> | undefined;

  onFilter(search: string, date: string): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.store.setFilters(search.trim(), date), 250);
  }
  label(r: LocalWeighing): string { return r.syncStatus === 'Synced' ? 'Đã đồng bộ' : r.syncStatus === 'Pending' ? 'Chờ đồng bộ' : 'Lỗi'; }
  badge(r: LocalWeighing): string { return 'badge ' + (r.syncStatus === 'Synced' ? 'b-ok' : r.syncStatus === 'Pending' ? 'b-warn' : 'b-err'); }
}
