package main

import (
	"encoding/json"
	"errors"
	"log"
	"math"
	"net/http"
	"net/url"
	"strings"
	"time"
)

// 参数上限。定这些常量不是为了刁难使用者，而是为了让「什么算非法输入」
// 有明确的书面标准，三端都能对照。
const (
	maxTitleLen       = 200
	maxDescriptionLen = 2000
	maxBodyBytes      = 1 << 20 // 1 MB
	maxCoordAbs       = 10000.0
)

// Server 持有 HTTP 层依赖。业务状态全部在 Store 里，Server 无状态。
type Server struct {
	store *Store
}

func NewServer(s *Store) *Server {
	return &Server{store: s}
}

// routes 注册全部路由。Go 1.22+ 的 ServeMux 支持方法与路径参数，
// 因此不需要引入第三方路由库。
func (s *Server) routes() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /api/health", s.handleHealth)
	mux.HandleFunc("GET /api/problems", s.handleList)
	mux.HandleFunc("POST /api/problems", s.handleCreate)
	mux.HandleFunc("PATCH /api/problems/{id}/status", s.handleUpdateStatus)

	// 中间件顺序：日志包在最外层，这样 CORS 预检也会被记录。
	return withLogging(withCORS(mux))
}

// ---------------------------------------------------------------- handlers

// handleHealth 提供服务运行状态检查（试题 3.3 第 1 条）。
func (s *Server) handleHealth(w http.ResponseWriter, r *http.Request) {
	replyJSON(w, http.StatusOK, map[string]any{
		"status":       "ok",
		"time":         time.Now().UTC(),
		"problemCount": s.store.Count(),
	})
}

// handleList 返回已保存的问题列表（试题 3.3 第 3 条）。
func (s *Server) handleList(w http.ResponseWriter, r *http.Request) {
	items := s.store.List()
	replyJSON(w, http.StatusOK, ProblemList{Items: items, Total: len(items)})
}

type createRequest struct {
	Title       string    `json:"title"`
	Description string    `json:"description"`
	Priority    string    `json:"priority"`
	Position    *Position `json:"position"`
}

// handleCreate 接收并保存 Unity 上报的问题（试题 3.3 第 2、5、6 条）。
func (s *Server) handleCreate(w http.ResponseWriter, r *http.Request) {
	var req createRequest
	if err := decodeJSON(w, r, &req); err != nil {
		replyError(w, http.StatusBadRequest, "INVALID_JSON", err.Error(), "")
		return
	}

	if e := validateCreate(&req); e != nil {
		replyError(w, http.StatusBadRequest, e.Code, e.Message, e.Field)
		return
	}

	id, err := newUUIDv4()
	if err != nil {
		log.Printf("[ERROR] 生成 ID 失败: %v", err)
		replyError(w, http.StatusInternalServerError, "INTERNAL", "生成问题 ID 失败", "")
		return
	}

	now := time.Now().UTC()
	p := Problem{
		ID:          id,
		Title:       strings.TrimSpace(req.Title),
		Description: strings.TrimSpace(req.Description),
		Priority:    req.Priority,
		Status:      StatusOpen, // 新问题默认 open（试题 3.3 第 5 条）
		Position:    *req.Position,
		CreatedAt:   now,
		UpdatedAt:   now,
	}

	if err := s.store.Add(p); err != nil {
		log.Printf("[ERROR] 保存问题失败: %v", err)
		replyError(w, http.StatusInternalServerError, "INTERNAL", "保存失败，请稍后重试", "")
		return
	}

	log.Printf("[CREATE] id=%s priority=%s pos=(%.2f, %.2f, %.2f) title=%q",
		p.ID, p.Priority, p.Position.X, p.Position.Y, p.Position.Z, p.Title)
	replyJSON(w, http.StatusCreated, p)
}

type updateStatusRequest struct {
	Status string `json:"status"`
}

// handleUpdateStatus 根据 ID 修改处理状态（试题 3.3 第 4、6 条）。
func (s *Server) handleUpdateStatus(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")

	var req updateStatusRequest
	if err := decodeJSON(w, r, &req); err != nil {
		replyError(w, http.StatusBadRequest, "INVALID_JSON", err.Error(), "")
		return
	}

	if !isValidStatus(req.Status) {
		replyError(w, http.StatusBadRequest, "INVALID_ARGUMENT",
			"status 必须是 open、in_progress、resolved 之一，收到: "+quoteOrEmpty(req.Status),
			"status")
		return
	}

	p, err := s.store.UpdateStatus(id, req.Status)
	if err != nil {
		if errors.Is(err, ErrNotFound) {
			replyError(w, http.StatusNotFound, "NOT_FOUND", "问题不存在: "+id, "id")
			return
		}
		log.Printf("[ERROR] 更新状态失败: %v", err)
		replyError(w, http.StatusInternalServerError, "INTERNAL", "保存失败，请稍后重试", "")
		return
	}

	log.Printf("[UPDATE] id=%s -> %s", p.ID, p.Status)
	replyJSON(w, http.StatusOK, p)
}

// ---------------------------------------------------------------- 校验

// validateCreate 逐字段校验，返回第一个错误。
// 一次只报一个错误是有意的：客户端修完一个再提交，出错信息永远指向当前要改的地方。
func validateCreate(req *createRequest) *ErrorBody {
	title := strings.TrimSpace(req.Title)
	if title == "" {
		return &ErrorBody{Code: "INVALID_ARGUMENT", Message: "title 不能为空", Field: "title"}
	}
	if len([]rune(title)) > maxTitleLen {
		return &ErrorBody{Code: "INVALID_ARGUMENT", Message: "title 超过 200 字符上限", Field: "title"}
	}
	if len([]rune(req.Description)) > maxDescriptionLen {
		return &ErrorBody{Code: "INVALID_ARGUMENT", Message: "description 超过 2000 字符上限", Field: "description"}
	}

	// priority 不传时给 medium，传了就必须合法。
	if req.Priority == "" {
		req.Priority = PriorityMedium
	}
	if !isValidPriority(req.Priority) {
		return &ErrorBody{Code: "INVALID_ARGUMENT",
			Message: "priority 必须是 low、medium、high 之一，收到: " + quoteOrEmpty(req.Priority), Field: "priority"}
	}

	if req.Position == nil {
		return &ErrorBody{Code: "INVALID_ARGUMENT", Message: "position 不能为空", Field: "position"}
	}
	for _, c := range []struct {
		name string
		v    float64
	}{
		{"x", req.Position.X}, {"y", req.Position.Y}, {"z", req.Position.Z},
	} {
		if math.IsNaN(c.v) || math.IsInf(c.v, 0) {
			return &ErrorBody{Code: "INVALID_ARGUMENT",
				Message: "position." + c.name + " 必须是有限数值", Field: "position." + c.name}
		}
		if math.Abs(c.v) > maxCoordAbs {
			return &ErrorBody{Code: "INVALID_ARGUMENT",
				Message: "position." + c.name + " 超出合理范围（±10000）", Field: "position." + c.name}
		}
	}
	return nil
}

// ---------------------------------------------------------------- 工具

// decodeJSON 统一处理请求体：限制体积、拒绝未知字段、拒绝尾随垃圾。
// DisallowUnknownFields 能让「字段名写错」这类问题在开发期立刻暴露，而不是被静默忽略。
func decodeJSON(w http.ResponseWriter, r *http.Request, dst any) error {
	r.Body = http.MaxBytesReader(w, r.Body, maxBodyBytes)

	dec := json.NewDecoder(r.Body)
	dec.DisallowUnknownFields()

	if err := dec.Decode(dst); err != nil {
		return errors.New("请求体不是合法 JSON，或包含未定义字段: " + err.Error())
	}
	if dec.More() {
		return errors.New("请求体只能包含一个 JSON 对象")
	}
	return nil
}

func replyJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	if err := json.NewEncoder(w).Encode(v); err != nil {
		log.Printf("[WARN] 写响应失败: %v", err)
	}
}

func replyError(w http.ResponseWriter, status int, code, message, field string) {
	replyJSON(w, status, ErrorBody{Code: code, Message: message, Field: field})
}

func quoteOrEmpty(v string) string {
	if v == "" {
		return "(空)"
	}
	return `"` + v + `"`
}

// ---------------------------------------------------------------- 中间件

// withCORS 让浏览器里的 React 能跨域访问（试题 3.2 第 9 条）。
//
// 只放行本机来源：Vite 默认 5173，但端口可能被占用后自动切换，
// 所以按「host 是不是 localhost / 127.0.0.1」判断，而不是写死端口。
// 这既是开发期最省事的选择，也避免把服务暴露成全开放。
func withCORS(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		origin := r.Header.Get("Origin")
		if isLocalOrigin(origin) {
			w.Header().Set("Access-Control-Allow-Origin", origin)
			w.Header().Set("Vary", "Origin")
			w.Header().Set("Access-Control-Allow-Methods", "GET, POST, PATCH, OPTIONS")
			w.Header().Set("Access-Control-Allow-Headers", "Content-Type")
			w.Header().Set("Access-Control-Max-Age", "600")
		}

		if r.Method == http.MethodOptions {
			w.WriteHeader(http.StatusNoContent)
			return
		}
		next.ServeHTTP(w, r)
	})
}

func isLocalOrigin(origin string) bool {
	if origin == "" {
		return false // 非浏览器客户端（Unity、curl）没有 Origin，本来也不需要 CORS 头
	}
	u, err := url.Parse(origin)
	if err != nil {
		return false
	}
	host := u.Hostname()
	return host == "localhost" || host == "127.0.0.1" || host == "::1"
}

// statusRecorder 记录实际写出的状态码，供访问日志使用。
type statusRecorder struct {
	http.ResponseWriter
	status int
}

func (s *statusRecorder) WriteHeader(code int) {
	s.status = code
	s.ResponseWriter.WriteHeader(code)
}

func withLogging(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		start := time.Now()
		rec := &statusRecorder{ResponseWriter: w, status: http.StatusOK}
		next.ServeHTTP(rec, r)
		log.Printf("[HTTP] %-6s %-34s %d %s", r.Method, r.URL.Path, rec.status,
			time.Since(start).Round(time.Millisecond))
	})
}
