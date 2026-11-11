import { useEffect, useState, type FormEvent } from 'react'
import { ApiError, cancelAdminHold, deleteClass, listAdminHolds } from '../api/client'
import type { AdminHold, ClassSummary } from '../api/types'
import { messages, errorMessages } from '../i18n'

type Props = {
  item: ClassSummary
  onDeleted: () => void
  onChanged: () => void
}

export default function AdminApplications({ item, onDeleted, onChanged }: Props) {
  const [holds, setHolds] = useState<AdminHold[]>([])
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)
  const [selected, setSelected] = useState<AdminHold>()

  useEffect(() => {
    const controller = new AbortController()

    async function load() {
      try {
        const rows = await listAdminHolds(item.id, controller.signal)
        if (!controller.signal.aborted) setHolds(rows)
      } catch (error) {
        if (!controller.signal.aborted) setError(message(error))
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }

    load()
    return () => controller.abort()
  }, [item.id, reload])

  async function removeClass() {
    if (busy || !window.confirm(messages.confirmDeleteClass)) return
    setBusy(true)
    setError('')
    try {
      await deleteClass(item.id)
      onDeleted()
    } catch (error) {
      setError(message(error))
    } finally {
      setBusy(false)
    }
  }

  async function cancel(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!selected || busy) return
    const data = new FormData(event.currentTarget)
    const reason = String(data.get('reason')).trim()
    if (!reason) {
      setError(messages.cancelReason)
      return
    }

    setBusy(true)
    setError('')
    try {
      await cancelAdminHold(selected.holdId, reason, data.has('block'))
      setSelected(undefined)
      setLoading(true)
      setReload(value => value + 1)
      onChanged()
    } catch (error) {
      setError(message(error))
    } finally {
      setBusy(false)
    }
  }

  const hasStudents = item.heldSeats > 0 || item.confirmedSeats > 0 || holds.some(active)

  return (
    <section className="panel">
      <h2>{messages.classApplications}</h2>
      <p>{item.name}</p>
      {error && <p role="alert">{error}</p>}
      {loading && <p role="status">{messages.loadingHolds}</p>}
      {!loading && holds.length === 0 && !error && <p>{messages.noHolds}</p>}
      <ul className="classes">
        {holds.map(hold => (
          <li key={hold.holdId}>
            <strong>{hold.studentId}</strong>
            <p>{messages.status[hold.status]}</p>
            {hold.cancelReason && <p>{messages.cancelReason}: {hold.cancelReason}</p>}
            {active(hold) && <button type="button" disabled={busy || loading} onClick={() => setSelected(hold)}>{messages.cancel}</button>}
          </li>
        ))}
      </ul>
      {selected && <form onSubmit={cancel}>
        <p>{selected.studentId}</p>
        <label>{messages.cancelReason}<input name="reason" required maxLength={500} /></label>
        {selected.status !== 'PENDING' && <label><span><input type="checkbox" name="block" /> {messages.blockSeat}</span></label>}
        <button disabled={busy}>{messages.cancel}</button>
      </form>}
      <p>
        <button type="button" disabled={busy || loading || hasStudents} onClick={removeClass}>{messages.deleteClass}</button>
      </p>
      {hasStudents && <p>{errorMessages.CLASS_HAS_STUDENTS}</p>}
    </section>
  )
}

function active(hold: AdminHold) {
  return hold.status === 'HELD' || hold.status === 'CONFIRMED' || hold.status === 'PENDING'
}

function message(error: unknown) {
  if (error instanceof ApiError) return errorMessages[error.code] ?? messages.serverError
  return messages.networkError
}
