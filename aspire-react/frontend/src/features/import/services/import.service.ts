import apiClient from '../../../services/api-client';

/** One processed row from the backend per-row import report. */
export interface ImportRow {
  rowNumber: number;
  success: boolean;
  message: string;
}

/** Aggregated outcome of one import request (mirrors ImportSheetResult). */
export interface ImportResult {
  status: string;
  created: number;
  failed: number;
  /** Every processed row — used for the full per-row report table. */
  rows?: ImportRow[];
  /** Failed rows subset (kept for backward compat / quick display). */
  errors?: ImportRow[];
}

export type ImportType = 'reference' | 'assets' | 'components' | 'accessories' | 'consumables';

/**
 * POST /api/v1/import/<type> — multipart/form-data: the .xlsx file + the SELECTED company id.
 * The backend RE-VALIDATES companyId against the acting user's real scope (never trusts the
 * client): out-of-scope company → 403; missing company → 400 COMPANY_REQUIRED.
 * ONE import = ONE company: every created record + its ActionLog gets exactly this CompanyId.
 */
export async function importExcel(type: ImportType, file: File, companyId: string): Promise<ImportResult> {
  const form = new FormData();
  form.append('file', file);
  form.append('companyId', companyId);
  const res = await apiClient.post(`/import/${type}`, form, {
    headers: { 'Content-Type': 'multipart/form-data' },
    timeout: 120000, // large workbooks may take a while
  });
  return res.data as ImportResult;
}

/** GET /api/v1/import/templates/assets — dynamic empty .xlsx skeleton (7 sheets, headers only). */
export async function downloadImportTemplate(): Promise<void> {
  const res = await apiClient.get('/import/templates/assets', { responseType: 'blob' });
  const url = URL.createObjectURL(res.data as Blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = 'import-template.xlsx';
  a.click();
  URL.revokeObjectURL(url);
}
