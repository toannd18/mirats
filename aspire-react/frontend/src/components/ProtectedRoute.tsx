import { login, isAuthenticated } from '../services/keycloak';

interface ProtectedRouteProps {
  children: React.ReactNode;
}

const ProtectedRoute: React.FC<ProtectedRouteProps> = ({ children }) => {
  if (!isAuthenticated()) {
    // Redirect to Keycloak login directly instead of /login route
    login();
    return null;
  }

  return <>{children}</>;
};

export default ProtectedRoute;
