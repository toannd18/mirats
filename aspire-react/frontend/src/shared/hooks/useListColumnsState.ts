import { useMemo } from 'react';

export interface ColumnVisibility {
  show: boolean;
}

/**
 * Versioned ProTable column state.
 *
 * Bump `storageKey` whenever the default columns change so users who already saved an older
 * layout get the new defaults instead of a stale column set.
 */
export function useListColumnsState(
  storageKey: string,
  defaultValue: Record<string, ColumnVisibility>,
) {
  return useMemo(() => ({
    persistenceKey: storageKey,
    persistenceType: 'localStorage' as const,
    defaultValue,
  }), [storageKey, defaultValue]);
}
