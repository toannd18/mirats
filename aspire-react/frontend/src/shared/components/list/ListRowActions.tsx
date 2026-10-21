import type { ReactNode } from 'react';
import { Button, Dropdown, Space } from 'antd';
import type { MenuProps } from 'antd';
import { MoreOutlined } from '@ant-design/icons';

export interface ListPrimaryAction {
  key: string;
  label: string;
  icon?: ReactNode;
  onClick: () => void;
  disabled?: boolean;
}

interface ListRowActionsProps {
  primary?: ListPrimaryAction | null;
  menuItems?: MenuProps['items'];
  stopPropagation?: boolean;
}

/**
 * Approved UI-1 list action cell: one fixed-width outlined primary action + optional ⋯ menu.
 * Shared by every list page so the primary button and the menu always line up.
 */
export default function ListRowActions({ primary, menuItems, stopPropagation = false }: ListRowActionsProps) {
  const stop = (e: { stopPropagation: () => void }) => {
    if (stopPropagation) e.stopPropagation();
  };

  if (!primary && (!menuItems || menuItems.length === 0)) return null;

  return (
    <Space size={4} wrap>
      {primary && (
        <Button
          size="small"
          style={{ width: 104 }}
          icon={primary.icon}
          disabled={primary.disabled}
          onClick={(e) => { stop(e); primary.onClick(); }}
        >
          {primary.label}
        </Button>
      )}
      {menuItems && menuItems.length > 0 && (
        <Dropdown menu={{ items: menuItems }} trigger={['click']}>
          <Button size="small" icon={<MoreOutlined />} aria-label="Thao tác khác" onClick={stop} />
        </Dropdown>
      )}
    </Space>
  );
}
