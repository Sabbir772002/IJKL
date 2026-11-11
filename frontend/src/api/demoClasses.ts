import type { ClassSummary } from './types'

const classNames = ['প্রথম', 'দ্বিতীয়', 'তৃতীয়', 'চতুর্থ', 'পঞ্চম', 'ষষ্ঠ', 'সপ্তম', 'অষ্টম']
const sections = ['ক', 'খ', 'গ']

export const demoClasses: ClassSummary[] = []

for (const name of classNames) {
  for (const section of sections) {
    const index = demoClasses.length
    const totalSeats = 30
    const confirmedSeats = 9 + index % 20
    const heldSeats = index % 3
    demoClasses.push({
      id: `demo-${index}`,
      name: `${name} শ্রেণি — শাখা ${section}`,
      session: '2027',
      totalSeats,
      confirmedSeats,
      heldSeats,
      blockedSeats: 0,
      availableSeats: totalSeats - confirmedSeats - heldSeats,
    })
  }
}
