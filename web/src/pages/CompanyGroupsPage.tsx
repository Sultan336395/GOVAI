import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { CompanyRelationshipType, MyCompany } from '@/api/types'
import { companyPermissions, useCompanies } from '@/app/contexts'
import { EmptyState, ErrorBox, FieldError, Loading, SuccessBox } from '@/components/Common'
import { relationshipLabels, relationshipOrder } from '@/lib/companyLabels'

/**
 * Şirket grubu ve ana–bağlı şirket ilişkisi.
 *
 * Döngüsel bağ (A → B → A) sunucuda reddedilir; burada da aynı hata metni gösterilir.
 * Bir şirketi kendi ana şirketi yapmak listede hiç sunulmaz.
 */
export default function CompanyGroupsPage() {
  const queryClient = useQueryClient()
  const { companies, refresh, isLoading } = useCompanies()

  const [groupName, setGroupName] = useState('')
  const [groupDescription, setGroupDescription] = useState('')
  const [groupError, setGroupError] = useState<string | undefined>()
  const [notice, setNotice] = useState<string | null>(null)

  const {
    data: groups = [],
    isLoading: groupsLoading,
    error: groupsError,
  } = useQuery({ queryKey: ['company-groups'], queryFn: api.listCompanyGroups })

  const createGroup = useMutation({
    mutationFn: () => api.createCompanyGroup({ name: groupName.trim(), description: groupDescription.trim() || null }),
    onSuccess: async (group) => {
      setGroupName('')
      setGroupDescription('')
      setGroupError(undefined)
      setNotice(`"${group.name}" grubu oluşturuldu.`)
      await queryClient.invalidateQueries({ queryKey: ['company-groups'] })
    },
    onError: (error: unknown) => {
      setGroupError(error instanceof Error ? error.message : 'Grup oluşturulamadı.')
    },
  })

  if (isLoading || groupsLoading) return <Loading />
  if (groupsError) return <ErrorBox error={groupsError} />

  const canManageAny = companies.some((company) => companyPermissions.manageProfile(company.companyRole))

  function handleCreateGroup() {
    if (createGroup.isPending) return

    if (!groupName.trim()) {
      setGroupError('Grup adı zorunludur.')
      return
    }

    createGroup.mutate()
  }

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Şirket grubu ve bağlı şirketler</h1>
          <p>
            Aynı gruba bağlı şirketler birlikte raporlanır. Ana–bağlı şirket bağı, çağrıların
            grup düzeyinde değerlendirilmesi gereken durumlar için tutulur.
          </p>
        </div>
      </div>

      {notice ? <SuccessBox>{notice}</SuccessBox> : null}

      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ marginTop: 0 }}>Yeni grup</h2>

        <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))' }}>
          <div className="field">
            <label htmlFor="groupName">Grup adı</label>
            <input
              id="groupName"
              value={groupName}
              onChange={(e) => {
                setGroupName(e.target.value)
                setGroupError(undefined)
              }}
              placeholder="Örnek Holding"
            />
            <FieldError message={groupError} />
          </div>

          <div className="field">
            <label htmlFor="groupDescription">Açıklama</label>
            <input
              id="groupDescription"
              value={groupDescription}
              onChange={(e) => setGroupDescription(e.target.value)}
            />
          </div>
        </div>

        <div className="toolbar">
          <button type="button" onClick={handleCreateGroup} disabled={createGroup.isPending}>
            {createGroup.isPending ? 'Oluşturuluyor…' : 'Grup oluştur'}
          </button>
        </div>
      </div>

      {groups.length > 0 ? (
        <div className="card" style={{ marginBottom: 16 }}>
          <h2 style={{ marginTop: 0 }}>Gruplar</h2>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Grup</th>
                  <th>Açıklama</th>
                  <th>Şirket sayısı</th>
                </tr>
              </thead>
              <tbody>
                {groups.map((group) => (
                  <tr key={group.id}>
                    <td>{group.name}</td>
                    <td className="muted">{group.description ?? '—'}</td>
                    <td>{group.companyCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}

      <h2>Şirketlerin bağı</h2>

      {!canManageAny ? (
        <EmptyState>
          Bağ kurabilmek için en az bir şirkette sahip veya yönetici olmanız gerekir.
        </EmptyState>
      ) : (
        companies.map((company) => (
          <HierarchyRow
            key={company.id}
            company={company}
            groups={groups}
            candidates={companies.filter((candidate) => candidate.id !== company.id)}
            onSaved={async (message) => {
              setNotice(message)
              await refresh()
            }}
          />
        ))
      )}
    </>
  )
}

function HierarchyRow({
  company,
  groups,
  candidates,
  onSaved,
}: {
  company: MyCompany
  groups: { id: string; name: string }[]
  candidates: MyCompany[]
  onSaved: (message: string) => Promise<void>
}) {
  const [groupId, setGroupId] = useState<string>(company.groupId ?? '')
  const [parentCompanyId, setParentCompanyId] = useState<string>(company.parentCompanyId ?? '')
  const [relationshipType, setRelationshipType] = useState<CompanyRelationshipType>(
    company.relationshipType,
  )
  const [isHeadCompany, setIsHeadCompany] = useState(company.isHeadCompany)
  const [isSaving, setIsSaving] = useState(false)
  const [error, setError] = useState<unknown>(null)

  const canEdit = companyPermissions.manageProfile(company.companyRole)

  async function save() {
    // Çift tıklama koruması.
    if (isSaving) return

    setIsSaving(true)
    setError(null)
    try {
      await api.updateCompanyHierarchy(company.id, {
        groupId: groupId || null,
        parentCompanyId: parentCompanyId || null,
        relationshipType,
        isHeadCompany,
      })
      await onSaved(`${company.legalName} için grup bağı güncellendi.`)
    } catch (caught) {
      setError(caught)
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div className="card" style={{ marginBottom: 12 }}>
      <strong>{company.legalName}</strong>
      <div className="muted" style={{ fontSize: 12, marginBottom: 8 }}>
        VKN {company.taxNumber}
      </div>

      {error ? <ErrorBox error={error} /> : null}

      <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))' }}>
        <div className="field">
          <label htmlFor={`group-${company.id}`}>Grup</label>
          <select
            id={`group-${company.id}`}
            value={groupId}
            disabled={!canEdit}
            onChange={(e) => setGroupId(e.target.value)}
          >
            <option value="">Gruba bağlı değil</option>
            {groups.map((group) => (
              <option key={group.id} value={group.id}>
                {group.name}
              </option>
            ))}
          </select>
        </div>

        <div className="field">
          <label htmlFor={`parent-${company.id}`}>Ana şirket</label>
          <select
            id={`parent-${company.id}`}
            value={parentCompanyId}
            disabled={!canEdit}
            onChange={(e) => setParentCompanyId(e.target.value)}
          >
            <option value="">Ana şirketi yok</option>
            {candidates.map((candidate) => (
              <option key={candidate.id} value={candidate.id}>
                {candidate.legalName}
              </option>
            ))}
          </select>
        </div>

        <div className="field">
          <label htmlFor={`relationship-${company.id}`}>İlişki türü</label>
          <select
            id={`relationship-${company.id}`}
            value={relationshipType}
            disabled={!canEdit}
            onChange={(e) => setRelationshipType(e.target.value as CompanyRelationshipType)}
          >
            {relationshipOrder.map((type) => (
              <option key={type} value={type}>
                {relationshipLabels[type]}
              </option>
            ))}
          </select>
        </div>
      </div>

      <label style={{ display: 'flex', gap: 8, alignItems: 'center', marginTop: 8 }}>
        <input
          type="checkbox"
          checked={isHeadCompany}
          disabled={!canEdit}
          onChange={(e) => setIsHeadCompany(e.target.checked)}
        />
        Grubun ana şirketi
      </label>

      <div className="toolbar">
        <button type="button" onClick={() => void save()} disabled={!canEdit || isSaving}>
          {isSaving ? 'Kaydediliyor…' : 'Kaydet'}
        </button>
        {!canEdit ? (
          <span className="muted" style={{ fontSize: 12 }}>
            Bu şirkette değişiklik yapma yetkiniz yok.
          </span>
        ) : null}
      </div>
    </div>
  )
}
