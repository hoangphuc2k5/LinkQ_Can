export type Direction = 'In' | 'Out';
export type CargoStatus = 'Loaded' | 'Empty';
export type SyncStatus = 'Pending' | 'Synced' | 'Error';

export interface ApiResponse<T> { success: boolean; data: T | null; error: { code: string; message: string } | null; }

export interface CreateWeighing { licensePlate: string; firstWeightKg: number; direction: Direction; cargoStatus: CargoStatus; }

export interface LocalWeighing {
  id: string; licensePlate: string; firstWeightKg: number; secondWeightKg: number | null; cargoWeightKg: number | null;
  direction: Direction; cargoStatus: CargoStatus;
  weighedAt: string; clientId: string; syncStatus: SyncStatus; syncedAt: string | null; lastSyncError: string | null; retryCount: number;
}

export interface PagedResult<T> { items: T[]; total: number; page: number; pageSize: number; }

/** Lịch sử: source = 'server' (đầy đủ mọi trạm) hoặc 'local' (server không khả dụng, chỉ phiếu của máy này). */
export interface HistoryResult extends PagedResult<LocalWeighing> { source: 'server' | 'local'; warning: string | null; }

export interface SyncStatusInfo {
  clientId: string; serverOnline: boolean | null; isSyncing: boolean; pendingCount: number; errorCount: number;
  lastSuccessfulSyncAt: string | null; lastError: string | null; nextRetryAt: string | null; consecutiveFailures: number;
}

export interface SyncNowResult { outcome: string; error: string | null; pushed: number; pulled: number; }
