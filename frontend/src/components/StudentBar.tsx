import { useState, type FormEvent } from 'react'
import { messages } from '../i18n'
import { validStudent } from '../hooks/useStudent'

type Props = { student: string; busy: boolean; onSave: (student: string) => void }

export default function StudentBar({ student, busy, onSave }: Props) {
  const [draft, setDraft] = useState(student)
  const [invalid, setInvalid] = useState(false)

  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const value = draft.trim()
    if (!validStudent(value)) {
      setInvalid(true)
      return
    }
    onSave(value)
    setDraft(value)
    setInvalid(false)
  }

  return <form className="student-bar" onSubmit={save}>
    <label>{messages.studentId}
      <input inputMode="numeric" maxLength={17} value={draft} disabled={busy}
        aria-invalid={invalid}
        onChange={event => {
          setDraft(event.target.value)
          setInvalid(false)
        }} required />
    </label>
    <button disabled={busy}>{messages.saveStudent}</button>
    {student && <button type="button" disabled={busy} onClick={() => {
      setDraft('')
      setInvalid(false)
      onSave('')
    }}>{messages.clearStudent}</button>}
    {invalid && <p role="alert">{messages.invalidStudent}</p>}
  </form>
}
