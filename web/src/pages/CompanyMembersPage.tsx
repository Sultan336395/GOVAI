import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { CompanyRole } from '@/api/types'
import { companyPermissions, useAuth, useCompanies } from '@/app/contexts'
import {
  EmptyState,
  ErrorBox,
  FieldError,
  InfoBox,
  Loading,
  NotAuthorized,
  SuccessBox,
} from '@/components/Common'
import { companyRoleHints, companyRoleLabels, companyRoleOrder } from '@/lib/companyLabels'
import { formatDate } from '@/lib/format'

/**
 * Şirket kullanıcıları ve yetkileri.
 *
 * Üye eklemenin iki yolu vardır: kiracıda zaten hesabı olan bir kullanıcıyı doğrudan
 * eklemek (yalnızca kiracı yöneticisi kullanıcı listesini görebildiği için ona açıktır)
 * ve e-posta ile davet. Davet jetonunun açık metni yalnızca oluşturma anında bir kez
 * gösterilir; sunucuda yalnızca özeti saklanır.
 */
export default function CompanyMembersPage() {
  const { companyId: routeCompanyId } = useParams<{ companyId?: string }>()
  const queryClient = useQueryClient()
  const { user } = useAuth()
  const { companies, selectedCompanyId, isLoading: companiesLoading, refresh } = useCompanies()

  const [notice, setNotice] = useState<string | null>(null)
  const [actionError, setActionError] = useState<unknown>(null)
  const [issuedToken, setIssuedToken] = useState<string | null>(null)

  const [inviteEmail, setInviteEmail] = useState('')
  const [inviteRole, setInviteRole] = useState<CompanyRole>('CompanyExpert')
  const [inviteError, setInviteError] = useState<string | undefined>()

  const [newMemberUserId, setNewMemberUserId] = useState('')
  const [newMemberRole, setNewMemberRole] = useState<CompanyRole>('CompanyExpert')

  // Yol şirket kimliği taşımıyorsa çalışılan şirkete düşülür; menü de bu yolu kullanır.
  const companyId = routeCompanyId ?? selectedCompanyId ?? undefined

  const company = companies.find((candidate) => candidate.id === companyId)
  const canManage = companyPermissions.manageMembers(company?.companyRole ?? null)

  const {
    data: members = [],
    isLoading: membersLoading,
    error: membersError,
  } = useQuery({
    queryKey: ['company-members', companyId],
    queryFn: () => api.listCompanyMembers(companyId!),
    enabled: Boolean(companyId) && Boolean(company),
  })

  const { data: invitations = [] } = useQuery({
    queryKey: ['company-invitations', companyId],
    queryFn: () => api.listCompanyInvitations(companyId!),
    enabled: Boolean(companyId) && canManage,
  })

  // Kullanıcı listesi yalnızca kiracı yöneticisine açıktır; değilse davet yolu kullanılır.
  const { data: tenantUsers = [] } = useQuery({
    queryKey: ['tenant-users'],
    queryFn: api.listTenantUsers,
    enabled: canManage && user?.role === 'SuperAdmin',
    retry: false,
  })

  const reload = async () => {
    await queryClient.invalidateQueries({ queryKey: ['company-members', companyId] })
    await queryClient.invalidateQueries({ queryKey: ['company-invitations', companyId] })
    await refresh()
  }

  const addMember = useMutation({
    mutationFn: () =>
      api.addCompanyMember(companyId!, { userId: newMemberUserId, companyRole: newMemberRole }),
    onSuccess: async (member) => {
      setNewMemberUserId('')
      setActionError(null)
      setNotice(`${member.fullName} şirkete eklendi.`)
      await reload()
    },
    onError: setActionError,
  })

  const changeRole = useMutation({
    mutationFn: (input: { membershipId: string; role: CompanyRole }) =>
      api.changeCompanyMemberRole(companyId!, input.membershipId, input.role),
    onSuccess: async (member) => {
      setActionError(null)
      setNotice(`${member.fullName} artık ${companyRoleLabels[member.companyRole]}.`)
      await reload()
    },
    onError: setActionError,
  })

  const removeMember = useMutation({
    mutationFn: (membershipId: string) => api.removeCompanyMember(companyId!, membershipId),
    onSuccess: async () => {
      setActionError(null)
      setNotice('Üyelik kaldırıldı.')
      await reload()
    },
    onError: setActionError,
  })

  const createInvitation = useMutation({
    mutationFn: () =>
      api.createCompanyInvitation(companyId!, { email: inviteEmail.trim(), companyRole: inviteRole }),
    onSuccess: async (result) => {
      setInviteEmail('')
      setInviteError(undefined)
      setActionError(null)
      setIssuedToken(result.token)
      setNotice(`${result.email} adresine davet oluşturuldu.`)
      await reload()
    },
    onError: (error: unknown) => {
      setInviteError(error instanceof Error ? error.message : 'Davet oluşturulamadı.')
    },
  })

  const revokeInvitation = useMutation({
    mutationFn: (invitationId: string) => api.revokeCompanyInvitation(companyId!, invitationId),
    onSuccess: async () => {
      setNotice('Davet iptal edildi.')
      await reload()
    },
    onError: setActionError,
  })

  if (companiesLoading || membersLoading) return <Loading />

  // Hiç şirket seçilmemişse ekran boş bir kimlikle açılmaz; kullanıcı yönlendirilir.
  if (!companyId) {
    return <EmptyState>Önce bir şirket seçin.</EmptyState>
  }

  if (!company) {
    return <EmptyState>Bu şirket listenizde yok veya erişiminiz kaldırılmış.</EmptyState>
  }

  if (!canManage) {
    return (
      <>
        <div className="page-header">
          <div>
            <h1>{company.legalName} — kullanıcılar</h1>
          </div>
        </div>
        <NotAuthorized message="Şirket kullanıcılarını yönetmek yalnızca şirket sahibinin yetkisindedir." />
      </>
    )
  }

  if (membersError) return <ErrorBox error={membersError} />

  const ownerCount = members.filter(
    (member) => member.isActive && member.companyRole === 'CompanyOwner',
  ).length

  return (
    <>
      <div className="page-header">
        <div>
          <h1>{company.legalName} — kullanıcılar ve yetkiler</h1>
          <p>
            Erişim yalnızca buradaki üyelik kaydından gelir. Bir üyelik kaldırıldığında,
            kullanıcının elindeki oturum jetonu hâlâ geçerli olsa bile erişimi hemen kesilir.
          </p>
        </div>
        <Link to={`/companies/${company.id}/edit`}>
          <button type="button">Şirket profili</button>
        </Link>
      </div>

      {notice ? <SuccessBox>{notice}</SuccessBox> : null}
      {actionError ? <ErrorBox error={actionError} /> : null}

      {issuedToken ? (
        <InfoBox>
          <strong>Davet bağlantısının anahtarı</strong>
          <p className="muted" style={{ margin: '6px 0' }}>
            Bu anahtar yalnızca şimdi görüntülenir; sunucuda açık hâliyle saklanmaz. Davet
            edilen kişiye siz iletirsiniz.
          </p>
          <code style={{ wordBreak: 'break-all' }}>{issuedToken}</code>
          <div className="toolbar" style={{ marginTop: 8 }}>
            <button type="button" onClick={() => setIssuedToken(null)}>
              Gizle
            </button>
          </div>
        </InfoBox>
      ) : null}

      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ marginTop: 0 }}>Üyeler</h2>

        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Kullanıcı</th>
                <th>Rol</th>
                <th>Durum</th>
                <th>Eklendiği tarih</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {members.map((member) => {
                const isLastOwner = member.companyRole === 'CompanyOwner' && ownerCount === 1

                return (
                  <tr key={member.membershipId}>
                    <td>
                      <strong>{member.fullName}</strong>
                      <div className="muted" style={{ fontSize: 12 }}>
                        {member.email}
                      </div>
                    </td>
                    <td>
                      <select
                        value={member.companyRole}
                        disabled={changeRole.isPending || isLastOwner}
                        onChange={(e) =>
                          changeRole.mutate({
                            membershipId: member.membershipId,
                            role: e.target.value as CompanyRole,
                          })
                        }
                      >
                        {companyRoleOrder.map((role) => (
                          <option key={role} value={role}>
                            {companyRoleLabels[role]}
                          </option>
                        ))}
                      </select>
                      <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>
                        {companyRoleHints[member.companyRole]}
                      </div>
                    </td>
                    <td>{member.isActive ? 'Etkin' : 'Pasif'}</td>
                    <td>{formatDate(member.createdAt)}</td>
                    <td>
                      <button
                        type="button"
                        disabled={removeMember.isPending || isLastOwner}
                        onClick={() => removeMember.mutate(member.membershipId)}
                      >
                        Kaldır
                      </button>
                      {isLastOwner ? (
                        <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>
                          Şirketin son sahibi kaldırılamaz.
                        </div>
                      ) : null}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </div>

      {tenantUsers.length > 0 ? (
        <div className="card" style={{ marginBottom: 16 }}>
          <h2 style={{ marginTop: 0 }}>Mevcut kullanıcıyı ekle</h2>

          <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))' }}>
            <div className="field">
              <label htmlFor="newMemberUserId">Kullanıcı</label>
              <select
                id="newMemberUserId"
                value={newMemberUserId}
                onChange={(e) => setNewMemberUserId(e.target.value)}
              >
                <option value="">Seçin</option>
                {tenantUsers
                  .filter((candidate) => !members.some((member) => member.userId === candidate.id))
                  .map((candidate) => (
                    <option key={candidate.id} value={candidate.id}>
                      {candidate.fullName} ({candidate.email})
                    </option>
                  ))}
              </select>
            </div>

            <div className="field">
              <label htmlFor="newMemberRole">Rol</label>
              <select
                id="newMemberRole"
                value={newMemberRole}
                onChange={(e) => setNewMemberRole(e.target.value as CompanyRole)}
              >
                {companyRoleOrder.map((role) => (
                  <option key={role} value={role}>
                    {companyRoleLabels[role]}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="toolbar">
            <button
              type="button"
              disabled={!newMemberUserId || addMember.isPending}
              onClick={() => addMember.mutate()}
            >
              {addMember.isPending ? 'Ekleniyor…' : 'Şirkete ekle'}
            </button>
          </div>
        </div>
      ) : null}

      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ marginTop: 0 }}>E-posta ile davet et</h2>
        <p className="muted" style={{ marginTop: 0 }}>
          Davet oluşturulduğunda güvenli bir anahtar üretilir. E-posta gönderimi bu aşamada
          sistemde açık değildir; anahtarı davet ettiğiniz kişiye siz iletirsiniz.
        </p>

        <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))' }}>
          <div className="field">
            <label htmlFor="inviteEmail">E-posta</label>
            <input
              id="inviteEmail"
              type="email"
              value={inviteEmail}
              onChange={(e) => {
                setInviteEmail(e.target.value)
                setInviteError(undefined)
              }}
              placeholder="kisi@firma.com"
            />
            <FieldError message={inviteError} />
          </div>

          <div className="field">
            <label htmlFor="inviteRole">Rol</label>
            <select
              id="inviteRole"
              value={inviteRole}
              onChange={(e) => setInviteRole(e.target.value as CompanyRole)}
            >
              {companyRoleOrder.map((role) => (
                <option key={role} value={role}>
                  {companyRoleLabels[role]}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="toolbar">
          <button
            type="button"
            disabled={createInvitation.isPending}
            onClick={() => {
              if (createInvitation.isPending) return
              if (!inviteEmail.trim()) {
                setInviteError('E-posta zorunludur.')
                return
              }
              createInvitation.mutate()
            }}
          >
            {createInvitation.isPending ? 'Oluşturuluyor…' : 'Davet oluştur'}
          </button>
        </div>
      </div>

      {invitations.length > 0 ? (
        <div className="card">
          <h2 style={{ marginTop: 0 }}>Davetler</h2>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>E-posta</th>
                  <th>Rol</th>
                  <th>Geçerlilik</th>
                  <th>Durum</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {invitations.map((invitation) => (
                  <tr key={invitation.id}>
                    <td>{invitation.email}</td>
                    <td>{companyRoleLabels[invitation.companyRole]}</td>
                    <td>{formatDate(invitation.expiresAt)}</td>
                    <td>
                      {invitation.acceptedAt
                        ? 'Kabul edildi'
                        : invitation.revokedAt
                          ? 'İptal edildi'
                          : invitation.isRedeemable
                            ? 'Bekliyor'
                            : 'Süresi doldu'}
                    </td>
                    <td>
                      {invitation.isRedeemable ? (
                        <button
                          type="button"
                          disabled={revokeInvitation.isPending}
                          onClick={() => revokeInvitation.mutate(invitation.id)}
                        >
                          İptal et
                        </button>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}
    </>
  )
}
