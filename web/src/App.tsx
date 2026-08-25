import { Navigate, Route, Routes } from 'react-router-dom'
import AppLayout from '@/app/AppLayout'
import CompanyProvider from '@/app/CompanyProvider'
import { useAuth } from '@/app/contexts'
import CompanyEditPage from '@/pages/CompanyEditPage'
import CompanyGroupsPage from '@/pages/CompanyGroupsPage'
import CompanyMembersPage from '@/pages/CompanyMembersPage'
import CompanyPage from '@/pages/CompanyPage'
import DashboardPage from '@/pages/DashboardPage'
import EligibilityDetailPage from '@/pages/EligibilityDetailPage'
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

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<ProtectedRoutes />}>
        <Route path="/" element={<DashboardPage />} />
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
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
