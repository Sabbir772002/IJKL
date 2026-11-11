import PaymentActions from './PaymentActions'
import type { Hold } from '../api/types'
import { messages, number } from '../i18n'

type Props = {
  holds: Hold[]
  now: number
  busy: boolean
  onChange: (id: string, confirm: boolean) => void
}

export default function MyHolds({ holds, now, busy, onChange }: Props) {
  return <section className="panel" aria-labelledby="holds-title">
    <h2 id="holds-title">{messages.myHolds}</h2>
    {holds.length === 0 && <p>{messages.noHolds}</p>}
    <ul className="classes">
      {holds.map((hold, index) => {
        const seconds = hold.expiresAt ? Math.max(0, Math.ceil((Date.parse(hold.expiresAt) - now) / 1000)) : 0
        const status = hold.status === 'HELD' && seconds === 0 ? 'EXPIRED' : hold.status
        return <li key={hold.holdId}>
          {index === 0 && <p><strong>{messages.latestApplication}</strong></p>}
          <h3>{hold.className}</h3>
          <p>{messages.status[status]}</p>
          {status === 'PENDING' && <>
            {hold.queuePosition != null && <p>{messages.queuePosition} {number.format(hold.queuePosition)}</p>}
            <button type="button" disabled={busy} onClick={() => onChange(hold.holdId, false)}>{messages.cancel}</button>
          </>}
          {status === 'HELD' && <PaymentActions seconds={seconds} busy={busy}
            onChange={confirm => onChange(hold.holdId, confirm)} />}
        </li>
      })}
    </ul>
  </section>
}
