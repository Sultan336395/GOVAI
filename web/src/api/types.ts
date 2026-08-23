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
