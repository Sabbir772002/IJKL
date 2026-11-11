export type ClassSummary = {
  id: string
  name: string
  session: string
  totalSeats: number
  heldSeats: number
  confirmedSeats: number
  blockedSeats: number
  availableSeats: number
}

export type ClassInput = { name: string; session: string; totalSeats: number }

export type Hold = {
  holdId: string
  classId: string
  className: string
  status: 'HELD' | 'CONFIRMED' | 'CANCELLED' | 'EXPIRED' | 'PENDING'
  expiresAt: string | null
}
