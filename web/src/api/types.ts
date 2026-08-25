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
