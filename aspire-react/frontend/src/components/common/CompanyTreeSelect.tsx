import { useEffect, useState } from 'react';
import { App, Button, Divider, Input, Space, TreeSelect } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { TreeSelectProps } from 'antd';
import apiClient from '../../services/api-client';

export interface CompanyNode {
  id: string;
  name: string;
  parentId?: string | null;
  children?: CompanyNode[];
}

interface CompanyTreeSelectProps {
  value?: string;
  onChange?: (value?: string) => void;
  disabled?: boolean;
  placeholder?: string;
  size?: 'large' | 'middle' | 'small';
  /** Hiện ô "Thêm công ty mới" ở đáy dropdown (quick-add). */
  allowQuickAdd?: boolean;
  /** Pseudo-node gốc (không có con) hiển thị TRÊN CÙNG tree — dùng cho các
   *  giá trị filter đặc biệt không phải công ty thật (VD "Chưa xác định công ty").
   *  value sẽ được truyền nguyên qua onChange. */
  extraRootOption?: { label: string; value: string };
}

/** Dựng treeData đệ quy (mọi cấp cha/con) cho TreeSelect. */
function toTreeData(nodes: CompanyNode[]): NonNullable<TreeSelectProps['treeData']> {
  return nodes.map((node) => ({
    value: node.id,
    title: node.name,
    children: node.children?.length ? toTreeData(node.children) : undefined,
  }));
}

/**
 * TreeSelect công ty DÙNG CHUNG — cho phép chọn công ty ở MỌI cấp (cha hoặc con),
 * đồng bộ với Component/Accessory/User (API /companies trả tree đệ quy).
 * Tự tải cây một lần; quick-add (nếu bật) POST /companies rồi refresh cây + tự chọn công ty mới.
 */
export default function CompanyTreeSelect({
  value, onChange, disabled, placeholder = 'Chọn công ty', size = 'middle', allowQuickAdd = false, extraRootOption,
}: CompanyTreeSelectProps) {
  const { message } = App.useApp();
  const [treeData, setTreeData] = useState<NonNullable<TreeSelectProps['treeData']>>([]);
  const [newCompanyName, setNewCompanyName] = useState('');
  const [adding, setAdding] = useState(false);

  const load = async () => {
    try {
      const res = await apiClient.get('/companies');
      setTreeData(toTreeData((res.data?.data ?? []) as CompanyNode[]));
    } catch {
      /* non-critical — tree rỗng, không crash */
    }
  };

  useEffect(() => {
    void load();
  }, []);

  // Prepends the pseudo root option (e.g. "Chưa xác định công ty") above the tree when set.
  const treeDataWithExtra = extraRootOption
    ? [{ value: extraRootOption.value, title: extraRootOption.label }, ...treeData]
    : treeData;

  const addCompany = async () => {
    const name = newCompanyName.trim();
    if (!name) { message.warning('Nhập tên công ty mới'); return; }
    setAdding(true);
    try {
      const res = await apiClient.post('/companies', { name });
      const created = res.data?.data as { id: string; name: string };
      await load();               // refresh cây (đảm bảo hierarchy đúng)
      onChange?.(created.id);     // tự chọn công ty vừa tạo
      setNewCompanyName('');
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể tạo công ty');
    } finally {
      setAdding(false);
    }
  };

  return (
    <TreeSelect
      showSearch
      allowClear
      size={size}
      style={{ width: '100%' }}
      value={value}
      onChange={(v) => onChange?.(v as string | undefined)}
      disabled={disabled}
      treeData={treeDataWithExtra}
      placeholder={placeholder}
      treeDefaultExpandAll
      filterTreeNode={(input, treeNode) => (treeNode?.title as string)?.toLowerCase().includes(input.toLowerCase())}
      popupRender={allowQuickAdd ? (menu) => (
        <>
          {menu}
          <Divider style={{ margin: '8px 0' }} />
          <Space style={{ padding: '0 8px 4px' }}>
            <Input size="small" placeholder="Tên công ty mới" value={newCompanyName}
              onChange={(e) => setNewCompanyName(e.target.value)}
              onPressEnter={() => void addCompany()} style={{ width: 180 }} />
            <Button size="small" type="primary" icon={<PlusOutlined />} loading={adding} onClick={() => void addCompany()}>Thêm</Button>
          </Space>
        </>
      ) : undefined}
    />
  );
}