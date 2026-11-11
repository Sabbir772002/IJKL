import { useState, type FormEvent } from 'react'
import { bn } from '../i18n/bn'
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
    <label>{bn.studentId}
      <input inputMode="numeric" maxLength={17} value={draft} disabled={busy}
        aria-invalid={invalid}
        onChange={event => {
          setDraft(event.target.value)
          setInvalid(false)
        }} required />
    </label>
    <button disabled={busy}>{bn.saveStudent}</button>
    {invalid && <p role="alert">{bn.invalidStudent}</p>}
  </form>
}
