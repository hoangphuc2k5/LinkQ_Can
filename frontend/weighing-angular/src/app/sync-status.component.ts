import { Component, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { WeighingStore } from './weighing.store';

@Component({
  selector: 'app-sync-status',
  standalone: true,
  imports: [DatePipe],
  template: `
  <section class="card">
    <h2>Trạng thái đồng bộ</h2>

    @if (!store.localServiceUp()) {
      <div class="banner err">Không kết nối được dịch vụ cục bộ trên máy này. Hãy khởi động Local Client Service.</div>
    } @else if (store.serverOffline()) {
      <div class="banner warn">Mất kết nối server. Phiếu cân vẫn được lưu trên máy và sẽ tự đồng bộ khi có mạng.</div>
    }

    @if (store.status(); as s) {
      <div class="stat"><span>Máy</span><b>{{ s.clientId }}</b></div>
      <div class="stat"><span>Server</span>
        @if (s.serverOnline === true) { <span class="badge b-ok">Server Online</span> }
        @else if (s.serverOnline === false) { <span class="badge b-warn">Server Offline</span> }
        @else { <span class="badge b-gray">Đang kiểm tra...</span> }
      </div>
      <div class="stat"><span>Bản ghi chưa đồng bộ</span><b>{{ s.pendingCount }}</b></div>
      @if (s.errorCount > 0) { <div class="stat"><span>Bản ghi lỗi / xung đột</span><b style="color:var(--err)">{{ s.errorCount }}</b></div> }
      <div class="stat"><span>Lần đồng bộ gần nhất</span>
        <b>{{ s.lastSuccessfulSyncAt ? (s.lastSuccessfulSyncAt | date: 'dd/MM/yyyy HH:mm:ss') : 'Chưa đồng bộ' }}</b></div>
      <div class="stat"><span>Tiến trình</span>
        @if (s.isSyncing || store.syncing()) { <span class="badge b-warn">Đang đồng bộ...</span> }
        @else if (s.lastError) { <span class="badge b-err">Thất bại</span> }
        @else if (s.lastSuccessfulSyncAt) { <span class="badge b-ok">Thành công</span> }
        @else { <span class="badge b-gray">Chờ</span> }
      </div>
      @if (s.lastError) { <div class="err">{{ s.lastError }}</div> }
      @if (s.consecutiveFailures > 0 && s.nextRetryAt) {
        <div class="muted" style="margin-top:6px">Thử lại lần sau: {{ s.nextRetryAt | date: 'HH:mm:ss' }} (lỗi liên tiếp: {{ s.consecutiveFailures }})</div>
      }
    }

    @if (store.syncMessage(); as m) { <div class="banner" [class.ok]="m.ok" [class.err]="!m.ok" style="margin-top:10px">{{ m.text }}</div> }

    <div class="btns">
      <button class="btn pri" type="button" (click)="store.syncNow()" [disabled]="store.syncing() || !store.localServiceUp()">Đồng bộ ngay</button>
    </div>
  </section>`,
})
export class SyncStatusComponent { readonly store = inject(WeighingStore); }
