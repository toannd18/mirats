// ==================== User Types ====================

export interface UserDto {
  id: string;
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  employeeNumber: string | null;
  jobTitle: string | null;
  isSuperUser: boolean;
  isActive: boolean;
  companyId: string | null;
  companyName: string | null;
  departmentId: string | null;
  departmentName: string | null;
  locationId: string | null;
  locationName: string | null;
}

export interface CompanyNode {
  id: string;
  name: string;
  parentId: string | null;
  children?: CompanyNode[];
}

export interface ReferenceOption {
  id: string;
  name: string;
  companyId?: string;
}