import { bn } from './bn'
import { en } from './en'

export const language = localStorage.getItem('admission-language') === 'en' ? 'en' : 'bn'
export const messages = language === 'en' ? en : bn
export const number = new Intl.NumberFormat(language === 'en' ? 'en' : 'bn-BD')
export const timerSeconds = new Intl.NumberFormat(language === 'en' ? 'en' : 'bn-BD', {
  minimumIntegerDigits: 2,
})

export function changeLanguage(value: string) {
  localStorage.setItem('admission-language', value)
  window.location.reload()
}

export const errorMessages: Record<string, string> = {
  CLASS_EXISTS: messages.duplicate,
  VALIDATION_ERROR: messages.invalid,
  INTERNAL: messages.serverError,
  INVALID_STUDENT_ID: messages.invalidStudent,
  SEAT_FULL: messages.noSeats,
  SEATS_AVAILABLE: language === 'en' ? 'Seats are available. Apply directly.' : 'আসন খালি আছে। সরাসরি আবেদন করুন।',
  WAITLIST_UNAVAILABLE: language === 'en' ? 'The waitlist is currently unavailable.' : 'অপেক্ষমাণ তালিকায় এখন যোগ দেওয়া যাচ্ছে না।',
  ALREADY_APPLIED: language === 'en' ? 'You already have an active application for this academic year.' : 'এই শিক্ষাবর্ষে আপনার একটি সক্রিয় আবেদন আছে।',
  HOLD_EXPIRED: language === 'en' ? 'Your seat reservation has expired.' : 'আসন সংরক্ষণের সময় শেষ হয়েছে।',
  INVALID_STATE: language === 'en' ? 'This action is unavailable for the current application status.' : 'আবেদনটির বর্তমান অবস্থায় এই কাজটি করা যাবে না।',
  FORBIDDEN: language === 'en' ? 'This application belongs to another student.' : 'এই আবেদনটি অন্য শিক্ষার্থীর।',
  NOT_FOUND: language === 'en' ? 'The class or application could not be found.' : 'শ্রেণি বা আবেদনটি পাওয়া যায়নি।',
  SEATS_BELOW_OCCUPIED: language === 'en' ? 'Total seats cannot be lower than the number of reserved and confirmed seats.' : 'সংরক্ষিত ও নিশ্চিত আসনের চেয়ে মোট আসন কমানো যাবে না।',
  CLASS_HAS_STUDENTS: language === 'en' ? 'A class with active applications cannot be removed.' : 'সক্রিয় আবেদন থাকলে শ্রেণি সরানো যাবে না।',
}
