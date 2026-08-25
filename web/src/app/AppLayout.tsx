import { useEffect, useMemo, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import type { CompanyRole } from '@/api/types'
import { canCreateCompany, companyPermissions, useAuth, useCompanies } from '@/app/contexts'
import { buildNavigation } from '@/app/navigation'
import NavIcon from '@/components/NavIcons'
import { companyRoleLabels } from '@/lib/companyLabels'

export default function AppLayout() {
  const { user, logout } = useAuth()
  const { companies, selectedCompanyId, selectCompany, isSwitching, activeRole } = useCompanies()
  const location = useLocation()

  // Dar ekranda menü ikon şeridine iner; açıldığında içeriğin üstüne biner.
  const [isNavOpen, setIsNavOpen] = useState(false)

  // Bağlantıya basınca şerit kapanmalı, yoksa açılan menü sayfayı örtmeye devam eder.
  useEffect(() => setIsNavOpen(false), [location.pathname])

  const canManageAnyCompany = companies.some((company) =>
    companyPermissions.manageProfile(company.companyRole),
  )

  // Aktif şirket çözülene kadar menünün yerinde durması için: varsayılan, yoksa ilk
  // sahip/yönetici olunan şirket.
  const isOwnerOrManager = (role: CompanyRole) =>
    role === 'CompanyOwner' || role === 'CompanyManager'

  const fallbackOwnedCompanyId =
    (companies.find((c) => c.isDefault && isOwnerOrManager(c.companyRole)) ??
      companies.find((c) => isOwnerOrManager(c.companyRole)))?.id ?? null

  const groups = useMemo(
    () =>
      buildNavigation({
        userRole: user?.role ?? null,
        activeRole,
        activeCompanyId: selectedCompanyId,
        canManageAnyCompany,
        fallbackOwnedCompanyId,
      }),
    [user?.role, activeRole, selectedCompanyId, canManageAnyCompany, fallbackOwnedCompanyId],
  )

  // Şirket ekleme kiracı işlemidir; aktif şirketteki role bakılmaz.
  const canAddCompany = canCreateCompany(user?.role ?? null, companies)

  return (
    <div className="app-shell" data-nav={isNavOpen ? 'open' : 'closed'}>
      {isNavOpen ? (
        <button
          type="button"
          className="nav-backdrop"
          aria-label="Menüyü kapat"
          onClick={() => setIsNavOpen(false)}
        />
      ) : null}

      <aside className="sidebar" aria-label="Ana gezinme">
        <div className="sidebar-head">
          <div className="brand">
            GOVAI
            <small>Fırsat Karar Destek</small>
          </div>

          <button
            type="button"
            className="nav-toggle"
            aria-expanded={isNavOpen}
            aria-label={isNavOpen ? 'Menüyü kapat' : 'Menüyü aç'}
            onClick={() => setIsNavOpen((open) => !open)}
          >
            <NavIcon name={isNavOpen ? 'close' : 'menu'} />
          </button>
        </div>

        <div className="sidebar-scroll">
          {companies.length > 0 ? (
            <section className="workspace" aria-label="Çalışılan şirket">
              <h2 className="workspace-title">Çalışılan Şirket</h2>

              <label className="sr-only" htmlFor="company-select">
                Çalışılan şirketi seçin
              </label>
              <select
                id="company-select"
                className="workspace-select"
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

              <div className="workspace-meta" aria-live="polite">
                {isSwitching ? (
                  <span className="role-badge role-badge-muted">Değiştiriliyor…</span>
                ) : activeRole ? (
                  <span className="role-badge">{companyRoleLabels[activeRole]}</span>
                ) : null}
              </div>

              <div className="workspace-actions">
                <NavLink to="/companies" end>
                  Şirketlerim
                </NavLink>
                {canAddCompany ? <NavLink to="/companies/new">Yeni Şirket Ekle</NavLink> : null}
              </div>
            </section>
          ) : null}

          <nav className="nav">
            {groups.map((group) => (
              <div className="nav-group" key={group.title}>
                <p className="nav-group-title">{group.title}</p>

                {group.items.map((item) => (
                  <NavLink
                    key={item.to}
                    to={item.to}
                    end={item.end}
                    title={item.label}
                    className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}
                  >
                    <NavIcon name={item.icon} className="nav-icon" />
                    <span className="nav-label">{item.label}</span>
                  </NavLink>
                ))}
              </div>
            ))}
          </nav>
        </div>

        <div className="sidebar-foot">
          <div className="who">
            {user?.fullName}
            <br />
            {user?.email}
          </div>
          <button type="button" className="logout" onClick={logout} title="Çıkış yap">
            <span className="nav-label">Çıkış yap</span>
            <span className="logout-short" aria-hidden="true">
              ⏻
            </span>
          </button>
        </div>
      </aside>

      <main className="content">
        <Outlet />
      </main>
    </div>
  )
}
