import { useState } from 'react'

const studentKey = 'admission-student-id'

export function validStudent(value: string) {
  return /^[0-9]{17}$/.test(value)
}

export function useStudent() {
  const [student, setStudent] = useState(() => {
    const saved = localStorage.getItem(studentKey) ?? ''
    return validStudent(saved) ? saved : ''
  })

  function saveStudent(value: string) {
    localStorage.setItem(studentKey, value)
    setStudent(value)
  }

  return { student, saveStudent }
}
