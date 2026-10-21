import { Component, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { errorMessage, LocalApiService } from './local-api.service';
import { LocalWeighing } from './models';
import { formatWeight, parseWeight } from './weighing-form.component';
import { WeighingStore } from './weighing.store';

@Component({
  selector: 'app-history-table',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule],
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
    @if (secondRow(); as row) {
      <form (ngSubmit)="submitSecond()" autocomplete="off">
        <div class="banner ok">Cân lần 2 cho xe <b>{{ row.licensePlate }}</b>. Cân lần 1: {{ row.firstWeightKg | number: '1.0-3' }} kg.</div>
        <label class="f" for="secondWeight">Cân lần 2 (xe sau khi dỡ hàng) *</label>
        <div class="weight">
          <input id="secondWeight" type="text" inputmode="decimal" [value]="secondText()" (input)="onSecond($event)" placeholder="0">
          <span>kg</span>
        </div>
        <div class="muted">Trọng lượng hàng = cân lần 1 − cân lần 2.</div>
        @if (secondError()) { <div class="err">{{ secondError() }}</div> }
        <div class="btns">
          <button class="btn pri" type="submit" [disabled]="secondSaving()">{{ secondSaving() ? 'Đang lưu...' : 'Lưu cân lần 2' }}</button>
          <button class="btn" type="button" (click)="closeSecond()">Hủy</button>
        </div>
      </form>
    }
    <div class="tablewrap">
      <table>
        <thead>
          <tr>
            <th>Thời gian cân</th><th>Biển số</th><th>Cân lần 1</th><th>Cân lần 2</th><th>TL hàng</th>
            <th>Lượt cân</th><th>Hàng</th><th>Máy</th><th>Đồng bộ</th><th></th>
          </tr>
        </thead>
        <tbody>
          @for (r of store.records(); track r.id) {
            <tr>
              <td>{{ r.weighedAt | date: 'dd/MM/yyyy HH:mm:ss' }}</td>
              <td><b>{{ r.licensePlate }}</b></td>
              <td class="num">{{ r.firstWeightKg | number: '1.0-3' }} kg</td>
              <td class="num">
                @if (r.secondWeightKg === null) { — } @else { {{ r.secondWeightKg | number: '1.0-3' }} kg }
              </td>
              <td class="num">
                @if (r.cargoWeightKg === null) { — } @else { {{ r.cargoWeightKg | number: '1.0-3' }} kg }
              </td>
              <td>{{ r.direction === 'In' ? 'Xe vào' : 'Xe ra' }}</td>
              <td>{{ r.cargoStatus === 'Loaded' ? 'Có hàng' : 'Không có hàng' }}</td>
              <td>{{ r.clientId }}</td>
              <td><span class="badge" [class]="badge(r)" [title]="r.lastSyncError ?? ''">{{ label(r) }}</span></td>
              <td>
                <div class="actions">
                  @if (canSecond(r)) { <button class="btn sm" type="button" (click)="openSecond(r)">Cân lần 2</button> }
                  <button class="btn sm" type="button" (click)="exportPdf(r)">Xuất PDF</button>
                </div>
              </td>
            </tr>
          } @empty {
            <tr><td colspan="10" class="muted">{{ store.loading() ? 'Đang tải...' : 'Chưa có phiếu cân nào.' }}</td></tr>
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
  private readonly api = inject(LocalApiService);
  private timer: ReturnType<typeof setTimeout> | undefined;

  readonly secondRow = signal<LocalWeighing | null>(null);
  readonly secondText = signal('');
  readonly secondError = signal('');
  readonly secondSaving = signal(false);

  onFilter(search: string, date: string): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.store.setFilters(search.trim(), date), 250);
  }
  label(r: LocalWeighing): string { return r.syncStatus === 'Synced' ? 'Đã đồng bộ' : r.syncStatus === 'Pending' ? 'Chờ đồng bộ' : 'Lỗi'; }
  badge(r: LocalWeighing): string { return 'badge ' + (r.syncStatus === 'Synced' ? 'b-ok' : r.syncStatus === 'Pending' ? 'b-warn' : 'b-err'); }

  canSecond(r: LocalWeighing): boolean {
    return r.secondWeightKg === null && r.clientId === this.store.status()?.clientId;
  }

  openSecond(r: LocalWeighing): void {
    this.secondRow.set(r);
    this.secondText.set('');
    this.secondError.set('');
  }

  closeSecond(): void { this.secondRow.set(null); this.secondError.set(''); }

  onSecond(e: Event): void {
    const el = e.target as HTMLInputElement;
    const text = formatWeight(el.value);
    el.value = text;
    this.secondText.set(text);
  }

  async submitSecond(): Promise<void> {
    const row = this.secondRow();
    if (!row) return;
    const n = parseWeight(this.secondText());
    if (!Number.isFinite(n) || n <= 0 || n > 1_000_000) {
      this.secondError.set('Cân lần 2 phải lớn hơn 0.');
      return;
    }
    if (n >= row.firstWeightKg) {
      this.secondError.set('Cân lần 2 phải nhỏ hơn cân lần 1.');
      return;
    }
    this.secondSaving.set(true);
    this.secondError.set('');
    try {
      await this.store.addSecondWeigh(row.id, n);
      this.secondRow.set(null);
    } catch (e) {
      this.secondError.set(errorMessage(e));
    } finally {
      this.secondSaving.set(false);
    }
  }

  async exportPdf(r: LocalWeighing): Promise<void> {
    try {
      const blob = await this.api.downloadPdf(r.id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `phieu-can-${r.licensePlate.replace(/ /g, '-')}.pdf`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      this.store.showToast('err', errorMessage(e));
    }
  }
}
