/**
 * GOVAI API sözleşmesinin TypeScript karşılığı.
 * Backend'deki enum'lar JSON'a string olarak serileştirilir (JsonStringEnumConverter).
 */

export type LegalType =
  | 'Unknown'
  | 'SoleProprietorship'
  | 'LimitedCompany'
  | 'JointStockCompany'
  | 'Cooperative'
  | 'Association'
  | 'Foundation'
  | 'PublicEntity'

export type EnterpriseSize = 'Micro' | 'Small' | 'Medium' | 'Large'

export type SourceType =
  | 'OfficialGazette'
  | 'Ministry'
  | 'DevelopmentAgency'
  | 'KosgebOrSimilar'
  | 'TenderPortal'
  | 'EuOrInternational'
  | 'Other'

export type SupportCategory =
  | 'EmploymentIncentive'
  | 'InvestmentIncentive'
  | 'Grant'
  | 'RndSupport'
  | 'DigitalTransformation'
  | 'ExportSupport'
  | 'GreenTransformation'
  | 'Tender'
  | 'Loan'
  | 'Other'

export type EligibilityVerdict =
  | 'Eligible'
  | 'ConditionallyEligible'
  | 'NotEligible'
  | 'Indeterminate'

/**
 * Sektör uyumu — listedeki sıralamanın birincil ölçütü.
 *
 * 'Unverified' ile 'NotMatched' ayrı tutulur: birincisi "bilmiyoruz", ikincisi
 * "biliyoruz ve tutmuyor". İkisi de listeden ÇIKARILMAZ, yalnızca sona iner.
 */
export type SectorFit = 'Matched' | 'Unverified' | 'NotMatched'

export type RuleDimension =
  | 'Sector'
  | 'Financial'
  | 'Employment'
  | 'Documentation'
  | 'Region'
  | 'TechnicalQualification'
  | 'Timing'

export type RuleSeverity = 'Blocking' | 'Major' | 'Minor' | 'Bonus'

export type RuleOutcome = 'Satisfied' | 'NotSatisfied' | 'Unknown' | 'NotApplicable'

export type DocumentStatus = 'Missing' | 'Provided' | 'Expired' | 'NotRequired'

export type NotificationKind =
  | 'DeadlineApproaching'
  | 'NewMatch'
  | 'ScoreChanged'
  | 'RegulationChanged'
  | 'DocumentMissing'
  | 'SystemAlert'

export type UserRole =
  | 'SuperAdmin'
  | 'CompanyManager'
  | 'OperationUser'
  | 'Consultant'
  | 'ReadOnly'
  // Platform işletim rolleri (Faz 1). Kiracı yöneticisi bunları atayamaz.
  | 'PlatformCatalogManager'
  | 'PlatformReviewer'
  | 'SystemIngest'

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

export interface LoginResponse {
  accessToken: string
  expiresAt: string
  refreshToken: string
  /** Sunucunun üyelikten belirlediği aktif şirket. Üyeliği yoksa null. */
  activeCompanyId: string | null
  user: {
    id: string
    tenantId: string
    email: string
    fullName: string
    role: UserRole
    isActive: boolean
    lastLoginAt: string | null
  }
}

export interface CompanySummary {
  id: string
  legalName: string
  taxNumber: string
  legalType: LegalType
  size: EnterpriseSize
  primaryNaceCode: string | null
  employeeCount: number
  annualRevenue: number
  lastSyncedAt: string | null
  profileVersion: number
}

export interface Workforce {
  employeeCount: number
  womenEmployeeCount: number
  youngEmployeeCount: number
  /** Firmanın genç çalışanı sayarken kullandığı azami yaş; sistem varsaymaz. */
  youngEmployeeMaxAge?: number | null
  rAndDEmployeeCount: number
  disabledEmployeeCount: number
}

export interface Financials {
  annualRevenue: number
  balanceSize: number
  equity: number
  exportRevenue: number
  currency: string
  fiscalYear: number | null
}

export interface CompanyDetail {
  id: string
  legalName: string
  taxNumber: string
  legalType: LegalType
  size: EnterpriseSize
  foundedOn: string | null
  workforce: Workforce
  financials: Financials
  exportFlag: boolean
  technologyFlag: boolean
  previousSuccessfulApplications: number
  naceCodes: { code: string; isPrimary: boolean; description: string | null }[]
  locations: {
    city: string
    district: string | null
    nuts2Code: string | null
    isHeadquarters: boolean
    isInTechnopark: boolean
  }[]
  certificates: {
    code: string
    name: string
    issuedOn: string | null
    validUntil: string | null
    documentUri: string | null
  }[]
  activeInvestments: {
    title: string
    relatedCategory: SupportCategory
    plannedBudget: number
    plannedStart: string | null
    plannedEnd: string | null
  }[]
  lastSyncedAt: string | null
  profileVersion: number
  /** 0..1 aralığında profil doluluk oranı. */
  profileCompleteness: number
}

/** Platform hesabı aktivasyon bağlantısının durumu. */
export interface PlatformActivationStatus {
  isRedeemable: boolean
  email: string | null
  reason: string | null
}

export interface CompleteActivationRequest {
  token: string
  password: string
  passwordConfirmation: string
}

export interface OpportunitySummary {
  id: string
  title: string
  publisher: string
  sourceType: SourceType
  supportCategory: SupportCategory
  publishedAt: string
  deadline: string | null
  daysUntilDeadline: number | null
  maxAmount: number | null
  currency: string | null
  isReviewedByConsultant: boolean
  ruleCount: number
  documentCount: number
}

/** Bir alanın neden boş olduğu (Faz 2). C# karşılığı: FieldAvailabilityDto. */
export interface FieldAvailability {
  deadline: FieldAvailabilityState
  budget: FieldAvailabilityState
  currency: FieldAvailabilityState
  eligibleApplicant: FieldAvailabilityState
  geography: FieldAvailabilityState
  sector: FieldAvailabilityState
  programmeType: FieldAvailabilityState
  officialDocumentUrl: FieldAvailabilityState
}

export type FieldAvailabilityState = 'Provided' | 'NotProvided' | 'NotApplicable'

/** C# karşılığı: RuleOperator. */
export type RuleOperator =
  | 'Equals'
  | 'NotEquals'
  | 'GreaterThan'
  | 'GreaterThanOrEqual'
  | 'LessThan'
  | 'LessThanOrEqual'
  | 'In'
  | 'NotIn'
  | 'ContainsAll'
  | 'ContainsAny'
  | 'NaceMatch'
  | 'IsTrue'
  | 'IsFalse'

/** Fırsatın kural taslağı. C# karşılığı: OpportunityRuleDto. */
/** Kanıtın kural içindeki kullanım türü. */
export type RuleEvidenceRole = 'ValueSource' | 'ConditionText' | 'Supporting'

/** Bir kuralın dayandığı resmî kanıt parçası; zincirin görünen halkası. */
export interface RuleEvidence {
  evidenceChunkId: string
  documentVersionId: string
  role: RuleEvidenceRole
  roleLabel: string
  startOffset: number
  endOffset: number
  pageNumber: number | null
  sectionTitle: string | null
  text: string
  textHash: string
  officialUrl: string | null
  documentVersionNumber: number | null
}

export interface OpportunityRule {
  id: string
  field: string
  operator: RuleOperator
  value: string
  dimension: RuleDimension
  severity: RuleSeverity
  humanReadable: string
  sourceExcerpt: string | null
  confidence: number
  isManuallyOverridden: boolean
  /** Kuralın dayandığı resmî kanıt parçaları. Bir kural birden çok parçaya dayanabilir. */
  evidence: RuleEvidence[]
  /** Kanıt yoksa yapay zekâ bu kural hakkında resmî kaynağa dayalı iddia üretemez. */
  supportsAiClaims: boolean
}

export interface DocumentRequirement {
  code: string
  name: string
  isMandatory: boolean
  issuingAuthority: string | null
  notes: string | null
}

export interface Budget {
  minAmount: number | null
  maxAmount: number | null
  currency: string
  supportRate: number | null
}

/**
 * Fırsatın nereden geldiğinin kanıtı (Faz 2).
 *
 * `officialUrl` yalnızca adres kaynağın resmî alan adındaysa dolar; aksi hâlde
 * `null` gelir ve "Resmî kaynağa git" düğmesi GÖSTERİLMEZ. Sebebi
 * `officialUrlRejectionReason` alanında durur.
 */
export interface OpportunityProvenance {
  sourceId: string
  sourceName: string
  sourceCategory: SourceCategory
  sourceVerified: boolean
  sourceVerifiedAt: string | null
  sourceHealth: SourceHealth
  officialDomain: string | null
  officialUrl: string | null
  officialUrlRejectionReason: string | null
  documentId: string | null
  documentVersion: number | null
  canonicalUrl: string | null
  contentHash: string | null
  normalizedTextHash: string | null
  charset: string | null
  mediaType: string | null
  retrievedAt: string | null
  parseStatus: DocumentParseStatus | null
  requiresOcr: boolean
  pageCount: number | null
  parseError: string | null
  evidence: EvidenceChunk[]
}

/** Fırsat detay ekranının verisi. C# karşılığı: OpportunityDetailDto. */
/** Türü belirlenmiş tek bir tutar. Etiket olmadan "1.500.000 TL" hibe mi kredi mi belli değildir. */
export interface BudgetItem {
  type: string
  label: string
  amount: number
  currency: string
  excerpt: string | null
  startOffset: number
  endOffset: number
  /** Türü belirlenemedi; hibe gibi gösterilemez. */
  needsReview: boolean
}

export interface BudgetRate {
  type: string
  label: string
  rate: number
  excerpt: string | null
  startOffset: number
  endOffset: number
  needsReview: boolean
}

export interface OpportunityDetail {
  id: string
  title: string
  publisher: string
  summary: string | null
  sourceUrl: string | null
  sourceType: SourceType
  supportCategory: SupportCategory
  publishedAt: string
  deadline: string | null
  daysUntilDeadline: number | null
  budget: Budget | null
  /** Türü belirlenmiş tutarlar; hibe ile krediyi ayırt eden tek kaynak. */
  budgetItems: BudgetItem[]
  budgetRates: BudgetRate[]
  /** Çağrının mevzuat dayanağı, metinde geçtiği biçimiyle. */
  legalBasis: string | null
  ruleExtractionConfidence: number
  isReviewedByConsultant: boolean
  rules: OpportunityRule[]
  documentChecklist: DocumentRequirement[]
  fieldAvailability: FieldAvailability
  /** Son başvuru tarihi geçmişse false; süresi geçmiş çağrı açık gösterilmez. */
  isOpen: boolean
  provenance: OpportunityProvenance | null
  /** Karantinadaysa nedeni; değilse 'None'. İnceleme ekranı bunu gösterir. */
  quarantineReason: QuarantineReason
  quarantineNote: string | null
}

/**
 * Faaliyet sektörü seçeneği (referans kataloğu).
 *
 * Sektör ve NACE kodu artık yazılmaz, listeden seçilir. Liste sunucudan gelir:
 * arayüz ile kayıt doğrulaması aynı kataloğu kullanmazsa, arayüzde geçerli görünen
 * bir seçim sunucuda reddedilir.
 */
export interface SectorOption {
  name: string
  /** Sektörün kapsadığı iki haneli NACE bölümleri. */
  divisions: string[]
}

/** NACE Rev. 2 kod seçeneği. */
export interface NaceOption {
  code: string
  title: string
  sector: string
}

export interface OpportunityMatch {
  assessmentId: string
  opportunityId: string
  opportunityTitle: string
  publisher: string
  supportCategory: SupportCategory
  deadline: string | null
  daysUntilDeadline: number | null
  finalScore: number
  confidence: number
  verdict: EligibilityVerdict
  sectorFit: SectorFit
  missingConditionCount: number
  missingMandatoryDocumentCount: number
  dataGapCount: number
  maxAmount: number | null
  executiveSummary: string | null
  evaluatedAt: string
}

export interface DimensionScore {
  dimension: RuleDimension
  dimensionLabel: string
  value: number
  weight: number
  contribution: number
  evaluatedRuleCount: number
  unknownRuleCount: number
  rationale: string
}

export interface RuleEvaluation {
  field: string
  dimension: RuleDimension
  severity: RuleSeverity
  outcome: RuleOutcome
  requirement: string
  actualValue: string
  expectedValue: string
  strength: number
  sourceExcerpt: string | null
  suggestedAction: string | null
}

export interface DocumentCheck {
  code: string
  name: string
  isMandatory: boolean
  status: DocumentStatus
  validUntil: string | null
  issuingAuthority: string | null
  action: string | null
}

export interface EligibilityDetail {
  assessmentId: string
  companyId: string
  companyName: string
  opportunityId: string
  opportunityTitle: string
  publisher: string
  sourceUrl: string | null
  deadline: string | null
  verdict: EligibilityVerdict
  sectorFit: SectorFit
  finalScore: number
  confidence: number
  hasBlockingFailure: boolean
  evaluatedAt: string
  companyProfileVersion: number
  dimensions: DimensionScore[]
  blockingFailures: RuleEvaluation[]
  missingConditions: RuleEvaluation[]
  satisfiedConditions: RuleEvaluation[]
  dataGaps: RuleEvaluation[]
  documentChecklist: DocumentCheck[]
  executiveSummary: string | null
}

export interface Dashboard {
  companyId: string
  companyName: string
  profileCompleteness: number
  totalEvaluatedOpportunities: number
  eligibleCount: number
  conditionallyEligibleCount: number
  notEligibleCount: number
  indeterminateCount: number
  averageScore: number
  closingWithin15Days: number
  missingMandatoryDocumentTotal: number
  dataGapTotal: number
  categoryBreakdown: {
    category: SupportCategory
    categoryLabel: string
    count: number
    eligibleCount: number
    averageScore: number
  }[]
  dimensionAverages: { dimension: RuleDimension; label: string; averageValue: number }[]
  topOpportunities: OpportunityMatch[]
  closingSoon: OpportunityMatch[]
}

export interface ScenarioRequest {
  name: string
  employeeCount?: number
  womenEmployeeCount?: number
  youngEmployeeCount?: number
  rAndDEmployeeCount?: number
  disabledEmployeeCount?: number
  annualRevenue?: number
  balanceSize?: number
  equity?: number
  exportRevenue?: number
  exportFlag?: boolean
  technologyFlag?: boolean
  addCertificateCodes?: string[]
  removeCertificateCodes?: string[]
  categories?: SupportCategory[]
}

export interface ScenarioImpact {
  opportunityId: string
  opportunityTitle: string
  supportCategory: SupportCategory
  baselineScore: number
  simulatedScore: number
  delta: number
  baselineVerdict: EligibilityVerdict
  simulatedVerdict: EligibilityVerdict
  becameEligible: boolean
}

export interface ScenarioResult {
  simulationId: string | null
  companyId: string
  name: string
  evaluatedOpportunityCount: number
  baselineEligibleCount: number
  simulatedEligibleCount: number
  baselineAverageScore: number
  simulatedAverageScore: number
  impacts: ScenarioImpact[]
  eligibleCountDelta: number
  averageScoreDelta: number
  newlyEligible: ScenarioImpact[]
}

export interface Notification {
  id: string
  kind: NotificationKind
  title: string
  body: string
  companyId: string | null
  opportunityId: string | null
  channel: 'InApp' | 'Email' | 'Webhook'
  createdAt: string
  sentAt: string | null
  isRead: boolean
}

export interface SourceDto {
  id: string
  name: string
  type: SourceType
  baseUrl: string
  cronExpression: string
  isEnabled: boolean
  lastRunAt: string | null
  lastRunStatus: 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Skipped'
  lastRunMessage: string | null
  consecutiveFailureCount: number
  configurationJson: string | null

  // ── Faz 2 künyesi ──
  category: SourceCategory
  health: SourceHealth
  configurationVerified: boolean
  configurationVerifiedAt: string | null
  lastSuccessfulRunAt: string | null
  isCrawlable: boolean
  authority: string | null
  jurisdiction: string | null
  officialDomain: string | null
  language: string | null
  startUrl: string | null
  listSelector: string | null
  contentSelector: string | null
  urlPattern: string | null
  maxPages: number
  allowedDomains: string | null
  documentTypes: string | null
}

// ══════════════════════════ Faz 1 — çoklu şirket ══════════════════════════
// C# karşılığı: GovAI.Application/Companies/MultiCompanyDtos.cs
// Bu blok elle senkronize edilir (bkz. CLAUDE.md §3).

export type CompanyRole = 'CompanyOwner' | 'CompanyManager' | 'CompanyExpert' | 'CompanyViewer'

export type CompanyRelationshipType =
  | 'Independent'
  | 'HeadCompany'
  | 'Subsidiary'
  | 'Affiliate'
  | 'Branch'
  | 'GroupCompany'

export type CreateCompanyOutcome = 'Created' | 'AlreadyInWorkspace' | 'VerificationRequired'

export type VerificationRequestStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled'

/** Şirket ekleme ve düzenleme formunun gövdesi. */
export interface CreateCompanyRequest {
  legalName: string
  taxNumber: string
  country: string
  mainSector: string
  primaryNaceCode: string

  shortName?: string | null
  taxOffice?: string | null
  mersisNumber?: string | null
  tradeRegistryNumber?: string | null

  legalType?: LegalType
  foundedOn?: string | null

  secondaryNaceCodes?: string[]
  subSectors?: string[]
  targetCountries?: string[]

  employeeCount?: number
  rAndDEmployeeCount?: number
  womenEmployeeCount?: number
  /** İsteğe bağlı; boş bırakılırsa "belirtilmemiş" sayılır. Yalnızca toplu sayı. */
  youngEmployeeCount?: number
  /** Firmanın genç çalışanı sayarken kullandığı azami yaş; sistem varsaymaz. */
  youngEmployeeMaxAge?: number | null
  disabledEmployeeCount?: number

  annualRevenue?: number
  balanceSize?: number

  isInTechnopark?: boolean
  exportFlag?: boolean

  website?: string | null
  phone?: string | null
  corporateEmail?: string | null
  city?: string | null
  address?: string | null

  groupId?: string | null
  parentCompanyId?: string | null
  relationshipType?: CompanyRelationshipType
  isHeadCompany?: boolean
}

export interface CreateCompanyResult {
  outcome: CreateCompanyOutcome
  message: string
  companyId?: string | null
  canNavigateToExisting: boolean
  verificationRequestId?: string | null
}

export interface MyCompany {
  id: string
  legalName: string
  shortName: string | null
  taxNumber: string
  legalType: LegalType
  size: EnterpriseSize
  primaryNaceCode: string | null
  mainSector: string | null
  city: string | null
  employeeCount: number
  annualRevenue: number
  profileCompletionPercentage: number
  isActive: boolean
  groupId: string | null
  groupName: string | null
  parentCompanyId: string | null
  parentCompanyName: string | null
  relationshipType: CompanyRelationshipType
  isHeadCompany: boolean
  companyRole: CompanyRole
  isDefault: boolean
}

export interface CompanyGroup {
  id: string
  name: string
  description: string | null
  isActive: boolean
  companyCount: number
}

export interface UpdateCompanyHierarchyRequest {
  groupId?: string | null
  parentCompanyId?: string | null
  relationshipType: CompanyRelationshipType
  isHeadCompany: boolean
}

export interface CompanyMember {
  membershipId: string
  userId: string
  email: string
  fullName: string
  companyRole: CompanyRole
  isActive: boolean
  isDefault: boolean
  createdAt: string
}

export interface CompanyInvitation {
  id: string
  email: string
  companyRole: CompanyRole
  expiresAt: string
  acceptedAt: string | null
  revokedAt: string | null
  isRedeemable: boolean
}

/** Davet jetonunun açık metni yalnızca oluşturma yanıtında, bir kez döner. */
export interface CompanyInvitationResult {
  invitationId: string
  email: string
  companyRole: CompanyRole
  expiresAt: string
  token: string
}

export interface ActiveCompanyResult {
  companyId: string
  companyName: string
  companyRole: CompanyRole
  accessToken: string
  expiresAt: string
}

export interface VerificationRequest {
  id: string
  taxNumber: string
  requestedLegalName: string
  status: VerificationRequestStatus
  requestedAt: string
  resolvedAt: string | null
  resolutionNote: string | null
}

export interface TenantUser {
  id: string
  tenantId: string
  email: string
  fullName: string
  role: UserRole
  isActive: boolean
  lastLoginAt: string | null
}

// ══════════════════════ Faz 2 — RegTech ══════════════════════
// C# karşılıkları: GovAI.Application/Regulatory ve GovAI.Application/Sources.

export type RegulationDomain =
  | 'Tax' | 'SocialSecurity' | 'LabourLaw' | 'CommercialLaw'
  | 'DataProtection' | 'CorporateGovernance' | 'Other'

export type RegulatoryChangeType =
  | 'NewRegulation' | 'Amendment' | 'Repeal' | 'Circular'
  | 'Communique' | 'Decision' | 'Guidance' | 'Announcement'

export type RegulatoryChangeStatus = 'Detected' | 'Verified' | 'Superseded' | 'Quarantined'

export type QuarantineReason =
  | 'None' | 'InvalidSourcePage' | 'Duplicate'
  | 'MissingRequiredFields' | 'ParserFailed' | 'NeedsManualReview'

export type DocumentOrigin = 'Crawl' | 'ManualImport'

export type SourceCategory =
  | 'Uncategorized'
  | 'Regulation' | 'Tax' | 'SocialSecurity' | 'LabourLaw' | 'CommercialLaw'
  | 'Grant' | 'Incentive' | 'Fund' | 'Tender' | 'EuProgramme'

export type SourceHealth = 'Unverified' | 'Healthy' | 'Degraded' | 'Failing'

export type DocumentParseStatus = 'Pending' | 'Parsed' | 'Failed' | 'NeedsOcr'

export interface RegulatoryChangeSummary {
  id: string
  title: string
  regulationDomain: RegulationDomain
  authority: string
  jurisdiction: string
  changeType: RegulatoryChangeType
  publicationDate: string | null
  effectiveDate: string | null
  status: RegulatoryChangeStatus
  officialUrl: string
  detectedAt: string
}

/** Kanıt parçası; metin resmî belgeye geri gösterilebilir. */
export interface EvidenceChunk {
  sequenceNumber: number
  pageNumber: number | null
  sectionTitle: string | null
  paragraphNumber: number | null
  text: string
  startOffset: number
  endOffset: number
  /** Parçanın metninin SHA-256'sı; kanıtın değişmediği bununla gösterilir. */
  textHash: string
  /** Parçanın kimliği; kural–kanıt zinciri bu alanla eşleştirilir. */
  evidenceChunkId: string
}

export interface RegulatoryChangeDetail extends RegulatoryChangeSummary {
  officialNumber: string | null
  summary: string | null
  lastVerifiedAt: string
  previousVersionId: string | null
  sourceName: string
  documentVersion: number
  canonicalUrl: string
  charset: string | null
  mediaType: string
  httpStatusCode: number
  retrievedAt: string
  parseStatus: DocumentParseStatus
  requiresOcr: boolean
  pageCount: number | null
  evidence: EvidenceChunk[]
}

/** Karantinadaki kayıt (PlatformReviewer inceleme ekranı). */
export interface QuarantinedDocument {
  documentId: string
  title: string
  url: string
  sourceName: string
  reason: QuarantineReason
  note: string | null
  collectedAt: string
  versionCount: number
  origin: DocumentOrigin
  /** Başlık yanlış karakter kümesiyle kaydedilmişti; ekranda gösterilen metin onarıldı. */
  titleRepaired: boolean
  /** Bu belgeden türeyen fırsat kaydı; yoksa null. Detaya bu kimlikle gidilir. */
  opportunityId: string | null
}

export interface TriageRow {
  documentId: string
  title: string
  url: string
  sourceName: string
  proposedReason: QuarantineReason
  evidence: string
  missingField: string | null
  isScored: boolean
  linkedAssessmentCount: number
  recommendedAction: string
}

export interface TriageReport {
  reviewed: number
  clean: number
  flagged: number
  applied: number
  affectedOpportunities: number
  affectedAssessments: number
  rows: TriageRow[]
}

export interface ManualImportResult {
  documentId: string
  isNew: boolean
  revision: number
  finalUrl: string
  httpStatusCode: number
  contentLength: number
  sourceName: string
  note: string
}

// ───────────────────────── DeepTech analiz (Faz 3) ─────────────────────────

/**
 * Kriter sonucu. Beş değerlidir: "bilmiyorum" ile "hayır" ve "belgede çelişki var"
 * ayrı şeylerdir. Kullanıcı üçüne farklı tepki verir — biri veri tamamlamayı, biri
 * başvurudan vazgeçmeyi, biri resmî kaynaktan doğrulamayı gerektirir.
 */
export type CriterionOutcome =
  | 'Met'
  | 'NotMet'
  | 'Unknown'
  | 'NotApplicable'
  | 'ConflictingEvidence'

export type ScoreGroup =
  | 'Mandatory'
  | 'SectorNace'
  | 'ScaleAndFinancials'
  | 'Geography'
  | 'Workforce'
  | 'Timing'
  | 'DocumentsAndConditions'

export type ConfidenceLevel = 'Low' | 'Medium' | 'High'

export type AiAnalysisStatus = 'AIUnavailable' | 'Succeeded' | 'InvalidOutput' | 'Error'

export type RegulationImpact =
  | 'Unknown'
  | 'Applicable'
  | 'NotApplicable'
  | 'PotentiallyApplicable'

export interface CriterionEvidence {
  evidenceChunkId: string | null
  documentVersionId: string | null
  excerpt: string
  locator: string | null
}

export interface CriterionResult {
  code: string
  name: string
  isMandatory: boolean
  outcome: CriterionOutcome
  outcomeLabel: string
  rationale: string
  companyFields: string[]
  evidence: CriterionEvidence[]
  scoreImpact: number
  missingOrConflictExplanation: string | null
  ruleSetVersion: string
  group: ScoreGroup
  groupName: string
}

export interface ScoreComponent {
  group: ScoreGroup
  name: string
  value: number
  weight: number
  contribution: number
  criterionCount: number
  metCount: number
  notMetCount: number
  unknownCount: number
  conflictCount: number
  rationale: string
}

export interface ExplainableScore {
  value: number
  /** Her zaman "Uygunluk puanı"; hiçbir yerde kazanma ihtimali yazmaz. */
  label: string
  components: ScoreComponent[]
  hasMandatoryFailure: boolean
  missingDataEffect: number
  ruleSetVersion: string
}

export interface ConfidenceFactor {
  code: string
  name: string
  value: number
  weight: number
  explanation: string
  notMeasured: boolean
}

export interface AnalysisConfidence {
  value: number
  level: ConfidenceLevel
  levelLabel: string
  factors: ConfidenceFactor[]
  ruleSetVersion: string
  /**
   * Göstergenin başlığı: yapay zekâ çalışmadıysa "Kural tabanlı güven".
   * "Yapay zekâ destekli güven" yazmak, model hiç çalışmamışken onun da doğruladığı izlenimi verir.
   */
  title: string
}

export interface RuleAiConflict {
  criterionCode: string
  ruleOutcome: CriterionOutcome
  claimType: string
  note: string
}

export interface AnalysisContribution {
  hasAiContribution: boolean
  aiStatus: AiAnalysisStatus
  aiStatusLabel: string
  aiExplanations: string[]
  rejectedClaimCount: number
  conflicts: RuleAiConflict[]
  warning: string | null
  /** "Kural tabanlı analiz" veya "Yapay zekâ destekli analiz". */
  modeLabel: string
  /** Model çalışmadıysa "Kullanılamıyor"; sıfır gösterilmez. */
  aiConfidenceLabel: string
}

export interface AnalysisVersion {
  analysisRunId: string
  companyProfileVersion: number
  financialDataVersion: number
  ruleSetVersion: string
  promptVersion: string | null
  modelProvider: string | null
  modelName: string | null
  outputSchemaVersion: string | null
  correlationId: string
  startedAt: string
  completedAt: string | null
  status: string
}

export interface OpportunityAnalysis {
  companyId: string
  opportunityId: string
  opportunityTitle: string
  evaluatedAt: string
  verdict: EligibilityVerdict
  verdictLabel: string
  sectorFit: SectorFit
  score: ExplainableScore
  confidence: AnalysisConfidence
  criteria: CriterionResult[]
  met: CriterionResult[]
  notMet: CriterionResult[]
  missing: CriterionResult[]
  conflicting: CriterionResult[]
  ruleSetVersion: string
  contribution: AnalysisContribution
  version: AnalysisVersion
}

export interface RegulationImpactAnalysis {
  companyId: string
  regulatoryChangeId: string
  regulationTitle: string
  evaluatedAt: string
  impact: RegulationImpact
  impactLabel: string
  confidence: AnalysisConfidence
  criteria: CriterionResult[]
  openQuestions: string[]
  legalDisclaimer: string
  ruleSetVersion: string
  contribution: AnalysisContribution
  version: AnalysisVersion
}

// ── Platform İnceleme: bakım işlemleri (Faz 3) ───────────────────────────────

/** Bir onarım adımının tek kayıt üzerindeki planı. */
export interface CatalogRepairMatch {
  stepCode: string
  target: 'Opportunity' | 'RegulatoryChange'
  action: 'Quarantine' | 'RetitleFromDocument'
  recordId: string
  currentTitle: string
  officialUrl: string | null
  /** Yeniden adlandırmada belge sürümünden okunan gerçek başlık. */
  proposedTitle: string | null
  willChange: boolean
  /** Değişmeyecekse sebebi. */
  skipReason: string | null
  affectedAssessmentCount: number
}

export interface CatalogRepairPlanReport {
  stepCount: number
  matchedRecordCount: number
  willChangeCount: number
  alreadyDoneCount: number
  /** Uygulama isteği bunu geri gönderir; görülmemiş plan uygulanamaz. */
  planHash: string
  matches: CatalogRepairMatch[]
}

export interface CatalogRepairOutcome {
  stepCode: string
  recordId: string
  result: string
  affectedAssessmentCount: number
  previousTitle: string | null
  target: 'Opportunity' | 'RegulatoryChange'
  action: 'Quarantine' | 'RetitleFromDocument'
}

export interface CatalogRepairReport {
  attempted: number
  changed: number
  alreadyDone: number
  outcomes: CatalogRepairOutcome[]
  /** Geri alma bu kimlikle yapılır; hiçbir şey değişmediyse null. */
  runId: string | null
}

export type RuleEvidenceBackfillOutcome =
  | 'Bound'
  | 'AlreadyBound'
  | 'NoEvidenceFound'
  | 'NeedsReparse'
  | 'NeedsRedownload'
  | 'SkippedQuarantined'
  | 'SkippedUnverifiedSource'
  | 'NoSourceDocument'
  | 'NoRules'
  | 'Failed'

export interface RuleEvidenceBackfillItem {
  opportunityId: string
  title: string
  outcome: RuleEvidenceBackfillOutcome
  explanation: string
  ruleCount: number
  rulesAlreadyBound: number
  rulesBound: number
  rulesWithoutEvidence: number
  documentVersionNumber: number | null
  documentVersionHash: string | null
  officialUrl: string | null
}

export interface RuleEvidenceBackfillReport {
  applied: boolean
  totalExamined: number
  boundCount: number
  alreadyBoundCount: number
  noEvidenceCount: number
  needsReparseCount: number
  needsRedownloadCount: number
  skippedQuarantinedCount: number
  skippedUnverifiedSourceCount: number
  noSourceDocumentCount: number
  failedCount: number
  evidenceLinksCreated: number
  nextCursor: string | null
  hasMore: boolean
  items: RuleEvidenceBackfillItem[]
  planHash: string
  runId: string | null
}

// ── Platform İnceleme: belge incelemesi (Faz 3) ──────────────────────────────

export interface DocumentVersion {
  versionId: string
  versionNumber: number
  retrievedAt: string
  httpStatusCode: number
  mediaType: string
  charset: string | null
  canonicalUrl: string
  /** Ham gövdenin SHA-256'sı; kanıt zinciri buna dayanır. */
  rawContentHash: string
  parseStatus: DocumentParseStatus
  requiresOcr: boolean
  parseError: string | null
  pageCount: number | null
  chunkCount: number
  title: string | null
}

export interface QuarantinedDocumentDetail {
  documentId: string
  title: string
  url: string
  canonicalUrl: string | null
  sourceId: string
  sourceName: string
  officialDomain: string | null
  reason: QuarantineReason
  note: string | null
  status: DocumentProcessingStatus
  processingError: string | null
  origin: DocumentOrigin
  collectedAt: string
  mediaType: string
  /** Ayrıştırılmış metin; uzun belgelerde kırpılır. */
  textPreview: string | null
  textLength: number
  /** Metin kırpıldıysa ekran bunu kullanıcıya söyler. */
  textTruncated: boolean
  versions: DocumentVersion[]
  opportunityId: string | null
  regulatoryChangeId: string | null
}

export type DocumentProcessingStatus =
  | 'Raw'
  | 'Parsed'
  | 'RulesExtracted'
  | 'Failed'
  | 'Discarded'
