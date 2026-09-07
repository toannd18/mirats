import { Navigate, useLocation } from 'react-router-dom';
import { isAuthenticated } from '../features/auth/services/auth';

interface ProtectedRouteProps {
  children: React.ReactNode;
}

/**
 * [AUTH Phase 2] Route guard — redirects unauthenticated users to the local /login page
 * (replaces the Keycloak full-page redirect). Preserves the intended destination so login
 * can navigate back to it.
 */
const ProtectedRoute: React.FC<ProtectedRouteProps> = ({ children }) => {
  const location = useLocation();

  if (!isAuthenticated()) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }

  return <>{children}</>;
};

export default ProtectedRoute;
