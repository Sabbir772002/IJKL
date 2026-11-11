import { useEffect, useState } from 'react'
import { apiUrl } from '../api/client'
import type { ClassSummary } from '../api/types'

export function useClassStream(enabled: boolean) {
  const [snapshot, setSnapshot] = useState<ClassSummary[]>()
  const [disconnected, setDisconnected] = useState(false)

  useEffect(() => {
    const url = apiUrl()
    if (!enabled || !url) return

    let stream: EventSource
    let lastMessage = Date.now()

    function connect() {
      lastMessage = Date.now()
      stream = new EventSource(url + '/classes/stream')
      stream.addEventListener('classes', event => {
        lastMessage = Date.now()
        try {
          setSnapshot(JSON.parse(event.data))
          setDisconnected(false)
        } catch {
          setDisconnected(true)
        }
      })
      stream.addEventListener('ping', () => {
        lastMessage = Date.now()
      })
      stream.onerror = () => setDisconnected(true)
    }

    connect()
    const timer = window.setInterval(() => {
      if (Date.now() - lastMessage < 30_000) return
      setDisconnected(true)
      stream.close()
      connect()
    }, 1000)

    return () => {
      window.clearInterval(timer)
      stream.close()
    }
  }, [enabled])

  return { snapshot, disconnected }
}
