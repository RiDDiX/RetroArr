import { beforeEach, describe, expect, it, vi } from 'vitest';
import axios, { AxiosError, AxiosHeaders, type AxiosAdapter, type InternalAxiosRequestConfig } from 'axios';

const KEY = 'RetroArr_api_key';

// client.ts keeps the key and the bootstrap promise in module state, so each test loads a fresh copy
async function load(bootstrapKey: string | null) {
  vi.resetModules();
  const get = vi.spyOn(axios, 'get').mockImplementation(async () => {
    if (!bootstrapKey) throw new Error('not loopback');
    return { data: { apiKey: bootstrapKey } };
  });
  const mod = await import('../client');
  return { ...mod, get };
}

function unauthorized(config: InternalAxiosRequestConfig) {
  return new AxiosError('Unauthorized', 'ERR_BAD_REQUEST', config, null, {
    status: 401, statusText: 'Unauthorized', data: {}, headers: {}, config,
  });
}

describe('api key storage', () => {
  beforeEach(() => localStorage.clear());

  it('round-trips through localStorage', async () => {
    const { getApiKey, setApiKey } = await load(null);
    expect(getApiKey()).toBeNull();
    setApiKey('abc');
    expect(getApiKey()).toBe('abc');
    expect(localStorage.getItem(KEY)).toBe('abc');
    setApiKey(null);
    expect(getApiKey()).toBeNull();
    expect(localStorage.getItem(KEY)).toBeNull();
  });

  it('starts from a stored key', async () => {
    localStorage.setItem(KEY, 'stored');
    const { getApiKey } = await load(null);
    expect(getApiKey()).toBe('stored');
  });
});

describe('401 interceptor', () => {
  beforeEach(() => localStorage.clear());

  it('retries once with the bootstrapped key', async () => {
    const { default: apiClient, getApiKey, setApiKey } = await load('fresh');
    setApiKey('stale');
    const seen: Array<string | undefined> = [];
    apiClient.defaults.adapter = (async (config: InternalAxiosRequestConfig) => {
      const sent = AxiosHeaders.from(config.headers).get('X-Api-Key') as string | undefined;
      seen.push(sent);
      if (sent !== 'fresh') {
        setApiKey(null); // the server no longer knows the key
        throw unauthorized(config);
      }
      return { data: 'ok', status: 200, statusText: 'OK', headers: {}, config };
    }) as AxiosAdapter;

    const resp = await apiClient.get('/x');

    expect(resp.data).toBe('ok');
    expect(seen).toEqual(['stale', 'fresh']);
    expect(getApiKey()).toBe('fresh');
  });

  it('gives up when no key can be bootstrapped, after one attempt', async () => {
    const { default: apiClient } = await load(null);
    let calls = 0;
    apiClient.defaults.adapter = (async (config: InternalAxiosRequestConfig) => {
      calls++;
      throw unauthorized(config);
    }) as AxiosAdapter;

    await expect(apiClient.get('/x')).rejects.toMatchObject({ response: { status: 401 } });
    expect(calls).toBe(1);
  });

  it('does not loop when the retry is refused too', async () => {
    const { default: apiClient } = await load('fresh');
    let calls = 0;
    apiClient.defaults.adapter = (async (config: InternalAxiosRequestConfig) => {
      calls++;
      throw unauthorized(config);
    }) as AxiosAdapter;

    await expect(apiClient.get('/x')).rejects.toMatchObject({ response: { status: 401 } });
    expect(calls).toBe(2);
  });
});
