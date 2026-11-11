import type { Hold } from '../api/types'
import { bn, number } from '../i18n/bn'

type Props = {
  holds: Hold[]
  now: number
  busy: boolean
  onChange: (id: string, confirm: boolean) => void
}

export default function MyHolds({ holds, now, busy, onChange }: Props) {
  return <section className="panel" aria-labelledby="holds-title">
    <h2 id="holds-title">{bn.myHolds}</h2>
    {holds.length === 0 && <p>{bn.noHolds}</p>}
    <ul className="classes">
      {holds.map((hold, index) => {
        const seconds = hold.expiresAt ? Math.max(0, Math.ceil((Date.parse(hold.expiresAt) - now) / 1000)) : 0
        const status = hold.status === 'HELD' && seconds === 0 ? 'EXPIRED' : hold.status
        return <li key={hold.holdId}>
          {index === 0 && <p><strong>{bn.latestApplication}</strong></p>}
          <h3>{hold.className}</h3>
          <p>{bn.status[status]}</p>
          {status === 'HELD' && <>
            <p>{bn.remaining} {number.format(Math.floor(seconds / 60))}:{number.format(seconds % 60).padStart(2, '০')}</p>
            <div className="hold-actions">
              <button type="button" disabled={busy} onClick={() => onChange(hold.holdId, true)}>{bn.pay}</button>
              <button type="button" disabled={busy} onClick={() => onChange(hold.holdId, false)}>{bn.cancel}</button>
            </div>
          </>}
        </li>
      })}
    </ul>
  </section>
}
