import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../src/App'
import { ApiError, applyForClass, changeHold, listClasses, listHolds } from '../src/api/client'
import type { ClassSummary, Hold } from '../src/api/types'
import { errorMessages, messages, number, timerSeconds } from '../src/i18n'

vi.mock('../src/api/client', async importOriginal => ({
  ...await importOriginal<typeof import('../src/api/client')>(),
  applyForClass: vi.fn(),
  changeHold: vi.fn(),
  listClasses: vi.fn(),
  listHolds: vi.fn(),
}))

vi.mock('../src/hooks/useClassStream', () => ({
  useClassStream: () => ({ snapshot: undefined, disconnected: false }),
}))

const student = '20152691234567890'
const classSummary: ClassSummary = {
  id: 'class-7',
  name: 'Class 7',
  session: '2027',
  totalSeats: 1,
  availableSeats: 1,
  heldSeats: 0,
  confirmedSeats: 0,
  blockedSeats: 0,
}

function reserveSeat(expiresAt: string): Hold {
  vi.mocked(listClasses).mockResolvedValue([{ ...classSummary, availableSeats: 0, heldSeats: 1 }])
  const hold: Hold = {
    holdId: 'hold-7',
    classId: classSummary.id,
    className: classSummary.name,
    status: 'HELD',
    expiresAt,
  }
  vi.mocked(listHolds).mockResolvedValue([hold])
  return hold
}

beforeEach(() => {
  vi.resetAllMocks()
  window.history.replaceState(null, '', '/')
  localStorage.clear()
  vi.mocked(listClasses).mockResolvedValue([classSummary])
  vi.mocked(listHolds).mockResolvedValue([])
  vi.mocked(changeHold).mockResolvedValue(undefined)
})

afterEach(() => {
  cleanup()
  vi.useRealTimers()
})

describe('admission UI', () => {
  it('validates the student number and switches home between the class list and application history', async () => {
    render(<App />)
    await screen.findByRole('heading', { name: classSummary.name })
    const input = screen.getByRole('textbox', { name: messages.studentId })
    const save = screen.getByRole('button', { name: messages.saveStudent })

    fireEvent.change(input, { target: { value: '123' } })
    fireEvent.click(save)
    expect(screen.getByRole('alert').textContent).toContain(messages.invalidStudent)
    expect(listHolds).not.toHaveBeenCalled()

    fireEvent.change(input, { target: { value: student } })
    fireEvent.click(save)
    await screen.findByText(messages.noHolds)
    expect(listHolds).toHaveBeenCalledWith(student, expect.any(AbortSignal))
    expect(screen.queryByRole('heading', { name: messages.classes })).toBeNull()

    cleanup()
    render(<App />)
    await screen.findByText(messages.noHolds)
    expect(screen.getByRole<HTMLInputElement>('textbox', { name: messages.studentId }).value).toBe(student)
    fireEvent.click(screen.getByRole('button', { name: messages.clearStudent }))
    await screen.findByRole('heading', { name: classSummary.name })
    expect(screen.queryByRole('heading', { name: messages.myHolds })).toBeNull()
  })

  it('shows payment and cancellation on the reserved class card', async () => {
    window.history.replaceState(null, '', '/new-application')
    localStorage.setItem('admission-student-id', student)
    const hold = reserveSeat(new Date(Date.now() + 120_000).toISOString())
    render(<App />)

    const heading = await screen.findByRole('heading', { name: classSummary.name })
    const card = within(heading.closest('li')!)
    const pay = await card.findByRole<HTMLButtonElement>('button', { name: messages.pay })
    const cancel = card.getByRole<HTMLButtonElement>('button', { name: messages.cancel })
    expect(pay.disabled).toBe(false)
    expect(cancel.disabled).toBe(false)
    expect(card.queryByRole('button', { name: messages.joinWaitlist })).toBeNull()
    expect(card.queryByRole('button', { name: messages.apply })).toBeNull()

    await act(async () => fireEvent.click(cancel))
    expect(changeHold).toHaveBeenCalledWith(hold.holdId, student, false)
  })

  it('counts down and removes payment once the hold expires without reloading', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-11-11T10:00:00Z'))
    localStorage.setItem('admission-student-id', student)
    reserveSeat(new Date(Date.now() + 2000).toISOString())
    await act(async () => { render(<App />) })

    const countdown = (seconds: number) => `${messages.remaining} ${number.format(0)}:${timerSeconds.format(seconds)}`
    expect(screen.getByText(countdown(2))).toBeDefined()
    await act(async () => { await vi.advanceTimersByTimeAsync(1000) })
    expect(screen.getByText(countdown(1))).toBeDefined()
    await act(async () => { await vi.advanceTimersByTimeAsync(1000) })
    expect(screen.getByText(messages.status.EXPIRED)).toBeDefined()
    expect(screen.queryByRole('button', { name: messages.pay })).toBeNull()
  })

  it('translates a seat conflict into a visible error', async () => {
    window.history.replaceState(null, '', '/new-application')
    localStorage.setItem('admission-student-id', student)
    vi.mocked(applyForClass).mockRejectedValue(new ApiError('SEAT_FULL'))
    render(<App />)

    const apply = await screen.findByRole<HTMLButtonElement>('button', { name: messages.apply })
    expect(apply.disabled).toBe(false)
    fireEvent.click(apply)
    expect((await screen.findByRole('alert')).textContent).toContain(errorMessages.SEAT_FULL)
  })
})
