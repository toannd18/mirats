import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { App, Button, Modal, type ButtonProps, type ModalProps } from 'antd';
import { useIsMobile } from '../../hooks/useIsMobile';

export type ModalFormSize = 'sm' | 'md' | 'lg';

interface ModalFormShellProps {
  open: boolean;
  title: ReactNode;
  children: ReactNode;
  onCancel: () => void;
  onOk?: () => void;
  okText?: string;
  cancelText?: string;
  confirmLoading?: boolean;
  okButtonProps?: ButtonProps;
  size?: ModalFormSize;
  footer?: ReactNode | null;
  maskClosable?: boolean;
  destroyOnHidden?: boolean;
  /** Transitional compatibility for callers migrating from raw Modal; ignored in favour of the size scale. */
  width?: number | string;
  style?: ModalProps['style'];
  // AntD's styles prop may be an object or a callback; callers only pass plain objects.
  styles?: Record<string, CSSProperties>;
  centered?: boolean;
  mask?: ModalProps['mask'];
}

const DESKTOP_WIDTH: Record<ModalFormSize, number> = {
  sm: 520,
  md: 720,
  lg: 960,
};

/**
 * [UI-3B] One modal/form shell for every create/edit dialog.
 *
 * - 95% width on phones; 520/720/960 on desktop (no ad-hoc fixed widths).
 * - The body scrolls independently; the action footer never leaves the viewport.
 * - First editable field receives focus when the dialog opens.
 * - A user-initiated close with unsaved input asks "Bỏ các thay đổi chưa lưu?".
 *   Successful saves are parent-driven (`open` becomes false), so they never hit this prompt.
 */
export default function ModalFormShell({
  open,
  title,
  children,
  onCancel,
  onOk,
  okText = 'Lưu thay đổi',
  cancelText = 'Hủy',
  confirmLoading = false,
  okButtonProps,
  size = 'md',
  footer,
  maskClosable = true,
  destroyOnHidden = true,
  style,
  styles,
  centered,
  mask,
}: ModalFormShellProps) {
  const isMobile = useIsMobile();
  const { modal } = App.useApp();
  const bodyRef = useRef<HTMLDivElement>(null);
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (!open) {
      setDirty(false);
      return;
    }

    // Focus the first real editable control after AntD has mounted the portal content.
    const timer = window.setTimeout(() => {
      const root = bodyRef.current;
      if (!root) return;
      const first = root.querySelector<HTMLElement>(
        'input:not([type="hidden"]):not([disabled]), textarea:not([disabled]), .ant-select-selector, .ant-picker-input input',
      );
      first?.focus();
    }, 80);

    return () => window.clearTimeout(timer);
  }, [open]);

  const handleCancel = () => {
    if (!dirty) {
      onCancel();
      return;
    }

    modal.confirm({
      title: 'Bỏ các thay đổi chưa lưu?',
      content: 'Các thay đổi bạn đã nhập sẽ không được lưu.',
      okText: 'Bỏ thay đổi',
      cancelText: 'Ở lại',
      okButtonProps: { danger: true },
      onOk: () => {
        setDirty(false);
        onCancel();
      },
    });
  };

  return (
    <Modal
      open={open}
      title={title}
      onCancel={handleCancel}
      onOk={onOk}
      okText={okText}
      cancelText={cancelText}
      confirmLoading={confirmLoading}
      okButtonProps={okButtonProps}
      width={isMobile ? '95%' : DESKTOP_WIDTH[size]}
      style={{ maxWidth: 'calc(100vw - 24px)', top: isMobile ? 12 : 48, ...style }}
      styles={{
        ...styles,
        body: {
          maxHeight: isMobile ? 'calc(100vh - 190px)' : 'calc(100vh - 230px)',
          overflowY: 'auto',
          paddingRight: 4,
          ...styles?.body,
        },
        footer: { marginTop: 0, paddingTop: 12, ...styles?.footer },
      }}
      footer={footer === undefined
        ? [
          <Button key="cancel" onClick={handleCancel}>{cancelText}</Button>,
          <Button key="ok" type="primary" loading={confirmLoading} {...okButtonProps} onClick={onOk}>
            {okText}
          </Button>,
        ]
        : footer}
      destroyOnHidden={destroyOnHidden}
      centered={centered ?? !isMobile}
      mask={{ closable: maskClosable, ...(mask && typeof mask === 'object' ? mask : {}) }}
    >
      <div
        ref={bodyRef}
        onInput={() => setDirty(true)}
        onChange={() => setDirty(true)}
      >
        {children}
      </div>
    </Modal>
  );
}
