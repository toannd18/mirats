import { useEffect, useState } from 'react';
import { BrowserRouter, Routes, Route, Navigate, useNavigate, useLocation } from 'react-router-dom';
import { ConfigProvider, App as AntApp, Spin, Layout, Menu, Button, Drawer, Grid, Badge, Avatar, Dropdown, Breadcrumb, Space } from 'antd';
import viVN from 'antd/locale/vi_VN';
import type { MenuProps } from 'antd';
import {
  DashboardOutlined, LaptopOutlined, UserOutlined, TeamOutlined,
  SafetyOutlined, ToolOutlined, ApiOutlined, GiftOutlined,
  BarChartOutlined, KeyOutlined, AppstoreOutlined, SettingOutlined,
  EnvironmentOutlined, TagOutlined, BankOutlined, ShopOutlined,
  GoldOutlined, ScheduleOutlined, ApartmentOutlined, ClusterOutlined,
  HistoryOutlined, ExperimentOutlined, MenuOutlined,
  MenuFoldOutlined, LogoutOutlined, IdcardOutlined, ImportOutlined,
} from '@ant-design/icons';
import { initKeycloak, login, logout, isAuthenticated, isSuperUser, getUserInfo } from './services/keycloak';
import { designTokens } from './theme/designTokens';
import { usePermissionMap } from './hooks/usePermission';
import { useCurrentUser, clearCurrentUserCache } from './hooks/useCurrentUser';
import apiClient from './services/api-client';
import ProtectedRoute from './components/ProtectedRoute';
import AssetListPage from './features/asset/pages/AssetListPage';
import AssetDetailPage from './features/asset/pages/AssetDetailPage';
import MaintenanceListPage from './features/maintenance/pages/MaintenanceListPage';
import UserListPage from './features/user/pages/UserListPage';
import UserDetailPage from './features/user/pages/UserDetailPage';
import DashboardPage from './features/system/pages/DashboardPage';
import ConsumableListPage from './features/consumable/pages/ConsumableListPage';
import ConsumableDetailPage from './features/consumable/pages/ConsumableDetailPage';
import ComponentListPage from './features/component/pages/ComponentListPage';
import ComponentDetailPage from './features/component/pages/ComponentDetailPage';
import AccessoryListPage from './features/accessory/pages/AccessoryListPage';
import AccessoryDetailPage from './features/accessory/pages/AccessoryDetailPage';
import LicenseListPage from './features/license/pages/LicenseListPage';
import ReportsPage from './features/system/pages/ReportsPage';
import GroupListPage from './features/permission/pages/GroupListPage';
import PermissionMatrixPage from './features/permission/pages/PermissionMatrixPage';
import CategoryListPage from './features/admin/pages/CategoryListPage';
import ManufacturerListPage from './features/admin/pages/ManufacturerListPage';
import SupplierListPage from './features/admin/pages/SupplierListPage';
import AssetModelListPage from './features/admin/pages/AssetModelListPage';
import LocationListPage from './features/admin/pages/LocationListPage';
import DepreciationListPage from './features/admin/pages/DepreciationListPage';
import CompanyListPage from './features/admin/pages/CompanyListPage';
import DepartmentListPage from './features/admin/pages/DepartmentListPage';
import SystemInfoListPage from './features/admin/pages/SystemInfoListPage';
import SystemConfigPage from './features/admin/pages/SystemConfigPage';
import SystemHistoryPage from './features/system/pages/SystemHistoryPage';
import SystemDetailPage from './features/system/pages/SystemDetailPage';
import ImportPage from './features/import/pages/ImportPage';

const { Header, Sider, Content } = Layout;

function AppLayout({ children }: { children: React.ReactNode }) {
  const navigate = useNavigate();
  const location = useLocation();
  const authenticated = isAuthenticated();
  const screens = Grid.useBreakpoint();
  // md breakpoint = 768px. Below md (tablet/mobile) the sidebar is hidden entirely
  // and replaced by a Drawer opened via the hamburger button in the header.
  const isMobile = screens.md === false;
  const [collapsed, setCollapsed] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [lowStock, setLowStock] = useState(0);
  const perm = usePermissionMap();
  const currentUser = useCurrentUser();
  const userInfo = getUserInfo();
  const isSuper = isSuperUser();
  const displayName = [userInfo.firstName, userInfo.lastName].filter(Boolean).join(' ') || userInfo.username;

  // Breadcrumb: suy ra 2 cấp đầu từ route (cấp 3 tên record để dành task sau).
  const crumbMap: Record<string, string> = {
    '/dashboard': 'Dashboard',
    '/consumables': 'Vật tư tiêu hao', '/components': 'Linh kiện', '/accessories': 'Phụ kiện',
    '/licenses': 'Bản quyền', '/assets': 'Tài sản', '/maintenances': 'Bảo trì',
    '/system-history': 'Lịch sử hệ thống', '/reports': 'Báo cáo', '/users': 'Người dùng',
    '/groups': 'Nhóm', '/permissions': 'Phân quyền',
    '/admin/categories': 'Danh mục', '/admin/manufacturers': 'Nhà SX', '/admin/suppliers': 'Nhà cung cấp',
    '/admin/asset-models': 'Asset Models', '/admin/locations': 'Địa điểm',
    '/admin/depreciations': 'Khấu hao', '/admin/companies': 'Công ty', '/admin/departments': 'Phòng ban',
    '/admin/system-infos': 'Hệ thống', '/admin/import': 'Import Excel',
    '/admin/system-config': 'Cấu hình hệ thống',
  };
  const parentCrumbMap: Record<string, string> = {
    '/consumables': 'Vật tư', '/components': 'Vật tư', '/accessories': 'Vật tư',
    '/admin/categories': 'Quản trị', '/admin/manufacturers': 'Quản trị', '/admin/suppliers': 'Quản trị',
    '/admin/asset-models': 'Quản trị', '/admin/locations': 'Quản trị',
    '/admin/depreciations': 'Quản trị', '/admin/companies': 'Quản trị', '/admin/departments': 'Quản trị',
    '/admin/system-infos': 'Quản trị', '/admin/import': 'Quản trị',
    '/admin/system-config': 'Quản trị',
  };
  const crumbSegs: { title: string }[] = [];
  const exactLabel = crumbMap[location.pathname];
  if (exactLabel) {
    const parent = parentCrumbMap[location.pathname];
    if (parent) crumbSegs.push({ title: parent });
    crumbSegs.push({ title: exactLabel });
  } else if (location.pathname.startsWith('/assets')) {
    crumbSegs.push({ title: 'Tài sản' });
    crumbSegs.push({ title: 'Chi tiết' });
  } else if (location.pathname.startsWith('/users')) {
    crumbSegs.push({ title: 'Người dùng' });
    crumbSegs.push({ title: 'Chi tiết' });
  } else if (location.pathname.startsWith('/systems')) {
    crumbSegs.push({ title: 'Hệ thống' });
    crumbSegs.push({ title: 'Chi tiết' });
  } else if (location.pathname.startsWith('/admin')) {
    crumbSegs.push({ title: 'Quản trị' });
  } else {
    crumbSegs.push({ title: exactLabel || 'Trang' });
  }

  const permMap: Record<string, string> = {
    '/consumables': 'consumables.view',
    '/components': 'components.view',
    '/accessories': 'accessories.view',
    '/licenses': 'licenses.view',
    '/assets': 'assets.view',
    '/maintenances': 'assets.view',
    '/reports': 'reports.view',
    '/users': 'users.view',
    '/groups': 'admin',
    '/permissions': 'admin',
    '/admin/categories': 'categories.view',
    '/admin/manufacturers': 'manufacturers.view',
    '/admin/suppliers': 'suppliers.view',
    '/admin/asset-models': 'models.view',
    '/admin/locations': 'locations.view',

    '/admin/depreciations': 'depreciations.view',
    '/admin/companies': 'companies.view',
    '/admin/departments': 'departments.view',
    '/admin/system-infos': 'systems.view',
    '/admin/system-config': 'system.config',
  };

  // Permission gating: superuser/admin sees everything. Otherwise a leaf is shown only
  // when its `.view` permission is granted. /system-history has no dedicated `.view`
  // permission (backend just [Authorize]) so it always shows.
  const canSee = (key: string): boolean => {
    if (!perm) return true; // permission still loading → show to avoid flicker
    if (perm.isSuperUser) return true;
    // Import page aggregates several per-entity CREATE permissions (backend gated per type).
    if (key === '/admin/import') {
      return ['categories.create', 'assets.create', 'components.create', 'accessories.create', 'consumables.create']
        .some(c => (perm.permissions[c] ?? 0) === 1);
    }
    const code = permMap[key];
    if (!code) return true;
    return (perm.permissions[code] ?? 0) === 1;
  };

  const filterChildren = (children: { key: string; label: string; icon?: React.ReactNode }[]) =>
    children.filter(c => canSee(c.key));

  useEffect(() => {
    if (!authenticated) return;
    let alive = true;
    apiClient.get('/dashboard/summary')
      .then(res => { if (alive) setLowStock(res.data?.data?.lowStockCount ?? 0); })
      .catch(() => { /* silent — badge is best-effort */ });
    return () => { alive = false; };
  }, [authenticated]);

  const inventoryChildren = filterChildren([
    { key: '/consumables', icon: <ToolOutlined />, label: 'Vật tư tiêu hao' },
    { key: '/components', icon: <ApiOutlined />, label: 'Linh kiện' },
    { key: '/accessories', icon: <GiftOutlined />, label: 'Phụ kiện' },
  ]);

  const adminChildren = filterChildren([
    { key: '/admin/categories', icon: <TagOutlined />, label: 'Danh mục' },
    { key: '/admin/manufacturers', icon: <BankOutlined />, label: 'Nhà SX' },
    { key: '/admin/suppliers', icon: <ShopOutlined />, label: 'Nhà cung cấp' },
    { key: '/admin/asset-models', icon: <GoldOutlined />, label: 'Asset Models' },
    { key: '/admin/locations', icon: <EnvironmentOutlined />, label: 'Địa điểm' },

    { key: '/admin/depreciations', icon: <ScheduleOutlined />, label: 'Khấu hao' },
    { key: '/admin/companies', icon: <BankOutlined />, label: 'Công ty' },
    { key: '/admin/departments', icon: <ApartmentOutlined />, label: 'Phòng ban' },
    { key: '/admin/system-infos', icon: <ClusterOutlined />, label: 'Hệ thống' },
    { key: '/admin/import', icon: <ImportOutlined />, label: 'Import Excel' },
    { key: '/admin/system-config', icon: <SettingOutlined />, label: 'Cấu hình hệ thống' },
  ]);

  const menuGroups: { label: string; items: NonNullable<MenuProps['items']> }[] = [];

  menuGroups.push({
    label: 'TỔNG QUAN',
    items: [{ key: '/dashboard', icon: <DashboardOutlined />, label: 'Dashboard' }],
  });

  const business: NonNullable<MenuProps['items']> = [];
  if (inventoryChildren.length > 0) {
    business.push({
      key: 'inventory', icon: <AppstoreOutlined />, label: (
        // Badge mặc định set color: var(--ant-color-text) (#020617 — đen tối) lên chữ,
        // làm label "Vật tư" chìm trên nền Sider tối. Badge v6 KHÔNG áp prop style
        // vào root span khi có children (Badge.js chỉ dùng mergedStyles.root), nên
        // không thể ép inherit qua prop. Thay vào đó:
        //   - span con đặt color: inherit (kế thừa chuỗi màu),
        //   - CSS override dùng class ổn định (KHÔNG phải css-dev-only) trong index.css:
        //     .ant-menu-dark .ant-menu-submenu-title .ant-badge { color: inherit; }
        //     → Badge root kế thừa màu sáng của submenu-title, span con sáng theo.
        <Badge count={lowStock} size="small" overflowCount={999} offset={[8, 0]}>
          <span style={{ color: 'inherit' }}>Vật tư</span>
        </Badge>
      ),
      children: inventoryChildren,
    });
  }
  if (canSee('/licenses')) business.push({ key: '/licenses', icon: <KeyOutlined />, label: 'Bản quyền' });
  if (canSee('/assets')) business.push({ key: '/assets', icon: <LaptopOutlined />, label: 'Tài sản' });
  if (canSee('/maintenances')) business.push({ key: '/maintenances', icon: <ExperimentOutlined />, label: 'Bảo trì' });
  business.push({ key: '/system-history', icon: <HistoryOutlined />, label: 'Lịch sử hệ thống' });
  if (canSee('/reports')) business.push({ key: '/reports', icon: <BarChartOutlined />, label: 'Báo cáo' });
  if (business.length > 0) menuGroups.push({ label: 'NGHIỆP VỤ', items: business });

  const systemItems: NonNullable<MenuProps['items']> = [];
  if (canSee('/users')) systemItems.push({ key: '/users', icon: <UserOutlined />, label: 'Người dùng' });
  if (canSee('/groups')) systemItems.push({ key: '/groups', icon: <TeamOutlined />, label: 'Nhóm' });
  if (canSee('/permissions')) systemItems.push({ key: '/permissions', icon: <SafetyOutlined />, label: 'Phân quyền' });
  if (systemItems.length > 0) menuGroups.push({ label: 'HỆ THỐNG', items: systemItems });

  if (adminChildren.length > 0) {
    menuGroups.push({ label: 'QUẢN TRỊ', items: [{ key: 'admin', icon: <SettingOutlined />, label: 'Quản trị', children: adminChildren }] });
  }

  const menuItems = menuGroups.map(g => ({
    type: 'group' as const,
    label: g.label,
    children: g.items,
  }));

  const allLeafKeys = Object.keys(permMap).concat('/dashboard', '/system-history');
  const currentPath = location.pathname;
  const selectedKey = allLeafKeys
    .filter(k => currentPath === k || currentPath.startsWith(k + '/') || currentPath.startsWith(k))
    .sort((a, b) => b.length - a.length)[0] ?? currentPath;

  // Auto-expand the submenu group that contains the active item (so the active child is visible).
  const submenuByKey: Record<string, string> = {
    '/consumables': 'inventory', '/components': 'inventory', '/accessories': 'inventory',
    '/admin/categories': 'admin', '/admin/manufacturers': 'admin', '/admin/suppliers': 'admin',
    '/admin/asset-models': 'admin', '/admin/locations': 'admin',
    '/admin/depreciations': 'admin', '/admin/companies': 'admin', '/admin/departments': 'admin',
    '/admin/system-infos': 'admin', '/admin/import': 'admin',
    '/admin/system-config': 'admin',
  };
  const activeSubmenu = submenuByKey[selectedKey];
  const [openKeys, setOpenKeys] = useState<string[]>(activeSubmenu ? [activeSubmenu] : []);

  // Ensure the submenu containing the active item is open (covers direct URL navigation too).
  useEffect(() => {
    if (activeSubmenu && !openKeys.includes(activeSubmenu)) {
      setOpenKeys(prev => [...prev, activeSubmenu]);
    }
  }, [activeSubmenu, openKeys]);

  const handleMenuClick = (key: string) => {
    navigate(key);
    setDrawerOpen(false);
  };

  const menuProps: MenuProps = {
    theme: 'dark',
    mode: 'inline',
    selectedKeys: [selectedKey],
    openKeys,
    onOpenChange: (keys) => setOpenKeys(keys as string[]),
    items: menuItems,
    onClick: ({ key }) => handleMenuClick(key),
    inlineCollapsed: collapsed,
  };

  // Desktop: collapsible Sider (icon-only when collapsed).
  const siderMenu = <Menu {...menuProps} />;
  // Mobile drawer always renders the menu expanded (independent of desktop collapse state).
  const drawerMenu = <Menu {...menuProps} inlineCollapsed={false} />

  return (
    <Layout style={{ minHeight: '100vh' }}>
      {!isMobile && (
        <Sider
          collapsible
          collapsed={collapsed}
          onCollapse={setCollapsed}
          trigger={null}
          width={220}
          style={{
            height: '100vh',
            position: 'sticky',
            top: 0,
            display: 'flex',
            flexDirection: 'column',
            overflow: 'hidden',
          }}
        >
          <div
            role="button"
            aria-label={collapsed ? 'Mở rộng menu' : 'Thu gọn menu'}
            tabIndex={0}
            onClick={() => setCollapsed(c => !c)}
            onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setCollapsed(c => !c); } }}
            style={{
              flexShrink: 0,
              padding: collapsed ? '16px 0' : '16px',
              color: 'white',
              fontWeight: 'bold',
              fontSize: '16px',
              whiteSpace: 'nowrap',
              overflow: 'hidden',
              display: 'flex',
              alignItems: 'center',
              justifyContent: collapsed ? 'center' : 'space-between',
              gap: 8,
              cursor: 'pointer',
              minHeight: 48,
            }}
          >
            <span style={{ lineHeight: '32px' }}>{collapsed ? 'M' : 'Mirats'}</span>
            {!collapsed && (
              <MenuFoldOutlined
                style={{ color: 'rgba(255,255,255,0.65)', fontSize: 14 }}
                onClick={(e) => { e.stopPropagation(); setCollapsed(c => !c); }}
              />
            )}
          </div>
          <div style={{ flex: 1, overflowY: 'auto', minHeight: 0 }}>
            {siderMenu}
          </div>
        </Sider>
      )}
      <Layout>
        <Header style={{ background: '#fff', padding: '0 16px', display: 'flex', alignItems: 'center', gap: 12, height: 56, lineHeight: '56px' }}>
          {isMobile && (
            <Button
              type="text"
              aria-label="Mở menu"
              icon={<MenuOutlined style={{ fontSize: 18 }} />}
              onClick={() => setDrawerOpen(true)}
            />
          )}
          {!isMobile && authenticated && crumbSegs.length > 0 && (
            <Breadcrumb items={crumbSegs} style={{ marginRight: 'auto' }} />
          )}
          <div style={{ flex: 1 }} />
          {authenticated ? (
            <Dropdown
              menu={{
                items: [
                  { key: 'profile', icon: <IdcardOutlined />, label: 'Xem hồ sơ', onClick: () => { if (currentUser?.id) navigate(`/users/${currentUser.id}`); } },
                  { type: 'divider' },
                  { key: 'logout', icon: <LogoutOutlined />, label: 'Đăng xuất', onClick: () => { clearCurrentUserCache(); logout(); }, danger: true },
                ],
              }}
              trigger={['click']}
              placement="bottomRight"
            >
              <Space style={{ cursor: 'pointer', alignItems: 'center', gap: 8 }}>
                <Avatar size="small" style={{ backgroundColor: '#0369A1', verticalAlign: 'middle' }}>
                  {(displayName || '?').charAt(0).toUpperCase()}
                </Avatar>
                <span style={{ fontSize: 13, color: '#020617' }}>{displayName || userInfo.username}</span>
                {!isSuper && currentUser?.companyName && (
                  <Badge color="geekblue" text={<span style={{ fontSize: 12, color: '#475569' }}>{currentUser.companyName}</span>} />
                )}
              </Space>
            </Dropdown>
          ) : (
            <Button type="primary" onClick={login}>Login</Button>
          )}
        </Header>
        <Content style={{ margin: isMobile ? 8 : 24, padding: isMobile ? 12 : 24, background: '#fff' }}>
          {children}
        </Content>
      </Layout>
      {/* Mobile: sidebar as Drawer overlay opened from the hamburger button. */}
      <Drawer
        placement="left"
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        size={240}
        styles={{ body: { padding: 0, background: '#001529' } }}
        closeIcon={null}
      >
        <div style={{ padding: '16px', color: 'white', fontWeight: 'bold', fontSize: '16px' }}>
          Mirats
        </div>
        {drawerMenu}
      </Drawer>
    </Layout>
  );
}

function App() {
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    initKeycloak().finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh' }}>
        <Spin size="large" />
      </div>
    );
  }

  return (
    <ConfigProvider locale={viVN} theme={designTokens}>
      <AntApp>
        <BrowserRouter>
          <Routes>
            <Route path="/" element={
              <ProtectedRoute>
                <AppLayout><Navigate to="/dashboard" replace /></AppLayout>
              </ProtectedRoute>
            } />
            <Route path="/dashboard" element={
              <ProtectedRoute>
                <AppLayout><DashboardPage /></AppLayout>
              </ProtectedRoute>
            } />
            <Route path="/assets" element={
              <ProtectedRoute>
                <AppLayout><AssetListPage /></AppLayout>
              </ProtectedRoute>
            } />
            <Route path="/assets/new" element={
              <ProtectedRoute>
                <AppLayout><AssetListPage /></AppLayout>
              </ProtectedRoute>
            } />
            <Route path="/assets/:id" element={
              <ProtectedRoute>
                <AppLayout><AssetDetailPage /></AppLayout>
              </ProtectedRoute>
            } />
            <Route path="/assets/:id/edit" element={
              <ProtectedRoute>
                <AppLayout><AssetListPage /></AppLayout>
              </ProtectedRoute>
            } />
            <Route path="/maintenances" element={
              <ProtectedRoute>
                <AppLayout><MaintenanceListPage /></AppLayout>
              </ProtectedRoute>
            } />
            <Route path="/system-history" element={
              <ProtectedRoute>
                <AppLayout><SystemHistoryPage /></AppLayout>
              </ProtectedRoute>
            } />
            <Route path="/systems/:id" element={
              <ProtectedRoute>
                <AppLayout><SystemDetailPage /></AppLayout>
              </ProtectedRoute>
            } />
            <Route path="/consumables" element={
              <ProtectedRoute><AppLayout><ConsumableListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/consumables/new" element={
              <ProtectedRoute><AppLayout><ConsumableListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/consumables/:id/view" element={
              <ProtectedRoute><AppLayout><ConsumableDetailPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/consumables/:id" element={
              <ProtectedRoute><AppLayout><ConsumableListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/components" element={
              <ProtectedRoute><AppLayout><ComponentListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/components/new" element={
              <ProtectedRoute><AppLayout><ComponentListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/components/:id/edit" element={
              <ProtectedRoute><AppLayout><ComponentListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/components/:id" element={
              <ProtectedRoute><AppLayout><ComponentDetailPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/accessories" element={
              <ProtectedRoute><AppLayout><AccessoryListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/accessories/new" element={
              <ProtectedRoute><AppLayout><AccessoryListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/accessories/:id/view" element={
              <ProtectedRoute><AppLayout><AccessoryDetailPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/accessories/:id" element={
              <ProtectedRoute><AppLayout><AccessoryListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/licenses" element={
              <ProtectedRoute><AppLayout><LicenseListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/licenses/new" element={
              <ProtectedRoute><AppLayout><LicenseListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/licenses/:id/edit" element={
              <ProtectedRoute><AppLayout><LicenseListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/licenses/:id" element={
              <ProtectedRoute><AppLayout><LicenseListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/reports" element={
              <ProtectedRoute><AppLayout><ReportsPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/users" element={
              <ProtectedRoute>
                <AppLayout><UserListPage /></AppLayout>
              </ProtectedRoute>
            } />
            <Route path="/users/:id" element={
              <ProtectedRoute>
                <AppLayout><UserDetailPage /></AppLayout>
              </ProtectedRoute>
            } />
            <Route path="/groups" element={
              <ProtectedRoute><AppLayout><GroupListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/permissions" element={
              <ProtectedRoute><AppLayout><PermissionMatrixPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/admin/categories" element={
              <ProtectedRoute><AppLayout><CategoryListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/admin/manufacturers" element={
              <ProtectedRoute><AppLayout><ManufacturerListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/admin/suppliers" element={
              <ProtectedRoute><AppLayout><SupplierListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/admin/asset-models" element={
              <ProtectedRoute><AppLayout><AssetModelListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/admin/locations" element={
              <ProtectedRoute><AppLayout><LocationListPage /></AppLayout></ProtectedRoute>
            } />

            <Route path="/admin/depreciations" element={
              <ProtectedRoute><AppLayout><DepreciationListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/admin/companies" element={
              <ProtectedRoute><AppLayout><CompanyListPage /></AppLayout></ProtectedRoute>
            } />
              <Route path="/admin/departments" element={
              <ProtectedRoute><AppLayout><DepartmentListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/admin/system-infos" element={
              <ProtectedRoute><AppLayout><SystemInfoListPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/admin/import" element={
              <ProtectedRoute><AppLayout><ImportPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="/admin/system-config" element={
              <ProtectedRoute><AppLayout><SystemConfigPage /></AppLayout></ProtectedRoute>
            } />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
      </AntApp>
    </ConfigProvider>
  );
}

export default App;