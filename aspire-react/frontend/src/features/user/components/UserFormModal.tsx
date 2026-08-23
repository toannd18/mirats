import { useEffect, useState, useCallback } from 'react';
import {
  Modal, Form, Input, Select, Switch, Space, App,
} from 'antd';
import type { SelectProps } from 'antd';
import apiClient from '../../../services/api-client';
import type { UserDto, ReferenceOption } from '../types/users';
import { useIsMobile } from '../../../hooks/useIsMobile';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';

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
  isSuperUser: boolean;
  isActive: boolean;
  companyId: string | undefined;
  departmentId: string | undefined;
  locationId: string | undefined;
}

// ==================== Component ====================

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
      void message.error('Failed to load dependent data');
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
        employeeNumber: values.employeeNumber?.trim() || null,
        jobTitle: values.jobTitle?.trim() || null,
        isSuperUser: values.isSuperUser,
        isActive: values.isActive,
        companyId: values.companyId || null,
        departmentId: values.departmentId || null,
        locationId: values.locationId || null,
      };

      if (isEditing) {
        await apiClient.put(`/users/${user!.id}`, { id: user!.id, ...payload });
        void message.success('User updated successfully');
      } else {
        await apiClient.post('/users', payload);
        void message.success('User created successfully');
      }
      onSuccess();
    } catch (error: unknown) {
      const err = error as {
        response?: { status?: number; data?: { message?: string; errors?: Record<string, string[]> } };
      };
      if (err.response?.status === 409) {
        void message.error(err.response?.data?.message || 'Conflict: username or email already exists');
      } else if (err.response?.data?.errors) {
        const first = Object.values(err.response.data.errors).flat()[0];
        void message.error(first || 'Validation failed');
      } else if (err.response?.data?.message) {
        void message.error(err.response.data.message);
      } else {
        void message.error('An unexpected error occurred');
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
    <Modal
      title={isEditing ? 'Edit User' : 'Create New User'}
      open={open}
      onOk={handleSubmit}
      onCancel={onCancel}
      confirmLoading={submitting}
      width={isMobile ? '95%' : 640}
      destroyOnHidden
      centered
      mask={{ closable: false }}
      okText={isEditing ? 'Save Changes' : 'Create User'}
    >
      <Form form={form} layout="vertical" size="middle" autoComplete="off" style={{ marginTop: 8 }}>
        {/* Row 1: First Name + Last Name */}
        <div style={fieldFlex}>
          <Form.Item
            label="First Name" name="firstName"
            rules={[{ required: true, message: 'Required' }, { max: 100 }]}
            style={{ flex: 1 }}
          >
            <Input placeholder="First name" />
          </Form.Item>
          <Form.Item
            label="Last Name" name="lastName"
            rules={[{ required: true, message: 'Required' }, { max: 100 }]}
            style={{ flex: 1 }}
          >
            <Input placeholder="Last name" />
          </Form.Item>
        </div>

        {/* Row 2: Username + Email */}
        <div style={fieldFlex}>
          <Form.Item
            label="Username" name="username"
            rules={[
              { required: true, message: 'Required' },
              { max: 100 },
              { pattern: /^\S+$/, message: 'No spaces allowed' },
            ]}
            style={{ flex: 1 }}
          >
            <Input placeholder="Username" disabled={isEditing} />
          </Form.Item>
          <Form.Item
            label="Email" name="email"
            rules={[{ required: true, message: 'Required' }, { type: 'email' }, { max: 255 }]}
            style={{ flex: 1 }}
          >
            <Input placeholder="Email address" />
          </Form.Item>
        </div>

        {/* Row 3: Employee Number + Job Title */}
        <div style={fieldFlex}>
          <Form.Item
            label="Employee Number" name="employeeNumber"
            rules={[{ max: 50 }]}
            style={{ flex: 1 }}
          >
            <Input placeholder="Optional" />
          </Form.Item>
          <Form.Item
            label="Job Title" name="jobTitle"
            rules={[{ max: 200 }]}
            style={{ flex: 1 }}
          >
            <Input placeholder="Optional" />
          </Form.Item>
        </div>

        {/* Company — CompanyTreeSelect (self-loads tree, keeps cascade via handleCompanyChange) */}
        <Form.Item
          label="Company"
          name="companyId"
          tooltip="Select a company to enable Department and Location"
        >
          <CompanyTreeSelect placeholder="Select company (optional)" onChange={handleCompanyChange} />
        </Form.Item>

        {/* Row 4: Department + Location (disabled until company selected) */}
        <div style={fieldFlex}>
          <Form.Item
            label="Department"
            name="departmentId"
            style={{ flex: 1 }}
            tooltip={!hasCompany ? 'Select a company first' : undefined}
          >
            <Select
              {...selectProps}
              disabled={!hasCompany}
              loading={fetchingDeps}
              placeholder={hasCompany ? 'Select department' : 'Select company first'}
              options={departmentOptions.map((d) => ({ value: d.id, label: d.name }))}
              notFoundContent={hasCompany ? 'No departments found' : 'Select a company first'}
            />
          </Form.Item>
          <Form.Item
            label="Location"
            name="locationId"
            style={{ flex: 1 }}
            tooltip={!hasCompany ? 'Select a company first' : undefined}
          >
            <Select
              {...selectProps}
              disabled={!hasCompany}
              loading={fetchingDeps}
              placeholder={hasCompany ? 'Select location' : 'Select company first'}
              options={locationOptions.map((l) => ({ value: l.id, label: l.name }))}
              notFoundContent={hasCompany ? 'No locations found' : 'Select a company first'}
            />
          </Form.Item>
        </div>

        {/* Switches */}
        <Space size="large" style={{ marginBottom: 16 }}>
          <Form.Item label="Active" name="isActive" valuePropName="checked">
            <Switch checkedChildren="Active" unCheckedChildren="Inactive" />
          </Form.Item>
          <Form.Item
            label="Super User"
            name="isSuperUser"
            valuePropName="checked"
            tooltip="Adds user to superuser group in Keycloak"
          >
            <Switch checkedChildren="Yes" unCheckedChildren="No" />
          </Form.Item>
        </Space>
      </Form>
    </Modal>
  );
};

export default UserFormModal;