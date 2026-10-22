import { Component, type ErrorInfo, type ReactNode } from 'react';

const CHUNK_RELOAD_KEY = 'mirats.chunk-reload-attempted';

interface Props {
  children: ReactNode;
}

interface State {
  error: Error | null;
  isChunkError: boolean;
}

function isChunkLoadError(error: Error): boolean {
  const message = `${error.name ?? ''} ${error.message ?? ''}`.toLowerCase();
  return message.includes('chunkloaderror')
    || message.includes('loading chunk')
    || message.includes('failed to fetch dynamically imported module')
    || message.includes('importing a module script failed')
    || message.includes('dynamically imported module');
}

/**
 * [UI-3] Global render/error boundary.
 *
 * Two fallback modes:
 *  - chunk load failure (usually after a deploy/version swap): "Đã có phiên bản mới" and
 *    one automatic reload attempt per browser session (guarded by sessionStorage).
 *  - any other render exception: generic error with reload/home actions.
 */
export default class AppErrorBoundary extends Component<Props, State> {
  state: State = { error: null, isChunkError: false };

  static getDerivedStateFromError(error: Error): State {
    return { error, isChunkError: isChunkLoadError(error) };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error('[AppErrorBoundary] render error', error, info.componentStack);
    if (!isChunkLoadError(error)) return;

    try {
      if (sessionStorage.getItem(CHUNK_RELOAD_KEY) === '1') return;
      sessionStorage.setItem(CHUNK_RELOAD_KEY, '1');
      window.setTimeout(() => window.location.reload(), 1200);
    } catch {
      // sessionStorage can be unavailable in hardened browsers; keep the manual fallback.
    }
  }

  private reload = (): void => {
    window.location.reload();
  };

  private goHome = (): void => {
    window.location.assign('/');
  };

  render(): ReactNode {
    const { error, isChunkError } = this.state;
    if (!error) return this.props.children;

    const title = isChunkError ? 'Đã có phiên bản mới' : 'Đã xảy ra lỗi';
    const message = isChunkError
      ? 'Ứng dụng vừa được cập nhật. Trang sẽ tự tải lại một lần; nếu vẫn lỗi, hãy bấm "Tải lại".'
      : 'Không thể hiển thị trang này. Vui lòng tải lại hoặc quay về tổng quan.';

    return (
      <div
        role="alert"
        style={{
          minHeight: '100vh',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          padding: 24,
          background: '#f5f5f5',
          fontFamily: 'system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
        }}
      >
        <div
          style={{
            width: '100%',
            maxWidth: 520,
            background: '#fff',
            border: '1px solid #e5e7eb',
            borderRadius: 12,
            padding: 28,
            boxShadow: '0 8px 24px rgba(15, 23, 42, 0.08)',
          }}
        >
          <div style={{ fontSize: 24, fontWeight: 700, color: '#111827', marginBottom: 10 }}>
            {title}
          </div>
          <div style={{ color: '#4b5563', lineHeight: 1.6, marginBottom: 20 }}>{message}</div>
          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
            <button
              type="button"
              onClick={this.reload}
              style={{
                border: 0, borderRadius: 8, padding: '10px 18px', cursor: 'pointer',
                background: '#1677ff', color: '#fff', fontWeight: 600,
              }}
            >
              Tải lại
            </button>
            <button
              type="button"
              onClick={this.goHome}
              style={{
                border: '1px solid #d1d5db', borderRadius: 8, padding: '10px 18px', cursor: 'pointer',
                background: '#fff', color: '#111827', fontWeight: 600,
              }}
            >
              Về tổng quan
            </button>
          </div>
        </div>
      </div>
    );
  }
}
