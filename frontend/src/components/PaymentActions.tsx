import { messages, number, timerSeconds } from '../i18n'

type Props = {
  seconds: number
  busy: boolean
  onChange: (confirm: boolean) => void
}

export default function PaymentActions({ seconds, busy, onChange }: Props) {
  return <>
    <p>{messages.remaining} {number.format(Math.floor(seconds / 60))}:{timerSeconds.format(seconds % 60)}</p>
    <div className="hold-actions">
      <button type="button" disabled={busy} onClick={() => onChange(true)}>{messages.pay}</button>
      <button type="button" disabled={busy} onClick={() => onChange(false)}>{messages.cancel}</button>
    </div>
  </>
}
