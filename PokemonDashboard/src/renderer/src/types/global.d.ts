import type { AppConfig } from './api'

declare global {
  interface Window {
    api: {
      loadConfig: () => Promise<AppConfig>
    }
  }
}
