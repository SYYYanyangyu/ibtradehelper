/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE?: string
  readonly VITE_IBTRADE_API_KEY?: string
  /** `1` 强制嘉信 Mock；`0` 强制走真实 API */
  readonly VITE_USE_MOCK?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
