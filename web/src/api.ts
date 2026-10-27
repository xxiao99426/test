import type { HealthResponse, Problem, ProblemListResponse, Status } from './types'

// 后端地址。默认指向本机 8080，可用环境变量覆盖（见 .env.example）。
// 注意这里写的是 127.0.0.1 而不是 localhost：某些系统上 localhost 会先解析到 ::1，
// 而 Go 默认监听 0.0.0.0（IPv4），会得到一个「连接被拒绝」的诡异现象。
const BASE_URL = (import.meta.env.VITE_API_BASE ?? 'http://127.0.0.1:8080').replace(/\/+$/, '')

export class ApiError extends Error {
  readonly code: string
  readonly field?: string
  readonly status?: number

  constructor(code: string, message: string, status?: number, field?: string) {
    super(message)
    this.name = 'ApiError'
    this.code = code
    this.status = status
    this.field = field
  }
}

interface ErrorBody {
  code?: string
  message?: string
  field?: string
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response
  try {
    res = await fetch(`${BASE_URL}${path}`, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        ...(init?.headers ?? {}),
      },
    })
  } catch {
    // fetch 只在网络层失败时抛异常，跨域被拦截也走这里。
    // 这两种原因的提示词合并成一句可操作的说明，比技术错误码更有用。
    throw new ApiError(
      'NETWORK_ERROR',
      `无法连接后端（${BASE_URL}）。请确认 Go 服务已启动，且当前页面来源在后端 CORS 白名单内。`,
    )
  }

  const text = await res.text()
  let body: unknown
  if (text) {
    try {
      body = JSON.parse(text)
    } catch {
      body = undefined
    }
  }

  if (!res.ok) {
    const err = body as ErrorBody | undefined
    throw new ApiError(
      err?.code ?? `HTTP_${res.status}`,
      err?.message ?? `请求失败（HTTP ${res.status}）`,
      res.status,
      err?.field,
    )
  }

  return body as T
}

export function fetchHealth(): Promise<HealthResponse> {
  return request<HealthResponse>('/api/health')
}

export function fetchProblems(): Promise<ProblemListResponse> {
  return request<ProblemListResponse>('/api/problems')
}

export function updateProblemStatus(id: string, status: Status): Promise<Problem> {
  return request<Problem>(`/api/problems/${encodeURIComponent(id)}/status`, {
    method: 'PATCH',
    body: JSON.stringify({ status }),
  })
}

export { BASE_URL }
