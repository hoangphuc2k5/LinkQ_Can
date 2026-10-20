/** Địa chỉ Local Client Service trên CHÍNH máy này (không phải server trung tâm). */
import { environment } from '../environments/environment';

export const LOCAL_API_URL = environment.localApiUrl;
export const STATUS_POLL_MS = 3000;
