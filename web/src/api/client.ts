import type {
  ComplianceLeverage,
  EvidencePortfolio,
  OpportunityAnalysis,
  RegulationImpactAnalysis,
  ActiveCompanyResult,
  CompanyDetail,
  CompanyGroup,
  CompanyInvitation,
  CompanyInvitationResult,
  CompanyMember,
  CompanyRole,
  CompanySummary,
  CreateCompanyRequest,
  CreateCompanyResult,
  Dashboard,
  EligibilityDetail,
  LoginResponse,
  Notification,
  CompleteActivationRequest,
  OpportunityDetail,
  PlatformActivationStatus,
  OpportunityMatch,
  OpportunitySummary,
  PagedResult,
  MyCompany,
  ScenarioRequest,
  ScenarioResult,
  ManualImportResult,
  NaceOption,
  QuarantinedDocument,
  QuarantinedDocumentDetail,
  CatalogRepairPlanReport,
  CatalogRepairReport,
  RuleEvidenceBackfillReport,
  RegulatoryChangeDetail,
  RegulatoryChangeSummary,
  SectorOption,
  SourceDto,
  TriageReport,
  TenantUser,
  UpdateCompanyHierarchyRequest,
  VerificationRequest,
  WeeklyReportSummary,
  WeeklyReportDetail,
  ReportInquiryState,
  AnswerQuestionResult,
  DashboardInsights,
  TenderBoard,
  TenderPursuit,
  TenderPursuitStatus,
  TenderOutcome,
  ExpertVerdict,
  CalibrationReport,
  RecordExpertVerdictRequest,
  AiSecondOpinionResult,
  ErpConnection,
  UpsertErpConnectionRequest,
  ErpPullResult,
  ErpFieldMap,
  ErpVendor,
} from './types'

const TOKEN_STORAGE_KEY = 'govai.token'

/** Vite proxy'si /api isteklerini backend'e yönlendirir; üretimde tam URL verilir. */
const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? ''

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly problem?: unknown,
    /** Alan adı → hata mesajları. Formların ilgili alanın altına yazması için. */
    readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_STORAGE_KEY),
  set: (token: string) => localStorage.setItem(TOKEN_STORAGE_KEY, token),
  clear: () => localStorage.removeItem(TOKEN_STORAGE_KEY),
}

/**
 * @param acceptStatuses Başarısız sayılmayacak durum kodları. Bazı uçlar "hata değil ama
 * 200 de değil" bir sonuç döner — örneğin mükerrer şirket kaydı 409 ile birlikte ne
 * yapılabileceğini anlatan bir gövde döner. Bunları istisnaya çevirmek o mesajı kaybettirir.
 */
async function request<T>(
  path: string,
  init: RequestInit = {},
  acceptStatuses: number[] = [],
): Promise<T> {
  const token = tokenStore.get()

  const response = await fetch(`${BASE_URL}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init.headers,
    },
  })

  if (response.status === 401) {
    tokenStore.clear()
    throw new ApiError(401, 'Oturum süresi doldu, lütfen tekrar giriş yapın.')
  }

  if (!response.ok && !acceptStatuses.includes(response.status)) {
    const problem = await response.json().catch(() => undefined)
    const shape = problem as
      | { detail?: string; title?: string; errors?: Record<string, string[]> }
      | undefined

    const fieldErrors = shape?.errors ?? {}

    // Doğrulama hatalarında anlamlı metin "errors" içindedir; başlık ("Girdi doğrulaması
    // başarısız") tek başına kullanıcıya ne yapması gerektiğini söylemez.
    const firstFieldMessage = Object.values(fieldErrors).flat()[0]

    const detail =
      shape?.detail ??
      firstFieldMessage ??
      shape?.title ??
      `İstek başarısız (${response.status})`

    throw new ApiError(response.status, detail, problem, fieldErrors)
  }

  // 202 (Accepted) ve 204 gibi gövdesiz yanıtlar da başarılıdır; boş gövdede json() patlar.
  const body = await response.text()

  return (body ? (JSON.parse(body) as T) : (undefined as T))
}

function query(params: Record<string, unknown>): string {
  const search = new URLSearchParams()

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue

    if (Array.isArray(value)) {
      value.forEach((item) => search.append(key, String(item)))
    } else {
      search.append(key, String(value))
    }
  }

  const serialized = search.toString()
  return serialized ? `?${serialized}` : ''
}

export const api = {
  // ---- kimlik ----
  login: (email: string, password: string) =>
    request<LoginResponse>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    }),

  // ---- firma profili ----
  listCompanies: () => request<CompanySummary[]>('/api/company-profile'),

  getCompany: (companyId: string) => request<CompanyDetail>(`/api/company-profile/${companyId}`),

  // ---- fırsatlar ----
  searchOpportunities: (params: {
    search?: string
    categories?: string[]
    onlyOpen?: boolean
    page?: number
    pageSize?: number
  }) => request<PagedResult<OpportunitySummary>>(`/api/opportunities${query(params)}`),

  // ---- uygunluk ----
  /** Uyum eksiklerinin fırsat karşılığı: neyi kapatırsam hangi çağrıya girerim. */
  getComplianceLeverage: (companyId: string) =>
    request<ComplianceLeverage>(`/api/leverage/companies/${companyId}`),

  /** Firmanın kanıt portföyü: hangi belge hâlâ güvenilir, hangisi eskimiş. */
  getEvidencePortfolio: (companyId: string) =>
    request<EvidencePortfolio>(`/api/evidence/companies/${companyId}`),

  listMatches: (
    companyId: string,
    params: {
      minScore?: number
      verdicts?: string[]
      categories?: string[]
      deadlineWithinDays?: number
      page?: number
      pageSize?: number
    } = {},
  ) =>
    request<PagedResult<OpportunityMatch>>(
      `/api/eligibility/companies/${companyId}/matches${query(params)}`,
    ),

  // ---- DeepTech analiz (Faz 3) ----

  /**
   * Şirket–fırsat analizi. POST'tur çünkü sonuç üretir ve sürümlü bir analiz kaydı
   * yazar; aynı sürümlerle çağrıldığında yeni kayıt oluşmaz (idempotent).
   */
  analyzeOpportunity: (companyId: string, opportunityId: string) =>
    request<OpportunityAnalysis>(
      `/api/analysis/companies/${companyId}/opportunities/${opportunityId}`,
      { method: 'POST' },
    ),

  analyzeRegulation: (companyId: string, regulatoryChangeId: string) =>
    request<RegulationImpactAnalysis>(
      `/api/analysis/companies/${companyId}/regulations/${regulatoryChangeId}`,
      { method: 'POST' },
    ),

  // ---- referans katalogları (form önerileri) ----

  searchSectors: (q?: string) => request<SectorOption[]>(`/api/reference/sectors${query({ q })}`),

  /** `sector` birden çok kez gönderilir; liste o sektörlerin kodlarıyla sınırlanır. */
  searchNace: (q: string, sector?: string[]) =>
    request<NaceOption[]>(`/api/reference/nace${query({ q, sector })}`),

  // ---- platform hesabı aktivasyonu (oturum gerektirmez) ----

  getActivationStatus: (token: string) =>
    request<PlatformActivationStatus>(`/api/platform/activations/${encodeURIComponent(token)}`),

  completeActivation: (body: CompleteActivationRequest) =>
    request<PlatformActivationStatus>('/api/platform/activations/complete', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  getEligibilityDetail: (assessmentId: string) =>
    request<EligibilityDetail>(`/api/eligibility/${assessmentId}`),

  /** Fırsatın kendi künyesi, kanıt zinciri ve resmî bağlantısı. */
  getOpportunity: (opportunityId: string) =>
    request<OpportunityDetail>(`/api/opportunities/${opportunityId}`),

  rescore: (companyId: string) =>
    request<{ evaluatedOpportunityCount: number; eligibleCount: number; averageScore: number }>(
      `/api/eligibility/companies/${companyId}/rescore`,
      { method: 'POST' },
    ),

  generateSummary: (assessmentId: string) =>
    request<{ assessmentId: string; summary: string }>(
      `/api/eligibility/${assessmentId}/summary`,
      { method: 'POST' },
    ),

  // ---- skorlama ve simülasyon ----
  simulate: (companyId: string, scenario: ScenarioRequest, persist = false) =>
    request<ScenarioResult>(
      `/api/scoring/companies/${companyId}/simulate${query({ persist })}`,
      { method: 'POST', body: JSON.stringify(scenario) },
    ),

  // ---- raporlar ----
  getDashboard: (companyId: string) =>
    request<Dashboard>(`/api/reports/companies/${companyId}/dashboard`),

  exportUrl: (companyId: string, format: 'excel' | 'pdf') =>
    `${BASE_URL}/api/reports/companies/${companyId}/export/${format}`,

  // ---- haftalık rapor ----
  listWeeklyReports: (companyId: string, limit?: number) =>
    request<WeeklyReportSummary[]>(
      `/api/reports/weekly/companies/${companyId}${query({ limit })}`,
    ),

  getWeeklyReport: (reportId: string) =>
    request<WeeklyReportDetail>(`/api/reports/weekly/${reportId}`),

  /** Dönem verilmezse tamamlanmış son hafta alınır. Aynı hafta ikinci kayıt açmaz. */
  generateWeeklyReport: (companyId: string, weekOf?: string) =>
    request<WeeklyReportDetail>(`/api/reports/weekly/companies/${companyId}`, {
      method: 'POST',
      body: JSON.stringify({ weekOf: weekOf ?? null }),
    }),

  weeklyReportExportUrl: (reportId: string, format: 'excel' | 'pdf') =>
    `${BASE_URL}/api/reports/weekly/${reportId}/${format}`,

  /** Sorulabilir sorular, sorulmuş cevaplar ve kalan hak. */
  getReportQuestions: (reportId: string) =>
    request<ReportInquiryState>(`/api/reports/weekly/${reportId}/questions`),

  /**
   * Seçilen soruyu cevaplatır.
   *
   * Yalnızca anahtar gönderilir; serbest metin yoktur. Daha önce sorulmuş bir soru
   * yeniden açılırsa kayıtlı cevap döner ve hak harcanmaz.
   */
  answerReportQuestion: (reportId: string, questionKey: string, parentInquiryId?: string | null) =>
    request<AnswerQuestionResult>(`/api/reports/weekly/${reportId}/questions`, {
      method: 'POST',
      body: JSON.stringify({ questionKey, parentInquiryId: parentInquiryId ?? null }),
    }),

  /** Dashboard'un dört ek bölümü. Ana dashboard sayılarından ayrı bir uçtur. */
  getDashboardInsights: (companyId: string) =>
    request<DashboardInsights>(`/api/reports/companies/${companyId}/dashboard/insights`),

  // ---- ihale başvuru takibi ----

  getTenderBoard: (companyId: string) =>
    request<TenderBoard>(`/api/tenders/companies/${companyId}`),

  /** Zaten takipteyse ikinci kayıt açılmaz; mevcut kayıt döner. */
  startTenderPursuit: (companyId: string, opportunityId: string, note?: string | null) =>
    request<TenderPursuit>(`/api/tenders/companies/${companyId}`, {
      method: 'POST',
      body: JSON.stringify({ opportunityId, note: note ?? null }),
    }),

  /** Sonuçlanan ihalede `outcome` zorunludur; sunucu aksini 422 ile reddeder. */
  changeTenderStatus: (
    pursuitId: string,
    status: TenderPursuitStatus,
    outcome?: TenderOutcome | null,
    note?: string | null,
  ) =>
    request<TenderPursuit>(`/api/tenders/${pursuitId}/status`, {
      method: 'POST',
      body: JSON.stringify({ status, outcome: outcome ?? null, note: note ?? null }),
    }),

  // ---- uzman değerlendirmesi ve kalibrasyon ----

  /** Uzman görüşü skoru DEĞİŞTİRMEZ; yalnızca ölçüm için saklanır. */
  recordExpertVerdict: (body: RecordExpertVerdictRequest) =>
    request<ExpertVerdict>('/api/calibration/verdicts', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  listExpertVerdicts: (companyId: string) =>
    request<ExpertVerdict[]>(`/api/calibration/companies/${companyId}/verdicts`),

  /** Firma verilmezse kiracının tamamı ölçülür. */
  getCalibrationReport: (companyId?: string) =>
    request<CalibrationReport>(`/api/calibration/report${query({ companyId })}`),

  /** Yapay zekâdan bağımsız ikinci görüş toplar. Skoru DEĞİŞTİRMEZ. */
  collectAiSecondOpinions: (companyId: string) =>
    request<AiSecondOpinionResult>(`/api/calibration/companies/${companyId}/ai-review`, {
      method: 'POST',
    }),

  // ---- ERP bağlantısı ----

  /**
   * Bağlantı kurulmamışsa sunucu 204 döner.
   *
   * `request` gövdesiz yanıtta `undefined` üretir; React Query ise `undefined` veriyi
   * HATA sayar ve ekranda "data is undefined" yazar. "Bağlantı yok" bir hata değil,
   * geçerli bir durumdur — bu yüzden açıkça `null`a çevrilir.
   */
  getErpConnection: async (companyId: string) =>
    (await request<ErpConnection | null>(`/api/erp/companies/${companyId}/connection`)) ?? null,

  upsertErpConnection: (companyId: string, body: UpsertErpConnectionRequest) =>
    request<ErpConnection>(`/api/erp/companies/${companyId}/connection`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  pullErpProfile: (companyId: string) =>
    request<ErpPullResult>(`/api/erp/companies/${companyId}/pull`, { method: 'POST' }),

  /** Ürün varsayılan eşlemesi. Alan adlarının tek kaynağı sunucudur. */
  getErpFieldMapDefaults: (vendor: ErpVendor) =>
    request<ErpFieldMap>(`/api/erp/field-map-defaults/${vendor}`),

  deleteErpConnection: (companyId: string) =>
    request<void>(`/api/erp/companies/${companyId}/connection`, { method: 'DELETE' }),

  // ---- bildirimler ----
  listNotifications: (params: { companyId?: string; onlyUnread?: boolean; pageSize?: number }) =>
    request<PagedResult<Notification>>(`/api/notifications${query(params)}`),

  markNotificationRead: (id: string) =>
    request<Notification>(`/api/notifications/${id}/read`, { method: 'POST' }),

  // ---- çoklu şirket (Faz 1) ----

  /** Kullanıcının üyeliği olan şirketler. Kiracının tümü değil — yalnızca erişebildikleri. */
  listMyCompanies: () => request<MyCompany[]>('/api/companies'),

  // 409 = vergi numarası bu çalışma alanında zaten kayıtlı, 202 = doğrulama talebi açıldı.
  // İkisi de kullanıcıya gösterilecek anlamlı sonuçtur; hata olarak ele alınmaz.
  createCompany: (body: CreateCompanyRequest) =>
    request<CreateCompanyResult>(
      '/api/companies',
      { method: 'POST', body: JSON.stringify(body) },
      [409],
    ),

  updateCompany: (companyId: string, body: CreateCompanyRequest) =>
    request<MyCompany>(`/api/companies/${companyId}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  updateCompanyHierarchy: (companyId: string, body: UpdateCompanyHierarchyRequest) =>
    request<MyCompany>(`/api/companies/${companyId}/hierarchy`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  listCompanyGroups: () => request<CompanyGroup[]>('/api/companies/groups'),

  createCompanyGroup: (body: { name: string; description?: string | null }) =>
    request<CompanyGroup>('/api/companies/groups', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateCompanyGroup: (groupId: string, body: { name: string; description?: string | null }) =>
    request<CompanyGroup>(`/api/companies/groups/${groupId}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  listVerificationRequests: () =>
    request<VerificationRequest[]>('/api/companies/verification-requests'),

  // ---- şirket kullanıcıları ----
  listCompanyMembers: (companyId: string) =>
    request<CompanyMember[]>(`/api/companies/${companyId}/members`),

  addCompanyMember: (companyId: string, body: { userId: string; companyRole: CompanyRole }) =>
    request<CompanyMember>(`/api/companies/${companyId}/members`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  changeCompanyMemberRole: (companyId: string, membershipId: string, companyRole: CompanyRole) =>
    request<CompanyMember>(`/api/companies/${companyId}/members/${membershipId}/role`, {
      method: 'PUT',
      body: JSON.stringify({ companyRole }),
    }),

  removeCompanyMember: (companyId: string, membershipId: string) =>
    request<void>(`/api/companies/${companyId}/members/${membershipId}`, { method: 'DELETE' }),

  setDefaultCompany: (companyId: string) =>
    request<void>(`/api/companies/${companyId}/members/default`, { method: 'POST' }),

  listCompanyInvitations: (companyId: string) =>
    request<CompanyInvitation[]>(`/api/companies/${companyId}/members/invitations`),

  createCompanyInvitation: (
    companyId: string,
    body: { email: string; companyRole: CompanyRole; validForDays?: number },
  ) =>
    request<CompanyInvitationResult>(`/api/companies/${companyId}/members/invitations`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  revokeCompanyInvitation: (companyId: string, invitationId: string) =>
    request<void>(`/api/companies/${companyId}/members/invitations/${invitationId}`, {
      method: 'DELETE',
    }),

  /** Aktif şirketi sunucuda değiştirir ve yeni jetonu döner. */
  setActiveCompany: (companyId: string) =>
    request<ActiveCompanyResult>('/api/auth/active-company', {
      method: 'POST',
      body: JSON.stringify({ companyId }),
    }),

  /** Yalnızca kiracı yöneticisi çağırabilir; üye eklerken kullanıcı seçimi için. */
  listTenantUsers: () => request<TenantUser[]>('/api/admin/users'),

  // ---- mevzuat (Faz 2) ----

  /** Doğrulanmış mevzuat değişiklikleri. Karantinadaki kayıt dönmez. */
  listRegulatoryChanges: (params: { domain?: string; jurisdiction?: string } = {}) =>
    request<RegulatoryChangeSummary[]>(`/api/regulatory-changes${query(params)}`),

  getRegulatoryChange: (changeId: string) =>
    request<RegulatoryChangeDetail>(`/api/regulatory-changes/${changeId}`),

  // ---- karantina (yalnızca platform incelemesi) ----
  listQuarantined: () => request<QuarantinedDocument[]>('/api/quarantine'),

  /** apply=false yalnızca rapor üretir, hiçbir şeyi değiştirmez. */
  runTriage: (apply = false) =>
    request<TriageReport>(`/api/quarantine/triage${query({ apply })}`, { method: 'POST' }),

  // ── Platform İnceleme: bakım işlemleri (Faz 3) ─────────────────────────
  //
  // İki adım AYRIDIR: plan hiçbir şey yazmaz, apply uygular. Uygulama isteği
  // planın parmak izini taşır — bu kural sunucuda da zorunludur, arayüzde
  // gevşetilemez.

  catalogRepairPlan: () => request<CatalogRepairPlanReport>('/api/sources/catalog-repair/plan'),

  applyCatalogRepair: (planHash: string) =>
    request<CatalogRepairReport>('/api/sources/catalog-repair/apply', {
      method: 'POST',
      body: JSON.stringify({ planHash }),
    }),

  undoCatalogRepair: (runId: string) =>
    request<CatalogRepairReport>(`/api/sources/catalog-repair/runs/${runId}/undo`, {
      method: 'POST',
    }),

  ruleEvidenceBackfillPlan: (batchSize = 100, after?: string) =>
    request<RuleEvidenceBackfillReport>(
      `/api/opportunities/rule-evidence/backfill/plan${query({ batchSize, after })}`,
    ),

  applyRuleEvidenceBackfill: (planHash: string, batchSize = 100, after?: string) =>
    request<RuleEvidenceBackfillReport>(
      `/api/opportunities/rule-evidence/backfill/apply${query({ batchSize, after })}`,
      { method: 'POST', body: JSON.stringify({ planHash }) },
    ),

  undoRuleEvidenceBackfill: (runId: string) =>
    request<{ removedLinkCount: number }>(
      `/api/opportunities/rule-evidence/backfill/runs/${runId}/undo`,
      { method: 'POST' },
    ),

  /** Bir belgenin tam incelemesi: künye, sürümler, ayrıştırma durumu, metin. */
  getQuarantinedDocument: (documentId: string) =>
    request<QuarantinedDocumentDetail>(`/api/quarantine/${documentId}`),

  approveQuarantined: (documentId: string) =>
    request<void>(`/api/quarantine/${documentId}/approve`, { method: 'POST' }),

  rejectQuarantined: (documentId: string, reason: string, note?: string) =>
    request<void>(`/api/quarantine/${documentId}/reject`, {
      method: 'POST',
      body: JSON.stringify({ reason, note }),
    }),

  /**
   * Kontrollü manuel içe aktarma: resmî ilan adresinden tek bir kaydı alır.
   * Bu bir tarama DEĞİLDİR; kaynak doğrulanmış sayılmaz.
   */
  manualImport: (sourceId: string, url: string, title?: string) =>
    request<ManualImportResult>(`/api/sources/${sourceId}/manual-import`, {
      method: 'POST',
      body: JSON.stringify({ url, title: title || null }),
    }),

  // ---- kaynaklar ----
  listSources: () => request<SourceDto[]>('/api/sources'),

  setSourceEnabled: (sourceId: string, enabled: boolean) =>
    request<SourceDto>(`/api/sources/${sourceId}/enabled`, {
      method: 'POST',
      body: JSON.stringify({ enabled }),
    }),

  triggerCrawl: (sourceId: string) =>
    request<void>(`/api/sources/${sourceId}/crawl`, { method: 'POST' }),
}
