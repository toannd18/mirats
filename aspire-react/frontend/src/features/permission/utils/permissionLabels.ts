const RESOURCE_LABELS: Record<string, string> = {
  assets: 'Tài sản',
  consumables: 'Vật tư tiêu hao',
  components: 'Linh kiện',
  accessories: 'Phụ kiện',
  licenses: 'Bản quyền',
  users: 'Người dùng',
  companies: 'Công ty',
  models: 'Model tài sản',
  categories: 'Danh mục',
  manufacturers: 'Nhà sản xuất',
  suppliers: 'Nhà cung cấp',
  departments: 'Phòng ban',
  locations: 'Vị trí',
  statuslabels: 'Trạng thái',
  depreciations: 'Khấu hao',
  reports: 'Báo cáo',
  customfields: 'Trường tùy chỉnh',
  systems: 'Hệ thống',
  maintenance: 'Bảo dưỡng',
  system: 'Hệ thống',
};

const ACTION_LABELS: Record<string, string> = {
  view: 'Xem',
  create: 'Tạo',
  edit: 'Sửa',
  delete: 'Xóa',
  checkout: 'Cấp phát',
  checkin: 'Thu hồi',
  audit: 'Kiểm kê',
  correct: 'Đính chính',
  templates: 'Mẫu bảo dưỡng',
  campaigns: 'Đợt bảo dưỡng',
  import: 'Nhập Excel',
  export: 'Xuất dữ liệu',
  admin: 'Quản trị',
  superuser: 'Toàn quyền',
  config: 'Cấu hình',
};

export const resourceLabel = (resource: string) => RESOURCE_LABELS[resource] ?? resource;
export const actionLabel = (action: string) => ACTION_LABELS[action] ?? action;
