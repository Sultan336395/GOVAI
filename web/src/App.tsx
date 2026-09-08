import { Navigate, Route, Routes } from 'react-router-dom'
import AppLayout from '@/app/AppLayout'
import CompanyProvider from '@/app/CompanyProvider'
import { useAuth } from '@/app/contexts'
import { isPlatformRole } from '@/app/navigation'
import CompanyEditPage from '@/pages/CompanyEditPage'
import CompanyGroupsPage from '@/pages/CompanyGroupsPage'
import CompanyMembersPage from '@/pages/CompanyMembersPage'
import CompanyPage from '@/pages/CompanyPage'
import QuarantinePage from '@/pages/QuarantinePage'
import CatalogRepairPage from '@/pages/CatalogRepairPage'
import RuleEvidenceBackfillPage from '@/pages/RuleEvidenceBackfillPage'
import RegulatoryChangeDetailPage from '@/pages/RegulatoryChangeDetailPage'
import RegulatoryChangesPage from '@/pages/RegulatoryChangesPage'
import DashboardPage from '@/pages/DashboardPage'
import EligibilityDetailPage from '@/pages/EligibilityDetailPage'
import ActivatePage from '@/pages/ActivatePage'
import LoginPage from '@/pages/LoginPage'
import MatchesPage from '@/pages/MatchesPage'
import MyCompaniesPage from '@/pages/MyCompaniesPage'
import NewCompanyPage from '@/pages/NewCompanyPage'
import NotificationsPage from '@/pages/NotificationsPage'
import OpportunitiesPage from '@/pages/OpportunitiesPage'
import SimulationPage from '@/pages/SimulationPage'
import SourcesPage from '@/pages/SourcesPage'

function ProtectedRoutes() {
  const { isAuthenticated } = useAuth()

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />
  }

  return (
    <CompanyProvider>
      <AppLayout />
    </CompanyProvider>
  )
}

/**
 * Giriş sonrası açılan ekran role göre değişir.
 *
 * Panel ana ekranı bir şirketin panosudur; platform hesaplarının şirket üyeliği yoktur
 * ve sol menülerinde şirket ekranı da yoktur. Onları buraya bırakmak "Önce bir firma
 * seçin" diyen, seçilecek firma da sunmayan bir çıkmaz üretiyordu. Platform hesabı
 * kendi ilk ekranına, ortak kataloğa iner.
 */
function HomeRoute() {
  const { user } = useAuth()

  // İnceleyicinin menüsünde katalog ekranı YOK; oraya yönlendirmek onu menüde
  // bulunmayan bir sayfaya düşürürdü. Rolün ilk işi karantina incelemesidir.
  if (user?.role === 'PlatformReviewer') {
    return <Navigate to="/quarantine" replace />
  }

  if (isPlatformRole(user?.role ?? null)) {
    return <Navigate to="/opportunities" replace />
  }

  return <DashboardPage />
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      {/* Aktivasyon oturum gerektirmez: hesap henüz parolasızdır. */}
      <Route path="/activate/:token" element={<ActivatePage />} />
      <Route element={<ProtectedRoutes />}>
        <Route path="/" element={<HomeRoute />} />
        <Route path="/matches" element={<MatchesPage />} />
        <Route path="/matches/:assessmentId" element={<EligibilityDetailPage />} />
        <Route path="/opportunities" element={<OpportunitiesPage />} />

        {/* Çoklu şirket (Faz 1). "groups" ve "new", :companyId'den önce tanımlanır. */}
        <Route path="/companies" element={<MyCompaniesPage />} />
        <Route path="/companies/new" element={<NewCompanyPage />} />
        <Route path="/companies/groups" element={<CompanyGroupsPage />} />
        <Route path="/companies/:companyId/edit" element={<CompanyEditPage />} />
        <Route path="/companies/:companyId/members" element={<CompanyMembersPage />} />
        {/* Aynı ekran, şirket kimliği olmadan da açılabilir: aktif şirkete düşer,
            o da yoksa "önce bir şirket seçin" der. İkinci bir kopya değildir. */}
        <Route path="/companies/members" element={<CompanyMembersPage />} />

        <Route path="/company" element={<CompanyPage />} />
        <Route path="/simulation" element={<SimulationPage />} />
        <Route path="/notifications" element={<NotificationsPage />} />
        <Route path="/sources" element={<SourcesPage />} />

        {/* Faz 2 — RegTech */}
        <Route path="/regulatory-changes" element={<RegulatoryChangesPage />} />
        <Route path="/regulatory-changes/:changeId" element={<RegulatoryChangeDetailPage />} />
        <Route path="/quarantine" element={<QuarantinePage />} />

        {/* Faz 3 — Platform İnceleme bakım işlemleri */}
        <Route path="/platform/catalog-repair" element={<CatalogRepairPage />} />
        <Route path="/platform/rule-evidence" element={<RuleEvidenceBackfillPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
