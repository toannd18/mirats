import { useEffect, useState, useCallback } from 'react';
import {
  Form, Input, Select, Switch, Space, App,
} from 'antd';
import type { SelectProps } from 'antd';
import apiClient from '../../../services/api-client';
import type { UserDto, ReferenceOption } from '../types/users';
import { useIsMobile } from '../../../hooks/useIsMobile';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';
import ModalFormShell from '../../../shared/components/ModalFormShell';
import { applyServerFieldErrors, serverErrorMessage } from '../../../shared/utils/formErrors';

// ==================== Types ====================

interface UserFormModalProps {
  open: boolean;
  user: UserDto | null;
  onSuccess: () => void;
  onCancel: () => void;
}

interface FormValues {
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  employeeNumber: string;
  jobTitle: string;
  password: string;
  isSuperUser: boolean;
  isActive: boolean;
  companyId: string | undefined;
  departmentId: string | undefined;
  locationId: string | undefined;
}

// ==================== Component ====================

// [FIX-N1] Project-wide "Guid.Empty = floater/none" sentinel (mirrors CompanyScopeService /
// AssetMaintenance.CompanyId): on UPDATE an empty Company/Department/Location select must send the
// sentinel so the backend CLEARS it, instead of omitting the field (= keep the stored value).
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

const selectProps: SelectProps = {
  showSearch: true, allowClear: true, optionFilterProp: 'label',
  size: 'middle', style: { width: '100%' },
};

const UserFormModal: React.FC<UserFormModalProps> = ({
  open, user, onSuccess, onCancel,
}) => {
  const { message } = App.useApp();
  const isMobile = useIsMobile();

  const [form] = Form.useForm<FormValues>();
  const [submitting, setSubmitting] = useState(false);
  const isEditing = !!user;

  // Cascading state — fetched from API based on selected companyId
  const [departmentOptions, setDepartmentOptions] = useState<ReferenceOption[]>([]);
  const [locationOptions, setLocationOptions] = useState<ReferenceOption[]>([]);
  const [fetchingDeps, setFetchingDeps] = useState(false);

  // Watch companyId reactively for disabling dependent fields
  const watchedCompanyId = Form.useWatch('companyId', form);

  // ==================== Fetch Departments/Locations by Company ====================

  const fetchDependentData = useCallback(async (companyId: string | undefined) => {
    if (!companyId) {
      setDepartmentOptions([]);
      setLocationOptions([]);
      return;
    }
    setFetchingDeps(true);
    try {
      const [deptRes, locRes] = await Promise.all([
        apiClient.get('/departments', { params: { companyId, pageSize: 500 } }),
        apiClient.get('/locations', { params: { companyId, pageSize: 500 } }),
      ]);
      setDepartmentOptions(deptRes.data.data ?? []);
      setLocationOptions(locRes.data.data ?? []);
    } catch {
      void message.error('Không tải được phòng ban/địa điểm');
    } finally {
      setFetchingDeps(false);
    }
  }, [message]);

  // ==================== Init / Reset on Open ====================

  useEffect(() => {
    if (open) {
      if (user) {
        form.setFieldsValue({
          username: user.username,
          email: user.email,
          firstName: user.firstName,
          lastName: user.lastName,
          employeeNumber: user.employeeNumber ?? '',
          jobTitle: user.jobTitle ?? '',
          isSuperUser: user.isSuperUser,
          isActive: user.isActive,
          companyId: user.companyId ?? undefined,
          departmentId: user.departmentId ?? undefined,
          locationId: user.locationId ?? undefined,
        });
        // Pre-fetch dependent data for editing
        if (user.companyId) {
          fetchDependentData(user.companyId);
        }
      } else {
        form.resetFields();
        form.setFieldsValue({
          isActive: true,
          isSuperUser: false,
          employeeNumber: '',
          jobTitle: '',
        });
        setDepartmentOptions([]);
        setLocationOptions([]);
      }
    } else {
      setDepartmentOptions([]);
      setLocationOptions([]);
    }
  }, [open, user, form, fetchDependentData]);

  // ==================== Company Change Handler ====================

  const handleCompanyChange = (value: string | undefined) => {
    // Reset dependent fields
    form.setFieldsValue({
      departmentId: undefined,
      locationId: undefined,
    });
    // Fetch new options
    fetchDependentData(value);
  };

  // ==================== Submit ====================

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();
      setSubmitting(true);

      const payload = {
        username: values.username?.trim(),
        email: values.email?.trim().toLowerCase(),
        firstName: values.firstName?.trim(),
        lastName: values.lastName?.trim(),
        // [FIX-N1] Update is three-valued (patch semantics): OMITTED = keep, Guid.Empty = clear to
        // floater/none, real id = set. On create the API's null = floater semantics apply as before.
        employeeNumber: isEditing ? (values.employeeNumber?.trim() ?? '') : (values.employeeNumber?.trim() || null),
        jobTitle: isEditing ? (values.jobTitle?.trim() ?? '') : (values.jobTitle?.trim() || null),
        isSuperUser: values.isSuperUser,
        isActive: values.isActive,
        companyId: isEditing ? (values.companyId || EMPTY_GUID) : (values.companyId || null),
        departmentId: isEditing ? (values.departmentId || EMPTY_GUID) : (values.departmentId || null),
        locationId: isEditing ? (values.locationId || EMPTY_GUID) : (values.locationId || null),
        // [AUTH Phase 4] initial password — create mode only (local auth; user must change it
        // at first login). Never sent on update.
        ...(isEditing ? {} : { password: values.password }),
      };

      if (isEditing) {
        await apiClient.put(`/users/${user!.id}`, { id: user!.id, ...payload });
        void message.success('Đã cập nhật người dùng');
      } else {
        await apiClient.post('/users', payload);
        void message.success('Đã tạo người dùng');
      }
      onSuccess();
    } catch (error: unknown) {
      if (!applyServerFieldErrors(form, error)) {
        void message.error(serverErrorMessage(error, 'Không thể lưu người dùng. Vui lòng thử lại.'));
      }
    } finally {
      setSubmitting(false);
    }
  };

  // ==================== Derived State ====================

  const hasCompany = !!watchedCompanyId;
  const fieldFlex = isMobile ? {} : { display: 'flex', gap: 12 };

  // ==================== Render ====================

  return (
    <ModalFormShell
      title={isEditing ? 'Sửa người dùng' : 'Thêm người dùng'}
      open={open}
      onOk={() => void handleSubmit()}
      onCancel={onCancel}
      confirmLoading={submitting}
      size="md"
      maskClosable={false}
      okText={isEditing ? 'Lưu thay đổi' : 'Tạo người dùng'}
    >
      <Form
        form={form}
        layout="vertical"
        size="middle"
        autoComplete="off"
        initialValues={{ isActive: true, isSuperUser: false, employeeNumber: '', jobTitle: '' }}
        style={{ marginTop: 8 }}
      >
        {/* Row 1: First Name + Last Name */}
        <div style={fieldFlex}>
          <Form.Item
            label="Họ" name="firstName"
            rules={[{ required: true, message: 'Vui lòng nhập họ' }, { max: 100 }]}
            style={{ flex: 1 }}
          >
            <Input placeholder="Nhập họ" />
          </Form.Item>
          <Form.Item
            label="Tên" name="lastName"
            rules={[{ required: true, message: 'Vui lòng nhập tên' }, { max: 100 }]}
            style={{ flex: 1 }}
          >
            <Input placeholder="Nhập tên" />
          </Form.Item>
        </div>

        {/* Row 2: Username + Email */}
        <div style={fieldFlex}>
          <Form.Item
            label="Tên đăng nhập" name="username"
            rules={[
              { required: true, message: 'Vui lòng nhập tên đăng nhập' },
              { max: 100 },
              { pattern: /^\S+$/, message: 'Tên đăng nhập không được có khoảng trắng' },
            ]}
            style={{ flex: 1 }}
          >
            <Input placeholder="Tên đăng nhập" disabled={isEditing} />
          </Form.Item>
          <Form.Item
            label="Email" name="email"
            rules={[{ required: true, message: 'Vui lòng nhập email' }, { type: 'email', message: 'Email không hợp lệ' }, { max: 255 }]}
            style={{ flex: 1 }}
          >
            <Input placeholder="Địa chỉ email" />
          </Form.Item>
        </div>

        {/* Row 3: Employee Number + Job Title */}
        <div style={fieldFlex}>
          <Form.Item
            label="Mã nhân viên" name="employeeNumber"
            rules={[{ max: 50 }]}
            style={{ flex: 1 }}
          >
            <Input placeholder="Không bắt buộc" />
          </Form.Item>
          <Form.Item
            label="Chức danh" name="jobTitle"
            rules={[{ max: 200 }]}
            style={{ flex: 1 }}
          >
            <Input placeholder="Không bắt buộc" />
          </Form.Item>
        </div>

        {/* Row 2.5: [AUTH Phase 4] Initial password — create mode only */}
        {!isEditing && (
          <Form.Item
            label="Mật khẩu ban đầu" name="password"
            rules={[
              { required: true, message: 'Vui lòng nhập mật khẩu ban đầu' },
              { min: 8, message: 'Mật khẩu phải có ít nhất 8 ký tự' },
            ]}
            extra="Người dùng sẽ bị yêu cầu đổi mật khẩu này ở lần đăng nhập đầu tiên."
          >
            <Input.Password placeholder="Mật khẩu ban đầu (tối thiểu 8 ký tự)" autoComplete="new-password" />
          </Form.Item>
        )}

        {/* Company — CompanyTreeSelect (self-loads tree, keeps cascade via handleCompanyChange) */}
        <Form.Item
          label="Công ty"
          name="companyId"
          tooltip="Chọn công ty để bật Phòng ban và Địa điểm"
        >
          <CompanyTreeSelect placeholder="Chọn công ty (không bắt buộc)" onChange={handleCompanyChange} />
        </Form.Item>

        {/* Row 4: Department + Location (disabled until company selected) */}
        <div style={fieldFlex}>
          <Form.Item
            label="Phòng ban"
            name="departmentId"
            style={{ flex: 1 }}
            tooltip={!hasCompany ? 'Chọn công ty trước' : undefined}
          >
            <Select
              {...selectProps}
              disabled={!hasCompany}
              loading={fetchingDeps}
              placeholder={hasCompany ? 'Chọn phòng ban' : 'Chọn công ty trước'}
              options={departmentOptions.map((d) => ({ value: d.id, label: d.name }))}
              notFoundContent={hasCompany ? 'Chưa có phòng ban' : 'Chọn công ty trước'}
            />
          </Form.Item>
          <Form.Item
            label="Địa điểm"
            name="locationId"
            style={{ flex: 1 }}
            tooltip={!hasCompany ? 'Chọn công ty trước' : undefined}
          >
            <Select
              {...selectProps}
              disabled={!hasCompany}
              loading={fetchingDeps}
              placeholder={hasCompany ? 'Chọn địa điểm' : 'Chọn công ty trước'}
              options={locationOptions.map((l) => ({ value: l.id, label: l.name }))}
              notFoundContent={hasCompany ? 'Chưa có địa điểm' : 'Chọn công ty trước'}
            />
          </Form.Item>
        </div>

        {/* Switches */}
        <Space size="large" style={{ marginBottom: 16 }}>
          <Form.Item label="Kích hoạt" name="isActive" valuePropName="checked">
            <Switch checkedChildren="Hoạt động" unCheckedChildren="Ngừng hoạt động" />
          </Form.Item>
          <Form.Item
            label="Quản trị viên cấp cao"
            name="isSuperUser"
            valuePropName="checked"
            tooltip="Quyền quản trị cao nhất trong hệ thống"
          >
            <Switch checkedChildren="Có" unCheckedChildren="Không" />
          </Form.Item>
        </Space>
      </Form>
    </ModalFormShell>
  );
};

export default UserFormModal;