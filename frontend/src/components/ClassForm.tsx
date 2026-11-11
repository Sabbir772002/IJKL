import { useState, type FormEvent } from 'react'
import { ApiError, createClass, editClass } from '../api/client'
import type { ClassSummary } from '../api/types'
import { bn, errorMessages } from '../i18n/bn'

type Props = {
  defaultSession: string
  onCreated: (session: string) => void
  onDuplicate: (item: ClassSummary) => void
  editing?: ClassSummary
  onCancelEdit: () => void
}

export default function ClassForm({ defaultSession, onCreated, onDuplicate, editing, onCancelEdit }: Props) {
  const [formSession, setFormSession] = useState(editing?.session ?? defaultSession)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState('')
  const [notice, setNotice] = useState('')
  const [existing, setExisting] = useState<ClassSummary>()

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (saving) return
    const form = event.currentTarget
    const data = new FormData(form)
    const name = String(data.get('name')).trim()
    const year = formSession.trim()
    const totalSeats = Number(data.get('seats'))
    setNotice('')
    setFormError('')
    setExisting(undefined)
    const validSeatCount = Number.isInteger(totalSeats) && totalSeats >= 1 && totalSeats <= 100000
    if (!name || !year || !validSeatCount) {
      setFormError(bn.invalid)
      return
    }
    setSaving(true)
    try {
      if (editing) {
        const blockedInput = data.get('blockedSeats')
        const blockedSeats = blockedInput === '' ? undefined : Number(blockedInput)
        await editClass(editing.id, name, totalSeats, blockedSeats)
        setNotice(bn.savedClass)
        onCreated(editing.session)
        return
      }
      const item = await createClass({ name, session: year, totalSeats })
      onCreated(item.session)
      form.reset()
      setNotice(bn.created)
    } catch (error) {
      if (error instanceof ApiError) {
        setFormError(errorMessages[error.code] ?? bn.networkError)
        if (error.existing) {
          setExisting(error.existing)
          onDuplicate(error.existing)
        }
      } else {
        setFormError(bn.networkError)
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="panel" aria-labelledby="create-title">
      <h2 id="create-title">{editing ? bn.editClass : bn.newClass}</h2>
      <form onSubmit={submit}>
        <label>{bn.name}<input name="name" required maxLength={120} placeholder={bn.example} defaultValue={editing?.name} /></label>
        <label>{bn.session}<input name="session" value={formSession} onChange={event => setFormSession(event.target.value)} readOnly={Boolean(editing)} required maxLength={32} /></label>
        <label>{bn.total}<input name="seats" type="number" required min={1} max={100000} step={1} defaultValue={editing?.totalSeats} /></label>
        {editing && <label>{bn.blocked}<input name="blockedSeats" type="number" min={0}
          step={1} placeholder={String(editing.blockedSeats)} /></label>}
        <button disabled={saving}>{saving ? bn.saving : editing ? bn.saveClass : bn.create}</button>
        {editing && <button type="button" disabled={saving} onClick={onCancelEdit}>{bn.newClass}</button>}
        {formError && <p role="alert">{formError}{existing && <> <strong>{existing.name}</strong></>}</p>}
        {notice && <p role="status">{notice}</p>}
      </form>
    </section>
  )
}
