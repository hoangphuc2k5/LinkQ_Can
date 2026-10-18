import { Component, inject } from '@angular/core';
import { HistoryTableComponent } from './history-table.component';
import { SyncStatusComponent } from './sync-status.component';
import { WeighingFormComponent } from './weighing-form.component';
import { WeighingStore } from './weighing.store';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [WeighingFormComponent, SyncStatusComponent, HistoryTableComponent],
  template: `
  <div class="page">
    <div class="top">
      <h1>Phần mềm cân xe</h1>
      <span class="machine">{{ store.status()?.clientId ?? 'Đang kết nối...' }}</span>
    </div>
    <div class="grid">
      <app-weighing-form />
      <app-sync-status />
    </div>
    <app-history-table />
    @if (store.toast(); as t) { <div class="toast" [class]="'toast ' + t.kind">{{ t.text }}</div> }
  </div>`,
})
export class AppComponent { readonly store = inject(WeighingStore); }
