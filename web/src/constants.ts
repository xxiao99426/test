import type { Priority, Status } from './types'

export const STATUS_OPTIONS: Status[] = ['open', 'in_progress', 'resolved']

export const STATUS_LABEL: Record<Status, string> = {
  open: '待处理',
  in_progress: '处理中',
  resolved: '已解决',
}

export const PRIORITY_LABEL: Record<Priority, string> = {
  low: '低',
  medium: '中',
  high: '高',
}

export const PRIORITY_ORDER: Record<Priority, number> = {
  high: 0,
  medium: 1,
  low: 2,
}
