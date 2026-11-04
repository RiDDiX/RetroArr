import { describe, expect, it } from 'vitest';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { getErrorMessage, isConflict } from '../client';

function axiosError(status: number, data: unknown, message = 'Request failed with status code ' + status) {
  const config = { headers: {} } as InternalAxiosRequestConfig;
  return new AxiosError(message, 'ERR_BAD_REQUEST', config, null, { status, statusText: '', data, headers: {}, config });
}

describe('getErrorMessage', () => {
  it('prefers data.message, then data.error', () => {
    expect(getErrorMessage(axiosError(400, { message: 'm', error: 'e' }))).toBe('m');
    expect(getErrorMessage(axiosError(400, { error: 'e' }))).toBe('e');
  });

  it('joins validation errors', () => {
    expect(getErrorMessage(axiosError(400, { errors: { Name: ['Required.'], Port: ['Bad.'] } }))).toBe('Required. Bad.');
  });

  it('falls back to a string body, then the axios message', () => {
    expect(getErrorMessage(axiosError(500, 'boom'))).toBe('boom');
    expect(getErrorMessage(axiosError(500, {}))).toBe('Request failed with status code 500');
  });

  it('handles plain errors and unknown values', () => {
    expect(getErrorMessage(new Error('plain'))).toBe('plain');
    expect(getErrorMessage('x')).toBe('Request failed');
    expect(getErrorMessage(null, 'custom')).toBe('custom');
  });
});

describe('isConflict', () => {
  it('is true for 409 only', () => {
    expect(isConflict(axiosError(409, {}))).toBe(true);
    expect(isConflict(axiosError(400, {}))).toBe(false);
    expect(isConflict(new Error('409'))).toBe(false);
  });
});
