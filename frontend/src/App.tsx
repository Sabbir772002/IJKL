import { useEffect, useRef, useState } from 'react'
import { ApiError, applyForClass, changeHold, listClasses, listHolds, serverNow } from './api/client'
import type { ClassSummary, Hold } from './api/types'
import ClassForm from './components/ClassForm'
import AdminApplications from './components/AdminApplications'
import ClassList from './components/ClassList'
import MyHolds from './components/MyHolds'
import PaymentActions from './components/PaymentActions'
import StudentBar from './components/StudentBar'
import { useStudent } from './hooks/useStudent'
import { useClassStream } from './hooks/useClassStream'
import { changeLanguage, errorMessages, language, messages } from './i18n'

const defaultSession = '2027'

export default function App() {
  const [loadedClasses, setClasses] = useState<ClassSummary[]>([])
  const applicationClass = window.location.pathname === '/new-application'
    ? new URLSearchParams(window.location.search)
    : undefined
  const [session, setSession] = useState(applicationClass?.get('session') ?? defaultSession)
  const [loading, setLoading] = useState(true)
  const [listError, setListError] = useState('')
  const [highlightedId, setHighlightedId] = useState(applicationClass?.get('class') ?? undefined)
  const [reload, setReload] = useState(0)
  const { student, saveStudent } = useStudent()
  const [holds, setHolds] = useState<Hold[]>([])
  const [holdsLoading, setHoldsLoading] = useState(Boolean(student))
  const [holdsError, setHoldsError] = useState('')
  const [holdsReload, setHoldsReload] = useState(0)
  const [actionError, setActionError] = useState('')
  const [promotionNotice, setPromotionNotice] = useState(false)
  const [busy, setBusy] = useState(false)
  const [now, setNow] = useState(serverNow)
  const syncedExpiry = useRef(new Set<string>())
  const pendingHolds = useRef(new Set<string>())
  const isAdmin = window.location.pathname === '/admin'
  const isHome = window.location.pathname === '/'
  const isApplication = window.location.pathname === '/application' || (isHome && Boolean(student))
  const isNewApplication = window.location.pathname === '/new-application'
  const isDashboard = !isAdmin && !isApplication && !isNewApplication
  const awaitingPayment = holds.filter(hold => hold.status === 'HELD' && hold.expiresAt &&
    Date.parse(hold.expiresAt) > now)
  let pageTitle = messages.dashboard
  if (isAdmin) pageTitle = messages.adminDashboard
  if (isApplication) pageTitle = messages.applicationStatus
  if (isNewApplication) pageTitle = messages.startApplication
  const { snapshot, disconnected } = useClassStream(true)
  const classes = snapshot ?? loadedClasses
  const waitingSnapshot = (isApplication || isDashboard) && holds.some(hold => hold.status === 'PENDING')
    ? snapshot
    : undefined
  const [editingClass, setEditingClass] = useState<ClassSummary>()
  const selectedClass = classes.find(item => item.id === editingClass?.id)

  useEffect(() => {
    if (!student) return
    const controller = new AbortController()

    async function loadHolds() {
      try {
        const rows = await listHolds(student, controller.signal)
        if (!controller.signal.aborted) {
          if (rows.some(hold => hold.status === 'HELD' && pendingHolds.current.has(hold.holdId))) {
            setPromotionNotice(true)
          }
          pendingHolds.current = new Set(rows.filter(hold => hold.status === 'PENDING').map(hold => hold.holdId))
          setHolds(rows)
          setHoldsError('')
          setNow(serverNow())
        }
      } catch (error) {
        if (!controller.signal.aborted) setHoldsError(errorMessage(error))
      } finally {
        if (!controller.signal.aborted) setHoldsLoading(false)
      }
    }

    loadHolds()
    return () => controller.abort()
  }, [student, holdsReload, waitingSnapshot])

  useEffect(() => {
    if (!holds.some(hold => hold.status === 'HELD')) return
    const timer = window.setInterval(() => setNow(serverNow()), 1000)
    return () => window.clearInterval(timer)
  }, [holds])

  useEffect(() => {
    const expired = holds.filter(hold => hold.status === 'HELD' && hold.expiresAt &&
      Date.parse(hold.expiresAt) <= now && !syncedExpiry.current.has(hold.holdId))
    if (expired.length === 0) return

    for (const hold of expired) syncedExpiry.current.add(hold.holdId)
    setHoldsReload(value => value + 1)
    setReload(value => value + 1)
  }, [holds, now])

  useEffect(() => {
    const controller = new AbortController()

    async function loadClasses() {
      try {
        const rows = await listClasses(controller.signal)
        if (!controller.signal.aborted) {
          setClasses(rows)
          setListError('')
        }
      } catch (error) {
        if (controller.signal.aborted) return
        let message = messages.networkError
        if (error instanceof ApiError) {
          message = errorMessages[error.code] ?? message
        }
        setListError(message)
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }

    loadClasses()
    return () => controller.abort()
  }, [reload])

  function refresh() {
    setLoading(true)
    setListError('')
    setReload(value => value + 1)
  }

  function handleCreated(year: string) {
    setSession(year)
    setHighlightedId(undefined)
    refresh()
  }

  function handleDuplicate(item: ClassSummary) {
    setSession(item.session)
    setHighlightedId(item.id)
    setEditingClass(item)
    refresh()
  }

  function refreshHolds() {
    setHoldsLoading(true)
    setHoldsError('')
    setHoldsReload(value => value + 1)
  }

  function handleStudentChange(value: string) {
    if (value === student) return
    saveStudent(value)
    setHolds([])
    pendingHolds.current.clear()
    setPromotionNotice(false)
    setActionError('')
    setHoldsError('')
    setHoldsLoading(Boolean(value))
  }

  async function runAction(action: () => Promise<void>) {
    if (!student || busy || holdsLoading) return
    setBusy(true)
    setActionError('')
    try {
      await action()
      setPromotionNotice(false)
    } catch (error) {
      setActionError(errorMessage(error))
    } finally {
      refreshHolds()
      refresh()
      setBusy(false)
    }
  }

  function handleApply(classId: string, waitlist: boolean) {
    if (isDashboard && !student) {
      const selected = classes.find(item => item.id === classId)
      if (selected) {
        const query = new URLSearchParams({ class: selected.id, session: selected.session })
        window.location.assign(`/new-application?${query}`)
      }
      return
    }
    return runAction(async () => {
      await applyForClass(classId, student, waitlist)
      window.location.assign('/application')
    })
  }

  function canApply(item: ClassSummary) {
    if (item.availableSeats <= 0 && item.heldSeats === 0) return false
    if (isDashboard && !student) return true
    if (!student || busy || holdsLoading || holdsError) return false
    return !holds.some(hold => {
      if (hold.status !== 'HELD' && hold.status !== 'CONFIRMED' && hold.status !== 'PENDING') return false
      if (hold.status === 'HELD' && hold.expiresAt && Date.parse(hold.expiresAt) <= now) return false
      return classes.find(row => row.id === hold.classId)?.session === item.session
    })
  }

  return (
    <main>
      <header className="site-header">
        <a className="site-name" href="/">{messages.title}</a>
        <nav className="site-menu" aria-label={messages.navigation}>
          <a href="/" aria-current={isHome ? 'page' : undefined}>{messages.dashboard}</a>
          <a href="/application" aria-current={isApplication && !isHome ? 'page' : undefined}>{messages.myHolds}</a>
          <a href="/admin" aria-current={isAdmin ? 'page' : undefined}>{messages.adminMenu}</a>
        </nav>
        <select aria-label={language === 'en' ? 'Language' : 'ভাষা'}
          value={language} onChange={event => changeLanguage(event.target.value)}>
          <option value="bn" lang="bn">বাংলা</option>
          <option value="en" lang="en">English</option>
        </select>
      </header>
      <div className="page-heading">
        <h1>{pageTitle}</h1>
        {!isAdmin && !isNewApplication && (
          <a className="start-action" href="/new-application">{messages.startApplication}</a>
        )}
      </div>
      {(isHome || isApplication || isNewApplication) && <section id="student" className="panel student-section">
        <StudentBar student={student} busy={busy} onSave={handleStudentChange} />
        {!student && <p>{messages.studentPrompt}</p>}
        {!isApplication && student && <a href="#classes-title">{messages.chooseClass}</a>}
        {isApplication && student && <>
          {promotionNotice && <p role="status">{messages.promoted}</p>}
          {holdsLoading && <p role="status">{messages.loadingHolds}</p>}
          {holdsError && <div role="alert">
            <p>{holdsError}</p>
            <button type="button" onClick={refreshHolds}>{messages.retry}</button>
          </div>}
          {!holdsLoading && !holdsError && <MyHolds
            holds={holds}
            now={now}
            busy={busy}
            onChange={(id, confirm) => runAction(() => changeHold(id, student, confirm))}
          />}
        </>}
      </section>}
      {actionError && <p role="alert">{actionError}</p>}
      {!isApplication && disconnected && <p role="status">{messages.streamDisconnected}</p>}
      {!isApplication && <div className={isAdmin ? 'layout' : undefined}>
        <ClassList
          classes={classes}
          session={session}
          defaultSession={defaultSession}
          highlightedId={highlightedId}
          loading={!snapshot && loading}
          error={snapshot ? '' : listError}
          onSessionChange={setSession}
          onRetry={refresh}
          showShortlist={isDashboard || isNewApplication}
          canApply={isDashboard || isNewApplication ? canApply : undefined}
          onApply={handleApply}
          paymentActions={isDashboard || isNewApplication ? classId => {
            const hold = awaitingPayment.find(item => item.classId === classId)
            if (!hold?.expiresAt) return null
            return <PaymentActions
              seconds={Math.ceil((Date.parse(hold.expiresAt) - now) / 1000)}
              busy={busy || holdsLoading || Boolean(holdsError)}
              onChange={confirm => runAction(() => changeHold(hold.holdId, student, confirm))}
            />
          } : undefined}
          onEdit={isAdmin ? setEditingClass : undefined}
        />
        {isAdmin && <div className="sidebar">
          <ClassForm key={selectedClass?.id ?? 'new'} editing={selectedClass}
            defaultSession={defaultSession} onCreated={handleCreated} onDuplicate={handleDuplicate}
            onCancelEdit={() => setEditingClass(undefined)} />
          {selectedClass && <AdminApplications key={selectedClass.id} item={selectedClass}
            onChanged={refresh} onDeleted={() => {
              setEditingClass(undefined)
              refresh()
            }} />}
        </div>}
      </div>}
      <footer className="site-footer">{messages.title}</footer>
    </main>
  )
}

function errorMessage(error: unknown) {
  if (error instanceof ApiError) return errorMessages[error.code] ?? messages.serverError
  return messages.networkError
}
