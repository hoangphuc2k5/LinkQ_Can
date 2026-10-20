import { Component, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { errorMessage } from './local-api.service';
import { WeighingStore } from './weighing.store';
import { CargoStatus, Direction } from './models';

/** "12.345,5" -> 12345.5 */
export function parseWeight(text: string): number { return Number(text.replace(/\./g, '').replace(',', '.')); }
/** Định dạng hàng nghìn kiểu vi-VN khi gõ: dấu '.' phân cách nghìn, ',' là dấu thập phân (tối đa 3 số). */
export function formatWeight(text: string): string {
  const clean = text.replace(/[^\d,]/g, '');
  const [int = '', ...rest] = clean.split(',');
  const intFmt = int.replace(/^0+(?=\d)/, '').replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  return rest.length ? `${intFmt},${rest.join('').slice(0, 3)}` : intFmt;
}
function weightValidator(c: AbstractControl): ValidationErrors | null {
  const n = parseWeight(String(c.value ?? ''));
  return Number.isFinite(n) && n > 0 && n <= 1_000_000 ? null : { weight: true };
}

@Component({
  selector: 'app-weighing-form',
  standalone: true,
  imports: [ReactiveFormsModule, DatePipe],
  template: `
  <section class="card">
    <h2>Nhập phiếu cân</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" autocomplete="off">
      <label class="f" for="plate">Biển số xe *</label>
      <input #plate id="plate" type="text" formControlName="licensePlate" maxlength="20" placeholder="VD: 51A-123.45"
             [class.invalid]="invalid('licensePlate')" (input)="onPlate($event)">
      @if (invalid('licensePlate')) { <div class="err">Vui lòng nhập biển số xe (tối đa 20 ký tự, chỉ chữ/số/.-).</div> }

      <label class="f" for="weight">Cân lần 1 (xe + hàng) *</label>
      <div class="weight">
        <input id="weight" type="text" inputmode="decimal" formControlName="weightText" placeholder="0"
               [class.invalid]="invalid('weightText')" (input)="onWeight($event)">
        <span>kg</span>
      </div>
      @if (invalid('weightText')) { <div class="err">Cân lần 1 phải là số lớn hơn 0 (kg).</div> }
      <div class="muted">Cân lần 2 (xe sau khi dỡ hàng) nhập sau, trong bảng lịch sử.</div>

      <label class="f">Loại lượt cân</label>
      <div class="radios">
        <label><input type="radio" formControlName="direction" value="In"> Xe vào</label>
        <label><input type="radio" formControlName="direction" value="Out"> Xe ra</label>
      </div>

      <label class="f">Tình trạng hàng</label>
      <div class="radios">
        <label><input type="radio" formControlName="cargoStatus" value="Loaded"> Có hàng</label>
        <label><input type="radio" formControlName="cargoStatus" value="Empty"> Không có hàng</label>
      </div>

      <label class="f">Thời gian cân (tự động)</label>
      <div class="muted">{{ now() | date: 'dd/MM/yyyy HH:mm:ss' }}</div>

      @if (error()) { <div class="err" style="margin-top:8px">{{ error() }} (dữ liệu đã nhập được giữ nguyên)</div> }

      <div class="btns">
        <button class="btn pri" type="submit" [disabled]="saving()">{{ saving() ? 'Đang lưu...' : 'Lưu phiếu cân' }}</button>
        <button class="btn" type="button" (click)="reset()">Làm mới</button>
      </div>
    </form>
  </section>`,
})
export class WeighingFormComponent {
  private readonly store = inject(WeighingStore);
  private readonly plateEl = viewChild.required<ElementRef<HTMLInputElement>>('plate');

  readonly now = signal(new Date());
  readonly saving = signal(false);
  readonly error = signal('');

  readonly form = new FormGroup({
    licensePlate: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(20), Validators.pattern(/^[A-Za-z0-9 .\-]+$/)] }),
    weightText: new FormControl('', { nonNullable: true, validators: [Validators.required, weightValidator] }),
    direction: new FormControl<Direction>('In', { nonNullable: true }),
    cargoStatus: new FormControl<CargoStatus>('Loaded', { nonNullable: true }),
  });

  constructor() {
    const t = setInterval(() => this.now.set(new Date()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  invalid(name: 'licensePlate' | 'weightText'): boolean { const c = this.form.controls[name]; return c.invalid && (c.touched || c.dirty); }
  onPlate(e: Event): void { const el = e.target as HTMLInputElement; this.form.controls.licensePlate.setValue(el.value.toUpperCase(), { emitEvent: false }); el.value = el.value.toUpperCase(); }
  onWeight(e: Event): void { const el = e.target as HTMLInputElement; const f = formatWeight(el.value); el.value = f; this.form.controls.weightText.setValue(f); }

  reset(): void { this.form.reset({ licensePlate: '', weightText: '', direction: 'In', cargoStatus: 'Loaded' }); this.error.set(''); this.plateEl().nativeElement.focus(); }

  async submit(): Promise<void> {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const v = this.form.getRawValue();
    this.saving.set(true); this.error.set('');
    try {
      await this.store.create({ licensePlate: v.licensePlate.trim().toUpperCase(), firstWeightKg: parseWeight(v.weightText), direction: v.direction, cargoStatus: v.cargoStatus });
      this.form.patchValue({ licensePlate: '', weightText: '' });
      this.form.markAsUntouched(); this.form.markAsPristine();
      this.plateEl().nativeElement.focus();
    } catch (e) {
      this.error.set(errorMessage(e)); // KHÔNG reset form khi lỗi
    } finally {
      this.saving.set(false);
    }
  }
}
