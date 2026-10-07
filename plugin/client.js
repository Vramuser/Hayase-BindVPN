export const HELPER_URL = 'http://127.0.0.1:49736';
export class HelperError extends Error {
  constructor(message, status = 0) { super(message); this.status = status; }
}
export function pluginClientId() {
  const runtimeId = globalThis.chrome?.runtime?.id;
  if (/^[a-p]{32}$/.test(runtimeId || '')) return runtimeId;
  const location = globalThis.location;
  if (location?.protocol === 'chrome-extension:' && /^[a-p]{32}$/.test(location.hostname)) return location.hostname;
  return '';
}
export async function request(path, token, body, fetcher = fetch, clientId = pluginClientId()) {
  if (!/^\/v1\/(pair|status|bind|unbind|test)$/.test(path)) throw new HelperError('Unsupported helper operation.');
  if (!/^[a-f0-9]{64}$/.test(token || '')) throw new HelperError('Paste the pairing code from the helper window.', 401);
  if (!/^[a-p]{32}$/.test(clientId)) throw new HelperError('Open this plugin through Hayase’s Plugins settings.', 403);
  let response;
  try {
    response = await fetcher(HELPER_URL + path, {
      method: body === undefined ? 'GET' : 'POST',
      headers: { Authorization: `Bearer ${token}`, 'X-Hayase-BindVPN-Client': clientId, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
      cache: 'no-store', credentials: 'omit', redirect: 'error', signal: AbortSignal.timeout(path === '/v1/test' ? 60000 : 7000)
    });
  } catch {
    throw new HelperError('The helper is unavailable. Start it or open it from the system tray. A saved binding stays blocked while the helper is stopped.');
  }
  const data = await response.json().catch(() => { throw new HelperError('The helper returned an invalid response.'); });
  if (!response.ok) throw new HelperError(data.error || 'The helper could not complete this request.', response.status);
  return data;
}
export function displayState(status) {
  if (status.appAvailable !== true) return 'Hayase not found';
  if (status.state === 'error') return 'Binding needs attention';
  if (status.ready && status.blocked && status.enabled) return 'Bound to interface';
  if (status.blocked) return 'Hayase is blocked';
  return 'Binding is off';
}
export function canEnable(status, adapterId) {
  return !!(status?.appAvailable === true && !status.testRunning && status.adapters?.some(a => a.id === adapterId));
}
export function preferredAdapter(adapters, boundId, currentId) {
  if (currentId && adapters.some(a => a.id === currentId)) return currentId;
  // Never substitute another adapter when the pinned one goes missing.
  if (boundId) return boundId;
  return adapters.find(a => a.suggested)?.id || '';
}
