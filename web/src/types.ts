// 与 Go 侧 server/model.go、Unity 侧 ProblemMarker.cs 完全一致的字段定义。
// 改这里之前先看另外两端，三端字段名不允许出现偏差。
export type Priority = 'low' | 'medium' | 'high'
export type Status = 'open' | 'in_progress' | 'resolved'

export interface Position {
  x: number
  y: number
  z: number
}

export interface Problem {
  id: string
  title: string
  description: string
  priority: Priority
  status: Status
  position: Position
  createdAt: string
  updatedAt: string
}

export interface ProblemListResponse {
  items: Problem[]
  total: number
}

export interface HealthResponse {
  status: string
  time: string
  problemCount: number
}
