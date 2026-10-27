import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ApiError, BASE_URL, fetchProblems, updateProblemStatus } from './api'
import { PRIORITY_LABEL, PRIORITY_ORDER, STATUS_LABEL, STATUS_OPTIONS } from './constants'
import type { Problem, Status } from './types'

const POLL_INTERVAL_MS = 6000

function describeError(e: unknown): string {
  if (e instanceof ApiError) return e.message
  if (e instanceof Error) return e.message
  return '未知错误'
}

function formatTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleString('zh-CN', { hour12: false })
}

function formatPosition(p: Problem['position']): string {
  return `(${p.x.toFixed(2)}, ${p.y.toFixed(2)}, ${p.z.toFixed(2)})`
}

export default function App() {
  const [problems, setProblems] = useState<Problem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const [pendingId, setPendingId] = useState<string | null>(null)
  const [statusFilter, setStatusFilter] = useState<'all' | Status>('all')
  const [lastSyncedAt, setLastSyncedAt] = useState<string | null>(null)
  const [autoRefresh, setAutoRefresh] = useState(true)

  // 用 ref 记录「是否有正在进行的写操作」，避免轮询把用户正在改的那行覆盖掉。
  const busyRef = useRef(false)

  const load = useCallback(async (silent = false) => {
    if (!silent) setLoading(true)
    try {
      const data = await fetchProblems()
      setProblems(data.items)
      setError(null)
      setLastSyncedAt(new Date().toLocaleTimeString('zh-CN', { hour12: false }))
    } catch (e) {
      setError(describeError(e))
    } finally {
      if (!silent) setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  // 自动刷新：Unity 那边提交后，管理端不用手动点也能看到。
  // 页面不可见时跳过，避免后台标签页空转。
  useEffect(() => {
    if (!autoRefresh) return
    const timer = window.setInterval(() => {
      if (busyRef.current) return
      if (document.visibilityState !== 'visible') return
      void load(true)
    }, POLL_INTERVAL_MS)
    return () => window.clearInterval(timer)
  }, [autoRefresh, load])

  useEffect(() => {
    if (!toast) return
    const timer = window.setTimeout(() => setToast(null), 3200)
    return () => window.clearTimeout(timer)
  }, [toast])

  const handleStatusChange = async (target: Problem, next: Status) => {
    const previous = target.status
    if (next === previous) return

    setPendingId(target.id)
    busyRef.current = true

    // 乐观更新：下拉立刻显示新值，网络慢时也不会觉得卡。失败再回滚。
    setProblems((cur) => cur.map((p) => (p.id === target.id ? { ...p, status: next } : p)))

    try {
      const updated = await updateProblemStatus(target.id, next)
      setProblems((cur) => cur.map((p) => (p.id === updated.id ? updated : p)))
      setError(null)
      setToast(`已把「${updated.title}」更新为 ${STATUS_LABEL[updated.status]}`)
    } catch (e) {
      setProblems((cur) => cur.map((p) => (p.id === target.id ? { ...p, status: previous } : p)))
      setError(`状态更新失败：${describeError(e)}`)
    } finally {
      setPendingId(null)
      busyRef.current = false
    }
  }

  const counts = useMemo(() => {
    const base: Record<Status, number> = { open: 0, in_progress: 0, resolved: 0 }
    for (const p of problems) base[p.status] += 1
    return base
  }, [problems])

  const visible = useMemo(() => {
    const list = statusFilter === 'all' ? problems : problems.filter((p) => p.status === statusFilter)
    // 新的排前面；同一时间用优先级兜底，让高危项更靠上。
    return [...list].sort((a, b) => {
      const t = new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
      if (t !== 0) return t
      return PRIORITY_ORDER[a.priority] - PRIORITY_ORDER[b.priority]
    })
  }, [problems, statusFilter])

  return (
    <div className="app">
      <header className="topbar">
        <div>
          <h1>巡检问题管理端</h1>
          <p className="subtitle">
            数据源 <code>{BASE_URL}</code>
            {lastSyncedAt && <span className="synced">· 最后同步 {lastSyncedAt}</span>}
          </p>
        </div>
        <div className="actions">
          <label className="toggle">
            <input
              type="checkbox"
              checked={autoRefresh}
              onChange={(e) => setAutoRefresh(e.target.checked)}
            />
            自动刷新
          </label>
          <button className="btn" onClick={() => void load()} disabled={loading}>
            {loading ? '刷新中…' : '刷新'}
          </button>
        </div>
      </header>

      {/* 后端不可用时显示这条，页面其余部分照常渲染，不会白屏（试题 3.2 第 4 条） */}
      {error && (
        <div className="banner banner-error" role="alert">
          <div>
            <strong>无法获取数据</strong>
            <p>{error}</p>
          </div>
          <button className="btn btn-sm" onClick={() => void load()}>
            重试
          </button>
        </div>
      )}

      {toast && (
        <div className="banner banner-ok" role="status">
          {toast}
        </div>
      )}

      <section className="stats">
        <div className="stat">
          <span className="stat-value">{problems.length}</span>
          <span className="stat-label">全部问题</span>
        </div>
        {STATUS_OPTIONS.map((s) => (
          <div className={`stat stat-${s}`} key={s}>
            <span className="stat-value">{counts[s]}</span>
            <span className="stat-label">{STATUS_LABEL[s]}</span>
          </div>
        ))}
      </section>

      <div className="toolbar">
        <label className="filter">
          按状态筛选
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value as 'all' | Status)}
          >
            <option value="all">全部</option>
            {STATUS_OPTIONS.map((s) => (
              <option key={s} value={s}>
                {STATUS_LABEL[s]}
              </option>
            ))}
          </select>
        </label>
        <span className="hint">
          共 {visible.length} 条{statusFilter !== 'all' && `（已筛选 ${STATUS_LABEL[statusFilter]}）`}
        </span>
      </div>

      <main>
        {loading && problems.length === 0 ? (
          <p className="placeholder">正在加载问题列表…</p>
        ) : visible.length === 0 ? (
          <p className="placeholder">
            {problems.length === 0
              ? '暂无问题。在 Unity 场景中点击设备放置标记并提交后，这里会出现记录。'
              : '当前筛选条件下没有问题。'}
          </p>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th style={{ width: '30%' }}>标题</th>
                <th>优先级</th>
                <th style={{ width: '150px' }}>状态</th>
                <th>位置 (x, y, z)</th>
                <th>创建时间</th>
              </tr>
            </thead>
            <tbody>
              {visible.map((p) => (
                <tr key={p.id}>
                  <td>
                    <div className="title">{p.title}</div>
                    {p.description && <div className="desc">{p.description}</div>}
                    <div className="id">ID {p.id.slice(0, 8)}</div>
                  </td>
                  <td>
                    <span className={`badge badge-priority-${p.priority}`}>
                      {PRIORITY_LABEL[p.priority]}
                    </span>
                  </td>
                  <td>
                    <select
                      className={`status-select status-${p.status}`}
                      value={p.status}
                      disabled={pendingId === p.id}
                      onChange={(e) => void handleStatusChange(p, e.target.value as Status)}
                    >
                      {STATUS_OPTIONS.map((s) => (
                        <option key={s} value={s}>
                          {STATUS_LABEL[s]}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td className="mono">{formatPosition(p.position)}</td>
                  <td className="time">{formatTime(p.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </main>

      <footer className="footer">
        状态修改即时写入 Go 后端，刷新页面或重启后端后仍然保留。
      </footer>
    </div>
  )
}
