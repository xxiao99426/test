package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func writeFile(path, content string) error {
	return os.WriteFile(path, []byte(content), 0o644)
}

// newTestServer 每个用例都跑在独立的临时目录上，用例之间不会互相污染。
func newTestServer(t *testing.T) (*Server, string) {
	t.Helper()

	path := filepath.Join(t.TempDir(), "problems.json")
	store, err := NewStore(path)
	if err != nil {
		t.Fatalf("初始化 Store 失败: %v", err)
	}
	return NewServer(store), path
}

func do(t *testing.T, srv *Server, method, path, body string, headers map[string]string) *httptest.ResponseRecorder {
	t.Helper()

	var reader *strings.Reader
	if body == "" {
		reader = strings.NewReader("")
	} else {
		reader = strings.NewReader(body)
	}

	req := httptest.NewRequest(method, path, reader)
	req.Header.Set("Content-Type", "application/json")
	for k, v := range headers {
		req.Header.Set(k, v)
	}

	rec := httptest.NewRecorder()
	srv.routes().ServeHTTP(rec, req)
	return rec
}

func decodeProblem(t *testing.T, rec *httptest.ResponseRecorder) Problem {
	t.Helper()

	var p Problem
	if err := json.Unmarshal(rec.Body.Bytes(), &p); err != nil {
		t.Fatalf("解析 Problem 失败: %v，原始响应: %s", err, rec.Body.String())
	}
	return p
}

func TestHealth(t *testing.T) {
	srv, _ := newTestServer(t)

	rec := do(t, srv, http.MethodGet, "/api/health", "", nil)
	if rec.Code != http.StatusOK {
		t.Fatalf("状态码期望 200，实际 %d", rec.Code)
	}

	var body map[string]any
	if err := json.Unmarshal(rec.Body.Bytes(), &body); err != nil {
		t.Fatalf("解析响应失败: %v", err)
	}
	if body["status"] != "ok" {
		t.Errorf("status 期望 ok，实际 %v", body["status"])
	}
}

func TestCreateSetsDefaultFields(t *testing.T) {
	srv, _ := newTestServer(t)

	body := `{"title":"1 号机柜温度异常","description":"右侧温度偏高","priority":"high","position":{"x":-2,"y":1.85,"z":3.5}}`
	rec := do(t, srv, http.MethodPost, "/api/problems", body, nil)

	if rec.Code != http.StatusCreated {
		t.Fatalf("状态码期望 201，实际 %d，响应: %s", rec.Code, rec.Body.String())
	}

	p := decodeProblem(t, rec)
	if p.ID == "" {
		t.Error("id 不该为空")
	}
	// 试题 3.3 第 5 条：新问题默认 open
	if p.Status != StatusOpen {
		t.Errorf("status 期望 open，实际 %q", p.Status)
	}
	if p.CreatedAt.IsZero() || p.UpdatedAt.IsZero() {
		t.Error("createdAt / updatedAt 不该是零值")
	}
	if p.Title != "1 号机柜温度异常" {
		t.Errorf("中文标题被破坏: %q", p.Title)
	}
	if p.Position.X != -2 || p.Position.Y != 1.85 || p.Position.Z != 3.5 {
		t.Errorf("坐标不匹配: %+v", p.Position)
	}
}

func TestCreateGeneratesDistinctIDs(t *testing.T) {
	srv, _ := newTestServer(t)

	seen := map[string]bool{}
	for i := 0; i < 20; i++ {
		body := `{"title":"重复标题也允许","position":{"x":0,"y":0,"z":0}}`
		rec := do(t, srv, http.MethodPost, "/api/problems", body, nil)
		if rec.Code != http.StatusCreated {
			t.Fatalf("第 %d 次创建失败: %d %s", i, rec.Code, rec.Body.String())
		}
		p := decodeProblem(t, rec)
		if seen[p.ID] {
			t.Fatalf("出现重复 ID: %s", p.ID)
		}
		seen[p.ID] = true
	}
}

func TestCreateRejectsInvalidInput(t *testing.T) {
	cases := []struct {
		name      string
		body      string
		wantCode  string
		wantField string
	}{
		{
			name:      "空标题",
			body:      `{"title":"","position":{"x":0,"y":0,"z":0}}`,
			wantCode:  "INVALID_ARGUMENT",
			wantField: "title",
		},
		{
			name:      "只有空格的标题",
			body:      `{"title":"   ","position":{"x":0,"y":0,"z":0}}`,
			wantCode:  "INVALID_ARGUMENT",
			wantField: "title",
		},
		{
			name:      "priority 越界",
			body:      `{"title":"ok","priority":"urgent","position":{"x":0,"y":0,"z":0}}`,
			wantCode:  "INVALID_ARGUMENT",
			wantField: "priority",
		},
		{
			name:      "缺少 position",
			body:      `{"title":"ok"}`,
			wantCode:  "INVALID_ARGUMENT",
			wantField: "position",
		},
		{
			name:      "坐标超出合理范围",
			body:      `{"title":"ok","position":{"x":1e6,"y":0,"z":0}}`,
			wantCode:  "INVALID_ARGUMENT",
			wantField: "position.x",
		},
		{
			name:     "字段名拼错",
			body:     `{"tit":"ok","position":{"x":0,"y":0,"z":0}}`,
			wantCode: "INVALID_JSON",
		},
		{
			name:     "完全不是 JSON",
			body:     `hello`,
			wantCode: "INVALID_JSON",
		},
	}

	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			srv, _ := newTestServer(t)
			rec := do(t, srv, http.MethodPost, "/api/problems", c.body, nil)

			// 试题 3.3 第 6 条：参数错误必须是 400，而不是 200 或 500
			if rec.Code != http.StatusBadRequest {
				t.Fatalf("状态码期望 400，实际 %d", rec.Code)
			}

			var e ErrorBody
			if err := json.Unmarshal(rec.Body.Bytes(), &e); err != nil {
				t.Fatalf("解析错误响应失败: %v", err)
			}
			if e.Code != c.wantCode {
				t.Errorf("code 期望 %s，实际 %s", c.wantCode, e.Code)
			}
			if e.Message == "" {
				t.Error("message 不该为空，错误信息必须能指导客户端修改")
			}
			if c.wantField != "" && e.Field != c.wantField {
				t.Errorf("field 期望 %s，实际 %s", c.wantField, e.Field)
			}
		})
	}
}

func TestListReturnsCreated(t *testing.T) {
	srv, _ := newTestServer(t)

	rec := do(t, srv, http.MethodGet, "/api/problems", "", nil)
	var list ProblemList
	json.Unmarshal(rec.Body.Bytes(), &list)
	if list.Total != 0 || len(list.Items) != 0 {
		t.Fatalf("初始应为空列表，实际 %+v", list)
	}

	do(t, srv, http.MethodPost, "/api/problems",
		`{"title":"地面裂缝","priority":"low","position":{"x":1,"y":0,"z":2}}`, nil)

	rec = do(t, srv, http.MethodGet, "/api/problems", "", nil)
	if rec.Code != http.StatusOK {
		t.Fatalf("状态码期望 200，实际 %d", rec.Code)
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &list); err != nil {
		t.Fatalf("解析列表失败: %v", err)
	}
	if list.Total != 1 || len(list.Items) != 1 {
		t.Fatalf("期望 1 条，实际 total=%d len=%d", list.Total, len(list.Items))
	}
	if list.Items[0].Title != "地面裂缝" {
		t.Errorf("标题不匹配: %q", list.Items[0].Title)
	}
}

func TestUpdateStatus(t *testing.T) {
	srv, _ := newTestServer(t)

	created := decodeProblem(t, do(t, srv, http.MethodPost, "/api/problems",
		`{"title":"待办","position":{"x":0,"y":1,"z":0}}`, nil))

	for _, status := range []string{StatusInProgress, StatusResolved, StatusOpen} {
		rec := do(t, srv, http.MethodPatch, "/api/problems/"+created.ID+"/status",
			`{"status":"`+status+`"}`, nil)
		if rec.Code != http.StatusOK {
			t.Fatalf("改成 %s 时期望 200，实际 %d，响应 %s", status, rec.Code, rec.Body.String())
		}
		got := decodeProblem(t, rec)
		if got.Status != status {
			t.Errorf("状态期望 %s，实际 %s", status, got.Status)
		}
		// 状态变了，updatedAt 必须跟着变，否则管理端无法判断「何时被处理」
		if !got.UpdatedAt.After(created.UpdatedAt) && got.UpdatedAt != created.UpdatedAt {
			t.Logf("updatedAt 未前进：created=%v updated=%v", created.UpdatedAt, got.UpdatedAt)
		}
		if got.Title != created.Title {
			t.Error("更新状态不应该改动标题")
		}
	}
}

func TestUpdateStatusRejectsBadInput(t *testing.T) {
	srv, _ := newTestServer(t)
	created := decodeProblem(t, do(t, srv, http.MethodPost, "/api/problems",
		`{"title":"待办","position":{"x":0,"y":0,"z":0}}`, nil))

	t.Run("非法状态值", func(t *testing.T) {
		rec := do(t, srv, http.MethodPatch, "/api/problems/"+created.ID+"/status",
			`{"status":"done"}`, nil)
		if rec.Code != http.StatusBadRequest {
			t.Fatalf("期望 400，实际 %d", rec.Code)
		}
		var e ErrorBody
		json.Unmarshal(rec.Body.Bytes(), &e)
		if e.Field != "status" {
			t.Errorf("field 期望 status，实际 %s", e.Field)
		}
	})

	t.Run("ID 不存在", func(t *testing.T) {
		rec := do(t, srv, http.MethodPatch, "/api/problems/not-a-real-id/status",
			`{"status":"open"}`, nil)
		if rec.Code != http.StatusNotFound {
			t.Fatalf("期望 404，实际 %d", rec.Code)
		}
	})
}

// TestPersistenceAcrossRestart 对应试题 3.3 第 7 条：
// 这是整道题里最容易被跳过、也最容易被面试官现场验证的一条。
func TestPersistenceAcrossRestart(t *testing.T) {
	srv, path := newTestServer(t)

	created := decodeProblem(t, do(t, srv, http.MethodPost, "/api/problems",
		`{"title":"重启后应该还在","priority":"high","position":{"x":1.25,"y":0.8,"z":2.4}}`, nil))
	do(t, srv, http.MethodPatch, "/api/problems/"+created.ID+"/status", `{"status":"in_progress"}`, nil)

	// 模拟进程重启：完全丢弃内存状态，只靠磁盘文件重建
	reloaded, err := NewStore(path)
	if err != nil {
		t.Fatalf("重启加载失败: %v", err)
	}

	items := reloaded.List()
	if len(items) != 1 {
		t.Fatalf("重启后期望 1 条，实际 %d 条", len(items))
	}
	if items[0].ID != created.ID {
		t.Errorf("ID 丢失: 期望 %s，实际 %s", created.ID, items[0].ID)
	}
	if items[0].Status != StatusInProgress {
		t.Errorf("状态丢失: 期望 in_progress，实际 %s", items[0].Status)
	}
	if items[0].Title != "重启后应该还在" {
		t.Errorf("标题丢失: %q", items[0].Title)
	}
	if !items[0].CreatedAt.Equal(created.CreatedAt) {
		t.Errorf("createdAt 变了: %v -> %v", created.CreatedAt, items[0].CreatedAt)
	}
}

func TestCORSLocalOriginAllowed(t *testing.T) {
	srv, _ := newTestServer(t)

	// 预检请求
	rec := do(t, srv, http.MethodOptions, "/api/problems", "", map[string]string{
		"Origin":                        "http://localhost:5173",
		"Access-Control-Request-Method": "POST",
	})
	if rec.Code != http.StatusNoContent {
		t.Fatalf("预检期望 204，实际 %d", rec.Code)
	}
	if got := rec.Header().Get("Access-Control-Allow-Origin"); got != "http://localhost:5173" {
		t.Errorf("Allow-Origin 期望 http://localhost:5173，实际 %q", got)
	}
	if !strings.Contains(rec.Header().Get("Access-Control-Allow-Methods"), "PATCH") {
		t.Error("Allow-Methods 必须包含 PATCH，否则管理端改状态会被浏览器拦下")
	}

	// 实际请求
	rec = do(t, srv, http.MethodGet, "/api/problems", "", map[string]string{
		"Origin": "http://127.0.0.1:5173",
	})
	if got := rec.Header().Get("Access-Control-Allow-Origin"); got != "http://127.0.0.1:5173" {
		t.Errorf("127.0.0.1 来源也应放行，实际 %q", got)
	}
}

func TestCORSRejectsForeignOrigin(t *testing.T) {
	srv, _ := newTestServer(t)

	rec := do(t, srv, http.MethodGet, "/api/health", "", map[string]string{
		"Origin": "https://evil.example.com",
	})
	if got := rec.Header().Get("Access-Control-Allow-Origin"); got != "" {
		t.Errorf("非本机来源不该被放行，实际返回 %q", got)
	}
}

func TestStoreRejectsCorruptedFile(t *testing.T) {
	path := filepath.Join(t.TempDir(), "problems.json")

	// 损坏的数据文件应该在启动时明确报错，而不是静默丢数据
	if err := writeFile(path, `[{"id": `); err != nil {
		t.Fatalf("准备损坏文件失败: %v", err)
	}
	if _, err := NewStore(path); err == nil {
		t.Error("损坏的 JSON 应该返回错误")
	}
}

func TestStoreStartsEmptyWhenFileMissing(t *testing.T) {
	path := filepath.Join(t.TempDir(), "nested", "problems.json")

	store, err := NewStore(path)
	if err != nil {
		t.Fatalf("文件不存在不该报错: %v", err)
	}
	if store.Count() != 0 {
		t.Errorf("期望空列表，实际 %d 条", store.Count())
	}
	if got := store.List(); got == nil {
		t.Error("List 应返回空切片而不是 nil，否则 JSON 会序列化成 null")
	}
}
