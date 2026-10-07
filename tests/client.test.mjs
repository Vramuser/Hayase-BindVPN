import test from 'node:test';
import assert from 'node:assert/strict';
import { request, displayState, preferredAdapter, canEnable, HelperError, HELPER_URL } from '../plugin/client.js';
const token = 'a'.repeat(64);
const clientId = 'b'.repeat(32);
test('binding uses a fixed localhost endpoint and puts the secret only in an authorization header', async () => {
  let observed;
  const result = await request('/v1/bind', token, { adapterId: 'chosen-vpn' }, async (url, options) => {
    observed = { url, options }; return { ok: true, json: async () => ({ state: 'bound' }) };
  }, clientId);
  assert.equal(result.state, 'bound');
  assert.equal(observed.url, HELPER_URL + '/v1/bind');
  assert.equal(observed.options.headers.Authorization, `Bearer ${token}`);
  assert.equal(observed.options.headers['X-Hayase-BindVPN-Client'], clientId);
  assert.deepEqual(JSON.parse(observed.options.body), { adapterId: 'chosen-vpn' });
  assert.equal(observed.options.credentials, 'omit');
  assert.equal(observed.options.redirect, 'error');
});
test('a missing bound adapter never switches to another VPN or Ethernet', () => {
  const adapters = [{ id: 'other-vpn', suggested: true }, { id: 'ethernet' }];
  assert.equal(preferredAdapter(adapters, 'missing-vpn', ''), 'missing-vpn');
  assert.equal(preferredAdapter(adapters, '', ''), 'other-vpn');
  assert.equal(preferredAdapter(adapters, '', 'ethernet'), 'ethernet');
});
test('binding is not shown as active when the block or enabled flag is missing', () => {
  assert.equal(displayState({ appAvailable: true, ready: true, blocked: false, enabled: true }), 'Binding is off');
  assert.equal(displayState({ appAvailable: true, ready: true, blocked: true, enabled: false }), 'Hayase is blocked');
  assert.equal(displayState({ appAvailable: true, ready: true, blocked: true, enabled: true }), 'Bound to interface');
  assert.equal(displayState({ appAvailable: true, state: 'error', blocked: true }), 'Binding needs attention');
  assert.equal(displayState({ appAvailable: false, ready: true, blocked: true, enabled: true }), 'Hayase not found');
});
test('a nonempty stale application path cannot enable the switch', () => {
  const data = { executable: 'C:/missing/Hayase.exe', appAvailable: false, adapters: [{ id: 'vpn' }] };
  assert.equal(canEnable(data, 'vpn'), false);
  assert.equal(canEnable({ ...data, appAvailable: true }, 'vpn'), true);
  assert.equal(canEnable({ ...data, appAvailable: true, testRunning: true }, 'vpn'), false);
  assert.equal(canEnable({ ...data, appAvailable: true }, 'missing'), false);
});
test('helper failure is explicit and cannot masquerade as connected', async () => {
  await assert.rejects(request('/v1/status', token, undefined, async () => { throw new Error('offline'); }, clientId), /helper is unavailable/);
  await assert.rejects(request('/v1/status', token, undefined, async () => ({ ok: false, status: 401, json: async () => ({ error: 'Re-pair' }) }), clientId), error => error instanceof HelperError && error.status === 401 && error.message === 'Re-pair');
});
test('invalid secrets and paths never reach the network', async () => {
  const fetcher = () => { throw new Error('must not fetch'); };
  await assert.rejects(request('/v1/status', 'bad', undefined, fetcher), /pairing code/);
  await assert.rejects(request('http://example.com', token, undefined, fetcher), /Unsupported/);
  await assert.rejects(request('/v1/pair', token, {}, fetcher, ''), /Plugins settings/);
});
