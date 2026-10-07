import { request, displayState, preferredAdapter, canEnable } from './client.js';
const byId = id => document.getElementById(id);
let token = localStorage.getItem('hayase-bindvpn-token') || '';
let status;
let busy = false;
let paired = false;
function failure(error) {
  byId('error').textContent = error.message;
  byId('error').hidden = false;
  byId('state').textContent = 'Status unavailable';
  byId('detail').textContent = 'Protection cannot be confirmed from the plugin.';
  byId('status-dot').dataset.state = 'error';
  status = undefined;
  byId('binding-toggle').checked = false;
  byId('switch-detail').textContent = 'Status unavailable';
  if (!paired || error.status === 401 || error.status === 403 || error.status === 409 || error.status === 0) {
    paired = false; byId('pair-panel').hidden = false; byId('controls').hidden = true;
  }
}
function render(data) {
  if (!Array.isArray(data.adapters)) throw new Error('The helper returned an invalid adapter list.');
  status = data;
  byId('pair-panel').hidden = true; byId('controls').hidden = false; byId('error').hidden = true;
  byId('state').textContent = displayState(data);
  byId('detail').textContent = data.message;
  byId('status-dot').dataset.state = data.state;
  byId('binding-toggle').checked = !!data.enabled;
  byId('switch-detail').textContent = data.enabled ? 'On' : 'Off';
  byId('app-warning').hidden = data.appAvailable === true;
  byId('executable').textContent = data.executable || 'Choose Hayase.exe in the helper.';
  byId('bound-adapter').textContent = data.adapters.find(a => a.id === data.adapterId)?.name || (data.adapterId ? 'Selected adapter is missing' : 'None');
  const select = byId('adapter');
  const selected = preferredAdapter(data.adapters, data.adapterId, select.value);
  select.replaceChildren(new Option('Choose an interface…', ''));
  if (data.adapterId && !data.adapters.some(a => a.id === data.adapterId)) select.add(new Option('Bound interface is missing', data.adapterId));
  for (const a of data.adapters) select.add(new Option(`${a.name} · ${a.up ? 'connected' : 'down'}`, a.id));
  select.value = selected;
  adapterDetail();
}
function adapterDetail() {
  const selected = status?.adapters.find(a => a.id === byId('adapter').value);
  byId('adapter-detail').textContent = selected ? `${selected.description}. ${selected.addresses.length ? selected.addresses.join(', ') : 'No address available.'}` : 'Select the adapter created by your VPN. This works with VPNs that expose a network interface.';
  controls();
}
function controls() {
  const selected = byId('adapter').value;
  const testRunning = !!status?.testRunning;
  byId('binding-toggle').disabled = busy || testRunning || (!status?.enabled && !canEnable(status, selected));
  byId('apply-adapter').hidden = !status?.enabled || !selected || selected === status.adapterId;
  byId('apply-adapter').disabled = busy || !canEnable(status, selected);
  byId('test').disabled = busy || testRunning || status?.appAvailable !== true || !status?.ready;
  byId('pair').disabled = busy;
  byId('refresh').disabled = busy;
  byId('adapter').disabled = busy || testRunning;
}
async function perform(operation) {
  if (busy) return;
  busy = true; controls();
  try { await operation(); }
  catch (error) { failure(error); }
  finally { busy = false; controls(); }
}
async function pair(candidate) {
  const data = await request('/v1/pair', candidate, {});
  token = candidate; paired = true;
  localStorage.setItem('hayase-bindvpn-token', token);
  byId('pair-code').value = ''; render(data);
}
byId('pair').addEventListener('click', () => perform(() => pair(byId('pair-code').value.trim().toLowerCase())));
byId('pair-code').addEventListener('keydown', event => { if (event.key === 'Enter') byId('pair').click(); });
byId('adapter').addEventListener('change', adapterDetail);
byId('refresh').addEventListener('click', () => perform(async () => render(await request('/v1/status', token))));
byId('binding-toggle').addEventListener('change', () => perform(async () => {
  const enabling = byId('binding-toggle').checked;
  render(await request(enabling ? '/v1/bind' : '/v1/unbind', token, enabling ? { adapterId: byId('adapter').value } : {}));
  byId('test-result').hidden = true;
}));
byId('apply-adapter').addEventListener('click', () => perform(async () => {
  render(await request('/v1/bind', token, { adapterId: byId('adapter').value })); byId('test-result').hidden = true;
}));
byId('test').addEventListener('click', () => perform(async () => {
  byId('test-result').hidden = false; byId('test-summary').textContent = 'Testing protection…'; byId('test-checks').replaceChildren();
  const result = await request('/v1/test', token, {});
  byId('test-summary').textContent = result.message;
  byId('test-result').dataset.passed = String(result.passed);
  for (const check of result.checks || []) {
    const item = document.createElement('li'); item.textContent = `${check.name}: ${check.state}. ${check.detail}`; byId('test-checks').append(item);
  }
  render(await request('/v1/status', token));
}));
await perform(() => pair(token));
setInterval(() => { if (paired && !busy) perform(async () => render(await request('/v1/status', token))); }, 3000);
