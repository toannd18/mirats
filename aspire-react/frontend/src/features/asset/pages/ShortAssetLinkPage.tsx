import { Navigate, useParams } from 'react-router-dom';

/**
 * Short QR route. Asset tags are editable after confirmation (UpdateAssetCommand does not lock
 * AssetTag), so the QR must use the immutable id and redirect to the canonical detail page.
 */
export default function ShortAssetLinkPage() {
  const { id } = useParams<{ id: string }>();
  if (!id) return <Navigate to="/assets" replace />;
  return <Navigate to={`/assets/${id}`} replace />;
}
