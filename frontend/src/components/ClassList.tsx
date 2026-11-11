import { useState } from 'react'
import type { ClassSummary } from '../api/types'
import { bn, number } from '../i18n/bn'

type Props = {
  classes: ClassSummary[]
  session: string
  defaultSession: string
  highlightedId?: string
  loading: boolean
  error: string
  onSessionChange: (session: string) => void
  onRetry: () => void
  canApply?: (item: ClassSummary) => boolean
  showShortlist: boolean
  onApply: (id: string) => void
  onEdit?: (item: ClassSummary) => void
}

export default function ClassList({
  classes, session, defaultSession, highlightedId, loading, error, onSessionChange, onRetry,
  showShortlist, canApply, onApply, onEdit,
}: Props) {
  const [page, setPage] = useState(1)
  const pageSize = 10
  const sessionNames = new Set([defaultSession])
  if (session) sessionNames.add(session)
  for (const item of classes) {
    sessionNames.add(item.session)
  }
  const sessions = Array.from(sessionNames).sort((a, b) => b.localeCompare(a))
  const visible = session ? classes.filter(item => item.session === session) : classes
  const showClasses = !loading && !error
  const pageCount = Math.max(1, Math.ceil(visible.length / pageSize))
  const currentPage = Math.min(page, pageCount)
  const pageClasses = visible.slice((currentPage - 1) * pageSize, currentPage * pageSize)
  const fewestSeats = visible.filter(item => item.availableSeats > 0)
    .sort((a, b) => a.availableSeats - b.availableSeats || a.name.localeCompare(b.name))
    .slice(0, 5)

  return (
    <section aria-labelledby="classes-title">
      <div className="section-head">
        <h2 id="classes-title">{bn.classes}</h2>
        <label>{bn.session}
            <select value={session} onChange={event => {
              setPage(1)
              onSessionChange(event.target.value)
            }}>
            <option value="">{bn.allSessions}</option>
            {sessions.map(year => <option key={year} value={year}>{/^\d+$/.test(year) ? number.format(Number(year)) : year}</option>)}
          </select>
        </label>
      </div>
      {loading && <p role="status">{bn.loading}</p>}
      {!loading && error && (
        <div role="alert">
          <p>{error}</p>
          <button type="button" onClick={onRetry}>{bn.retry}</button>
        </div>
      )}
      {showClasses && visible.length === 0 && <p className="empty">{bn.empty}</p>}
      {showClasses && showShortlist && fewestSeats.length > 0 && (
        <section className="panel shortlist">
          <h3>{bn.fewestSeats}</h3>
          <ol>
            {fewestSeats.map(item => (
              <li key={item.id}>
                <strong>{item.name}</strong>
                <span>{number.format(item.availableSeats)}{bn.available}</span>
              </li>
            ))}
          </ol>
        </section>
      )}
      {showClasses && visible.length > 0 && <>
        <ul className="classes">
          {pageClasses.map(item => (
            <li key={item.id} className={item.id === highlightedId ? 'highlight' : undefined}>
              <div className="class-head">
                <h3>{item.name}</h3>
                <span className="available">{number.format(item.availableSeats)}{bn.available}</span>
              </div>
              <p className="class-session">{bn.session} {item.session}</p>
              <progress className="seat-progress" value={item.confirmedSeats} max={item.totalSeats}
                aria-label={bn.confirmed} />
              <dl>
                <div><dt>{bn.total}</dt><dd>{number.format(item.totalSeats)}</dd></div>
                <div><dt>{bn.held}</dt><dd>{number.format(item.heldSeats)}</dd></div>
                <div><dt>{bn.confirmed}</dt><dd>{number.format(item.confirmedSeats)}</dd></div>
                {item.blockedSeats > 0 && <div><dt>{bn.blocked}</dt><dd>{number.format(item.blockedSeats)}</dd></div>}
              </dl>
              {canApply && <button type="button" disabled={!canApply(item)} onClick={() => onApply(item.id)}>
                {item.availableSeats > 0 ? bn.apply : bn.noSeats}
              </button>}
              {onEdit && <button type="button" onClick={() => onEdit(item)}>{bn.editClass}</button>}
            </li>
          ))}
        </ul>
        {pageCount > 1 && <nav className="pagination" aria-label={bn.classes}>
          <button type="button" disabled={currentPage === 1} onClick={() => setPage(currentPage - 1)}>{bn.previous}</button>
          <span aria-live="polite">{bn.page} {number.format(currentPage)} / {number.format(pageCount)}</span>
          <button type="button" disabled={currentPage === pageCount} onClick={() => setPage(currentPage + 1)}>{bn.next}</button>
        </nav>}
      </>}
    </section>
  )
}
