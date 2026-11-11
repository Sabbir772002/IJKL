import type { AdminHold, ClassInput, ClassSummary, Hold } from './types'

let serverOffset: number | undefined

export function serverNow() {
  return Date.now() + (serverOffset ?? 0)
}

export class ApiError extends Error {
  readonly code: string
  readonly existing?: ClassSummary

  constructor(code: string, existing?: ClassSummary) {
    super(code)
    this.code = code
    this.existing = existing
  }
}

export function apiUrl() {
  const { __CONFIG__ } = window as Window & { __CONFIG__?: { API_URL?: string } }
  return __CONFIG__?.API_URL?.replace(/\/+$/, '')
}

async function request(path: string, options?: RequestInit) {
  const url = apiUrl()
  if (!url) throw new ApiError('NETWORK')

  const response = await fetch(url + path, options)
  const date = response.headers.get('Date')
  if (serverOffset === undefined && date) {
    const serverTime = Date.parse(date)
    if (Number.isFinite(serverTime)) serverOffset = serverTime - Date.now()
  }
  if (!response.ok) {
    const result = await response.json().catch(() => null)
    throw new ApiError(result?.error?.code ?? 'INTERNAL', result?.error?.existing)
  }
  return response
}

export async function listClasses(signal?: AbortSignal): Promise<ClassSummary[]> {
  const response = await request('/classes', { signal })
  return response.json()
}
export async function createClass(input: ClassInput): Promise<ClassInput & { id: string }> {
  const response = await request('/classes', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  return response.json()
}

export async function listHolds(student: string, signal: AbortSignal): Promise<Hold[]> {
  const response = await request('/me/holds', { signal, headers: { 'X-Student-Id': student } })
  return response.json()
}

export async function editClass(id: string, name: string, totalSeats: number, blockedSeats?: number) {
  await request(`/classes/${encodeURIComponent(id)}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, totalSeats, blockedSeats }),
  })
}

export async function listAdminHolds(id: string, signal: AbortSignal): Promise<AdminHold[]> {
  const response = await request(`/admin/classes/${encodeURIComponent(id)}/holds`, { signal })
  return response.json()
}

export async function cancelAdminHold(id: string, reason: string, blockSeat: boolean) {
  await request(`/admin/holds/${encodeURIComponent(id)}/cancel`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason, blockSeat }),
  })
}

export async function deleteClass(id: string) {
  await request(`/classes/${encodeURIComponent(id)}`, { method: 'DELETE' })
}

export async function applyForClass(classId: string, student: string, waitlist = false) {
  const action = waitlist ? 'waitlist' : 'holds'
  await request(`/classes/${encodeURIComponent(classId)}/${action}`, {
    method: 'POST', headers: { 'X-Student-Id': student },
  })
}

export async function changeHold(holdId: string, student: string, confirm: boolean) {
  const path = `/holds/${encodeURIComponent(holdId)}`
  await request(confirm ? path + '/confirm' : path, {
    method: confirm ? 'POST' : 'DELETE', headers: { 'X-Student-Id': student },
  })
}
