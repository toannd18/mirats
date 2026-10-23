import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from 'react';
import { Alert, Button, Empty, Input, Spin, Tag, Typography } from 'antd';
import type { InputRef } from 'antd';
import { SearchOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import apiClient from '../../services/api-client';
import { useIsMobile } from '../../hooks/useIsMobile';

const { Text } = Typography;

interface SearchItem {
  type: string;
  id: string;
  title: string;
  subtitle: string;
  url: string;
}
interface SearchGroup {
  type: string;
  label: string;
  items: SearchItem[];
}
interface SearchResponse {
  groups: SearchGroup[];
  hasMore: boolean;
  total: number;
}

const EMPTY: SearchResponse = { groups: [], hasMore: false, total: 0 };
const MIN_QUERY = 2;

/**
 * Header quick search. Desktop shows an input; mobile shows a magnifier button that opens a
 * full-screen panel. Ctrl/Cmd+K, "/" and Escape work globally; results support ↑/↓ + Enter.
 */
export default function GlobalSearch() {
  const navigate = useNavigate();
  const isMobile = useIsMobile();
  const inputRef = useRef<InputRef>(null);
  const abortRef = useRef<AbortController | null>(null);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [result, setResult] = useState<SearchResponse>(EMPTY);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [activeIndex, setActiveIndex] = useState(0);

  const flat = useMemo(
    () => result.groups.flatMap(group => group.items.map(item => ({ ...item, groupLabel: group.label }))),
    [result],
  );

  const close = useCallback(() => {
    abortRef.current?.abort();
    setOpen(false);
    setQuery('');
    setResult(EMPTY);
    setError(null);
    setActiveIndex(0);
  }, []);

  const openSearch = useCallback(() => {
    setOpen(true);
    // Wait for the overlay input to mount.
    window.setTimeout(() => inputRef.current?.focus(), 30);
  }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      const typing = target?.tagName === 'INPUT' || target?.tagName === 'TEXTAREA' || target?.isContentEditable;
      if ((event.key.toLowerCase() === 'k' && (event.ctrlKey || event.metaKey))
        || (event.key === '/' && !typing)) {
        event.preventDefault();
        openSearch();
      }
      if (event.key === 'Escape' && open) close();
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [close, open, openSearch]);

  useEffect(() => {
    if (!open) return;
    const term = query.trim();
    if (term.length < MIN_QUERY) {
      abortRef.current?.abort();
      setResult(EMPTY);
      setError(null);
      setLoading(false);
      setActiveIndex(0);
      return;
    }

    const timer = window.setTimeout(() => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;
      setLoading(true);
      setError(null);
      apiClient.get('/search', { params: { q: term }, signal: controller.signal })
        .then((res: { data?: { data?: SearchResponse } }) => {
          if (controller.signal.aborted) return;
          setResult(res.data?.data ?? EMPTY);
          setActiveIndex(0);
        })
        .catch((err: unknown) => {
          if (controller.signal.aborted) return;
          const e = err as { name?: string; response?: { status?: number } };
          if (e?.name === 'CanceledError' || e?.name === 'AbortError') return;
          setResult(EMPTY);
          setError(e?.response?.status === 403
            ? 'Bạn không có quyền tìm loại dữ liệu này.'
            : 'Không thể tìm kiếm. Vui lòng thử lại.');
        })
        .finally(() => {
          if (!controller.signal.aborted) setLoading(false);
        });
    }, 250);
    return () => window.clearTimeout(timer);
  }, [open, query]);

  useEffect(() => () => abortRef.current?.abort(), []);

  const choose = (item: SearchItem) => {
    close();
    navigate(item.url);
  };

  const onInputKeyDown = (event: ReactKeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActiveIndex(index => Math.min(index + 1, Math.max(flat.length - 1, 0)));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActiveIndex(index => Math.max(index - 1, 0));
    } else if (event.key === 'Enter' && flat[activeIndex]) {
      event.preventDefault();
      choose(flat[activeIndex]);
    }
  };

  const panel = (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Tìm kiếm nhanh"
      style={isMobile ? {
        position: 'fixed', inset: 0, zIndex: 2000, background: '#fff',
        padding: 16, overflowY: 'auto',
      } : {
        position: 'fixed', zIndex: 2000, top: 64, left: '50%', transform: 'translateX(-50%)',
        width: 'min(680px, calc(100vw - 32px))', maxHeight: 'calc(100vh - 96px)',
        overflow: 'hidden', background: '#fff', borderRadius: 12,
        boxShadow: '0 12px 40px rgba(15, 23, 42, 0.18)', border: '1px solid #e5e7eb',
      }}
    >
      <div style={{ display: 'flex', gap: 8, alignItems: 'center', padding: 12 }}>
        <Input
          ref={inputRef}
          value={query}
          onChange={e => setQuery(e.target.value)}
          onKeyDown={onInputKeyDown}
          prefix={<SearchOutlined />}
          placeholder="Tìm mã tài sản, serial, tên…"
          allowClear
          size="large"
          autoFocus
        />
        <Button onClick={close} size="large">Đóng</Button>
      </div>
      <div style={{ padding: '0 12px 14px', maxHeight: isMobile ? 'calc(100vh - 90px)' : 520, overflowY: 'auto' }}>
        {query.trim().length < MIN_QUERY ? (
          <Text type="secondary">Nhập ít nhất 2 ký tự để tìm kiếm.</Text>
        ) : loading ? (
          <div style={{ textAlign: 'center', padding: 24 }}><Spin /></div>
        ) : error ? (
          <Alert type="error" showIcon title={error} />
        ) : flat.length === 0 ? (
          <Empty description="Không có kết quả phù hợp" />
        ) : (
          <>
            {result.groups.map(group => (
              <div key={group.type} style={{ marginBottom: 12 }}>
                <Text strong style={{ display: 'block', marginBottom: 4 }}>{group.label}</Text>
                {group.items.map(item => {
                  const index = flat.findIndex(x => x.type === item.type && x.id === item.id);
                  return (
                    <div
                      key={`${item.type}:${item.id}`}
                      role="button"
                      tabIndex={0}
                      onClick={() => choose(item)}
                      onMouseEnter={() => setActiveIndex(index)}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter' || event.key === ' ') choose(item);
                      }}
                      style={{
                        cursor: 'pointer', padding: '8px 10px', borderRadius: 8,
                        background: index === activeIndex ? '#e6f4ff' : undefined,
                      }}
                    >
                      <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
                        <Text strong ellipsis>{item.title}</Text>
                        <Tag>{group.label}</Tag>
                      </div>
                      <Text type="secondary" ellipsis style={{ fontSize: 12 }}>{item.subtitle}</Text>
                    </div>
                  );
                })}
              </div>
            ))}
            {result.hasMore && <Text type="secondary" style={{ fontSize: 12 }}>Hiển thị tối đa 20 kết quả. Hãy nhập thêm để thu hẹp.</Text>}
          </>
        )}
      </div>
    </div>
  );

  return (
    <>
      {isMobile ? (
        <Button
          type="text"
          aria-label="Tìm kiếm nhanh"
          icon={<SearchOutlined />}
          onClick={openSearch}
        />
      ) : (
        <Input
          readOnly
          value=""
          onFocus={openSearch}
          onClick={openSearch}
          prefix={<SearchOutlined />}
          placeholder="Tìm nhanh  (Ctrl/Cmd+K)"
          style={{ width: 280 }}
          aria-label="Tìm kiếm nhanh"
        />
      )}
      {open && panel}
    </>
  );
}
