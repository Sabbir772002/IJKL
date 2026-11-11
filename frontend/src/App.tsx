import { useEffect, useRef, useState } from 'react'
import { ApiError, applyForClass, changeHold, listClasses, listHolds, serverNow } from './api/client'
import { demoClasses } from './api/demoClasses'
import type { ClassSummary, Hold } from './api/types'
import ClassForm from './components/ClassForm'
import AdminApplications from './components/AdminApplications'
import ClassList from './components/ClassList'
import MyHolds from './components/MyHolds'
import StudentBar from './components/StudentBar'
import { useStudent } from './hooks/useStudent'
import { useClassStream } from './hooks/useClassStream'
import { bn, errorMessages } from './i18n/bn'

const defaultSession = '2027'

export default function App() {
  const [loadedClasses, setClasses] = useState<ClassSummary[]>([])
  const [session, setSession] = useState(defaultSession)
  const [loading, setLoading] = useState(true)
  const [listError, setListError] = useState('')
  const [usingDemo, setUsingDemo] = useState(false)
  const [highlightedId, setHighlightedId] = useState<string>()
  const [reload, setReload] = useState(0)
  const { student, saveStudent } = useStudent()
  const [holds, setHolds] = useState<Hold[]>([])
  const [holdsLoading, setHoldsLoading] = useState(Boolean(student))
  const [holdsError, setHoldsError] = useState('')
  const [holdsReload, setHoldsReload] = useState(0)
  const [actionError, setActionError] = useState('')
  const [busy, setBusy] = useState(false)
  const [now, setNow] = useState(serverNow)
  const syncedExpiry = useRef(new Set<string>())
  const isAdmin = window.location.pathname === '/admin'
  const isApplication = window.location.pathname === '/application'
  const isNewApplication = window.location.pathname === '/new-application'
  const isDashboard = !isAdmin && !isApplication && !isNewApplication
  let pageTitle = bn.dashboard
  if (isAdmin) pageTitle = bn.adminDashboard
  if (isApplication) pageTitle = bn.applicationStatus
  if (isNewApplication) pageTitle = bn.startApplication
  const showDashboard = new URLSearchParams(window.location.search).has('dashboard')
  const redirectToStatus = window.location.pathname === '/' && Boolean(student) && !showDashboard
  const { snapshot, disconnected } = useClassStream(!redirectToStatus)
  const classes = snapshot ?? loadedClasses
  const showingDemo = !snapshot && usingDemo
  const [editingClass, setEditingClass] = useState<ClassSummary>()
  const selectedClass = classes.find(item => item.id === editingClass?.id)

  useEffect(() => {
    if (redirectToStatus) window.location.replace('/application')
  }, [redirectToStatus])

  useEffect(() => {
    if (!student) return
    const controller = new AbortController()

    async function loadHolds() {
      try {
        const rows = await listHolds(student, controller.signal)
        if (!controller.signal.aborted) {
          setHolds(rows)
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
  }, [student, holdsReload])

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
          setUsingDemo(false)
        }
      } catch (error) {
        if (controller.signal.aborted) return
        if (error instanceof TypeError || (error instanceof ApiError && error.code === 'NETWORK')) {
          setClasses(demoClasses)
          setUsingDemo(true)
          return
        }
        setUsingDemo(false)
        let message = bn.networkError
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
    setActionError('')
    refreshHolds()
  }

  async function runAction(action: () => Promise<void>) {
    if (!student || busy || holdsLoading) return
    setBusy(true)
    setActionError('')
    try {
      await action()
    } catch (error) {
      setActionError(errorMessage(error))
    } finally {
      refreshHolds()
      refresh()
      setBusy(false)
    }
  }

  function handleApply(classId: string) {
    return runAction(async () => {
      await applyForClass(classId, student)
      window.location.assign('/application')
    })
  }

  function canApply(item: ClassSummary) {
    if (showingDemo || !student || busy || holdsLoading || holdsError || item.availableSeats <= 0) return false
    return !holds.some(hold => {
      if (hold.status !== 'HELD' && hold.status !== 'CONFIRMED' && hold.status !== 'PENDING') return false
      if (hold.status === 'HELD' && hold.expiresAt && Date.parse(hold.expiresAt) <= now) return false
      return classes.find(row => row.id === hold.classId)?.session === item.session
    })
  }

  if (redirectToStatus) return null

  return (
    <main>
      <header className="site-header">
        <a className="site-name" href="/">{bn.title}</a>
        <nav className="site-menu" aria-label={bn.navigation}>
          <a href={student ? '/?dashboard=1' : '/'} aria-current={isDashboard ? 'page' : undefined}>{bn.dashboard}</a>
          <a href="/application" aria-current={isApplication ? 'page' : undefined}>{bn.myHolds}</a>
          <a href="/admin" aria-current={isAdmin ? 'page' : undefined}>{bn.adminMenu}</a>
        </nav>
      </header>
      <div className="page-heading">
        <h1>{pageTitle}</h1>
        {!isAdmin && !isNewApplication && (
          <a className="start-action" href="/new-application">{bn.startApplication}</a>
        )}
      </div>
      {(isApplication || isNewApplication) && <section id="student" className="panel student-section">
        <StudentBar student={student} busy={busy} onSave={handleStudentChange} />
        {!student && <p>{bn.studentPrompt}</p>}
        {!isApplication && student && <a href="#classes-title">{bn.chooseClass}</a>}
        {isApplication && student && <>
          {holdsLoading && <p role="status">{bn.loadingHolds}</p>}
          {holdsError && <div role="alert">
            <p>{holdsError}</p>
            <button type="button" onClick={refreshHolds}>{bn.retry}</button>
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
      {!isApplication && disconnected && !showingDemo && <p role="status">{bn.streamDisconnected}</p>}
      {!isApplication && showingDemo && <div className="panel student-section" role="status">
        <p>{bn.demoNotice}</p>
        <button type="button" disabled={loading} onClick={refresh}>{bn.retry}</button>
      </div>}
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
          canApply={isNewApplication ? canApply : undefined}
          onApply={handleApply}
          onEdit={isAdmin && !showingDemo ? setEditingClass : undefined}
        />
        {isAdmin && !showingDemo && <div className="sidebar">
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
      <footer className="site-footer">{bn.title}</footer>
    </main>
  )
}

function errorMessage(error: unknown) {
  if (error instanceof ApiError) return errorMessages[error.code] ?? bn.serverError
  return bn.networkError
}
