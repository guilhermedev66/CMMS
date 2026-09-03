import * as signalR from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'

/** Matches WorkOrderChangedPayload in src/Cmms.Api/Realtime/WorkOrderDispatchHub.cs. */
export interface WorkOrderChangedEvent {
  workOrderId: string
  siteId: string
  status: string
  priority: string
  assetId: string | null
  action: string
  receivedAtUtc: string
}

/** Matches HighPriorityAlertPayload in src/Cmms.Api/Realtime/WorkOrderDispatchHub.cs. */
export interface HighPriorityAlertEvent {
  workOrderId: string
  siteId: string
  title: string
  priority: string
  assetId: string | null
  receivedAtUtc: string
}

const MAX_FEED_LENGTH = 25

/**
 * The M5 dispatch board's client side (ADR-17). Cookie-authed like every other request in this
 * app — no token to pass, the browser's session cookie carries the same identity the hub uses
 * server-side to derive which site groups this connection joins (never a client-supplied site id;
 * see WorkOrderDispatchHub.OnConnectedAsync). Connects only while `enabled` (i.e. the caller is
 * authenticated), disconnects on unmount/when `enabled` flips false.
 */
export function useWorkOrderDispatch(enabled: boolean) {
  const [connectionState, setConnectionState] = useState<'connecting' | 'connected' | 'disconnected'>('connecting')
  const [events, setEvents] = useState<WorkOrderChangedEvent[]>([])
  const [alerts, setAlerts] = useState<HighPriorityAlertEvent[]>([])
  const connectionRef = useRef<signalR.HubConnection | null>(null)

  useEffect(() => {
    if (!enabled) {
      setConnectionState('disconnected')
      return
    }

    let connection: signalR.HubConnection
    try {
      // An absolute URL (not a bare relative path) — SignalR's own relative-URL resolution needs
      // a `window.document.baseURI` that isn't reliably present in every host environment (e.g.
      // this app's own test environment); resolving against `window.location.origin` ourselves
      // sidesteps that entirely and is no less correct in a real browser.
      connection = new signalR.HubConnectionBuilder()
        .withUrl(`${window.location.origin}/api/hubs/work-orders`, { withCredentials: true })
        .withAutomaticReconnect()
        .build()
    } catch {
      // Construction itself can fail in an environment with no usable transport at all — fail
      // soft (no live updates) rather than crash the dashboard.
      setConnectionState('disconnected')
      return
    }
    connectionRef.current = connection

    connection.on('WorkOrderChanged', (payload: Omit<WorkOrderChangedEvent, 'receivedAtUtc'>) => {
      setEvents((prev) => [{ ...payload, receivedAtUtc: new Date().toISOString() }, ...prev].slice(0, MAX_FEED_LENGTH))
    })
    connection.on('HighPriorityAlert', (payload: Omit<HighPriorityAlertEvent, 'receivedAtUtc'>) => {
      setAlerts((prev) => [{ ...payload, receivedAtUtc: new Date().toISOString() }, ...prev].slice(0, MAX_FEED_LENGTH))
    })
    connection.onreconnecting(() => setConnectionState('connecting'))
    connection.onreconnected(() => setConnectionState('connected'))
    connection.onclose(() => setConnectionState('disconnected'))

    setConnectionState('connecting')
    connection
      .start()
      .then(() => setConnectionState('connected'))
      .catch(() => setConnectionState('disconnected'))

    return () => {
      connectionRef.current = null
      void connection.stop()
    }
  }, [enabled])

  function dismissAlert(workOrderId: string) {
    setAlerts((prev) => prev.filter((a) => a.workOrderId !== workOrderId))
  }

  return { connectionState, events, alerts, dismissAlert }
}
