import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { getApiKey } from './client';

type EventMap = {
  scanStarted: () => void;
  scanProgress: (payload: unknown) => void;
  scanFinished: (payload: { gamesAdded: number }) => void;
  downloadSnapshot: (payload: unknown) => void;
  libraryUpdated: () => void;
};

export type ConnState = 'connected' | 'reconnecting' | 'disconnected';
type ConnListener = (state: ConnState) => void;

const HUB_URL = '/hubs/progress';

class ProgressHubClient {
  private connection: HubConnection | null = null;
  private readonly listeners: { [K in keyof EventMap]: Set<EventMap[K]> } = {
    scanStarted: new Set(),
    scanProgress: new Set(),
    scanFinished: new Set(),
    downloadSnapshot: new Set(),
    libraryUpdated: new Set(),
  };
  private connState: ConnState = 'disconnected';
  private readonly connListeners = new Set<ConnListener>();

  getConnectionState(): ConnState {
    return this.connState;
  }

  onConnectionChange(cb: ConnListener): () => void {
    this.connListeners.add(cb);
    cb(this.connState);
    return () => { this.connListeners.delete(cb); };
  }

  private setConnState(next: ConnState) {
    if (next === this.connState) return;
    this.connState = next;
    this.connListeners.forEach((fn) => fn(next));
  }

  private ensureConnection(): HubConnection {
    if (this.connection) return this.connection;

    this.connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, {
        accessTokenFactory: () => getApiKey() ?? '',
        // Send the API key as header too - loopback requests bypass auth; LAN needs this.
        headers: getApiKey() ? { 'X-Api-Key': getApiKey()! } : undefined,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) => {
          // Exponential backoff capped at 30s.
          const base = Math.min(30_000, 500 * Math.pow(2, ctx.previousRetryCount));
          const jitter = Math.floor(Math.random() * 250);
          return base + jitter;
        },
      })
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('scanStarted', () => this.emit('scanStarted'));
    this.connection.on('scanProgress', (p) => this.emit('scanProgress', p));
    this.connection.on('scanFinished', (p) => this.emit('scanFinished', p));
    this.connection.on('downloadSnapshot', (p) => this.emit('downloadSnapshot', p));
    this.connection.on('libraryUpdated', () => this.emit('libraryUpdated'));

    this.connection.onreconnecting(() => this.setConnState('reconnecting'));
    this.connection.onreconnected(()  => this.setConnState('connected'));
    this.connection.onclose(()        => this.setConnState('disconnected'));

    return this.connection;
  }

  async start(): Promise<void> {
    const conn = this.ensureConnection();
    if (conn.state === HubConnectionState.Connected || conn.state === HubConnectionState.Connecting) return;
    try {
      await conn.start();
      this.setConnState('connected');
    } catch {
      // SignalR owns the reconnect loop (exponential backoff configured in
      // withAutomaticReconnect above). No manual retry needed.
      this.setConnState('disconnected');
    }
  }

  on<K extends keyof EventMap>(event: K, handler: EventMap[K]): () => void {
    this.listeners[event].add(handler as never);
    this.start();
    return () => {
      this.listeners[event].delete(handler as never);
    };
  }

  private emit<K extends keyof EventMap>(event: K, payload?: unknown) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    this.listeners[event].forEach((fn) => (fn as any)(payload));
  }
}

export const progressHub = new ProgressHubClient();
