import test from 'node:test';
import assert from 'node:assert/strict';
import { safeInternalPath } from '../src/utils/safeRedirect.ts';

test('giữ đường dẫn nội bộ hợp lệ', () => {
  assert.equal(safeInternalPath('/assets/x'), '/assets/x');
  assert.equal(safeInternalPath('/a/123?position=p1'), '/a/123?position=p1');
});

test('chặn protocol-relative //evil.com', () => {
  assert.equal(safeInternalPath('//evil.com'), '/');
  assert.equal(safeInternalPath('///evil.com'), '/');
});

test('chặn absolute URL https://evil.com', () => {
  assert.equal(safeInternalPath('https://evil.com'), '/');
});

test('chặn javascript:alert(1)', () => {
  assert.equal(safeInternalPath('javascript:alert(1)'), '/');
});

test('chặn backslash/control characters và giá trị không phải chuỗi', () => {
  assert.equal(safeInternalPath('/\\evil.com'), '/');
  assert.equal(safeInternalPath('/assets/\u0000x'), '/');
  assert.equal(safeInternalPath(null), '/');
  assert.equal(safeInternalPath(undefined), '/');
});
