import { useCurrentUser } from './useCurrentUser';
import { usePermissionMap } from './usePermission';

/**
 * [DF-8.3] Shared company-field visibility for create/edit forms.
 *
 * A company-scoped user must not choose another company; the server fills/keeps the company
 * on create/update. The field is also hidden when the user lacks companies.view, so the form
 * never triggers a 403 GET /companies.
 */
export function useCompanyField() {
  const currentUser = useCurrentUser();
  const permissionMap = usePermissionMap();
  const canViewCompanies = !!permissionMap
    && (permissionMap.isSuperUser || (permissionMap.permissions['companies.view'] ?? 0) === 1);
  const isCompanyScoped = !!currentUser && !currentUser.isSuperUser && !!currentUser.companyId;
  const showCompanyField = !isCompanyScoped && canViewCompanies;
  return { currentUser, canViewCompanies, isCompanyScoped, showCompanyField };
}
