import { NavLink, Outlet } from 'react-router-dom'
import { useAuth, useCompanies } from '@/app/contexts'
import { companyRoleLabels } from '@/lib/companyLabels'

const navItems = [
  { to: '/', label: 'Panel', end: true },
  { to: '/matches', label: 'Fırsat eşleşmeleri' },
  { to: '/opportunities', label: 'Çağrı kataloğu' },
  { to: '/companies', label: 'Şirketlerim' },
  { to: '/companies/groups', label: 'Şirket grupları' },
  { to: '/company', label: 'Firma profili' },
  { to: '/simulation', label: 'Senaryo simülasyonu' },
  { to: '/notifications', label: 'Bildirimler' },
  { to: '/sources', label: 'Veri kaynakları' },
]

export default function AppLayout() {
  const { user, logout } = useAuth()
  const { companies, selectedCompanyId, selectCompany, isSwitching, activeRole } = useCompanies()

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          GOVAI
          <small>Fırsat Karar Destek</small>
        </div>

        {companies.length > 0 ? (
          <div className="field" style={{ marginBottom: 0 }}>
            <label htmlFor="company-select">Aktif şirket</label>
            <select
              id="company-select"
              value={selectedCompanyId ?? ''}
              disabled={isSwitching}
              onChange={(e) => void selectCompany(e.target.value)}
            >
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.shortName ?? company.legalName}
                </option>
              ))}
            </select>
            <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>
              {isSwitching
                ? 'Şirket değiştiriliyor…'
                : activeRole
                  ? companyRoleLabels[activeRole]
                  : null}
            </div>
          </div>
        ) : null}

        <nav className="nav">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) => (isActive ? 'active' : '')}
            >
              {item.label}
            </NavLink>
          ))}
        </nav>

        <div style={{ marginTop: 'auto' }}>
          <div className="muted" style={{ fontSize: 12, marginBottom: 8 }}>
            {user?.fullName}
            <br />
            {user?.email}
          </div>
          <button type="button" onClick={logout} style={{ width: '100%' }}>
            Çıkış yap
          </button>
        </div>
      </aside>

      <main className="content">
        <Outlet />
      </main>
    </div>
  )
}
