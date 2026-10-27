package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sync"
	"time"
)

// ErrNotFound 表示按 ID 查找问题失败。
var ErrNotFound = errors.New("problem not found")

// Store 是问题的内存索引 + JSON 文件持久化。
//
// 并发模型：读写共用一把 RWMutex。本题数据量很小（几十到几百条），
// 用互斥锁而不是更复杂的方案，是为了让「为什么并发安全」能被一句话说清。
type Store struct {
	mu    sync.RWMutex
	path  string
	items []Problem
}

// NewStore 加载磁盘上的数据文件。文件不存在时以空列表启动，不算错误——
// 首次运行就是这个状态。
func NewStore(path string) (*Store, error) {
	s := &Store{path: path, items: []Problem{}}

	data, err := os.ReadFile(path)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return s, nil
		}
		return nil, fmt.Errorf("读取数据文件失败: %w", err)
	}

	// 空文件（或只有空白）按空列表处理，避免手工建了空文件后启动即崩。
	if len(data) == 0 {
		return s, nil
	}

	var items []Problem
	if err := json.Unmarshal(data, &items); err != nil {
		return nil, fmt.Errorf("解析数据文件失败（%s）: %w", path, err)
	}
	if items == nil {
		items = []Problem{}
	}
	s.items = items
	return s, nil
}

// List 返回全部问题的副本。返回副本，调用方无法在锁外改动内部切片。
func (s *Store) List() []Problem {
	s.mu.RLock()
	defer s.mu.RUnlock()

	out := make([]Problem, len(s.items))
	copy(out, s.items)
	return out
}

// Count 返回当前记录数，供健康检查使用。
func (s *Store) Count() int {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return len(s.items)
}

// Get 按 ID 查找单条记录。
func (s *Store) Get(id string) (Problem, error) {
	s.mu.RLock()
	defer s.mu.RUnlock()

	for _, p := range s.items {
		if p.ID == id {
			return p, nil
		}
	}
	return Problem{}, ErrNotFound
}

// Add 追加一条记录并落盘。
// 先写盘、成功后再替换内存：写盘失败时不会出现「内存有、磁盘没有」的假成功。
func (s *Store) Add(p Problem) error {
	s.mu.Lock()
	defer s.mu.Unlock()

	next := make([]Problem, len(s.items), len(s.items)+1)
	copy(next, s.items)
	next = append(next, p)

	if err := s.persistLocked(next); err != nil {
		return err
	}
	s.items = next
	return nil
}

// UpdateStatus 修改指定问题的状态并落盘，返回修改后的完整记录。
func (s *Store) UpdateStatus(id, status string) (Problem, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	idx := -1
	for i := range s.items {
		if s.items[i].ID == id {
			idx = i
			break
		}
	}
	if idx < 0 {
		return Problem{}, ErrNotFound
	}

	next := make([]Problem, len(s.items))
	copy(next, s.items)
	next[idx].Status = status
	next[idx].UpdatedAt = time.Now().UTC()

	if err := s.persistLocked(next); err != nil {
		return Problem{}, err
	}
	s.items = next
	return next[idx], nil
}

// persistLocked 把整份数据原子地写回磁盘。
//
// 先用「临时文件 + 重命名」而不是直接覆盖原文件：如果在写入中途进程被杀，
// 原文件仍然完整，不会留下半截 JSON 导致下次启动解析失败。
// 调用方必须已持有写锁，故命名带 Locked 后缀。
func (s *Store) persistLocked(items []Problem) error {
	data, err := json.MarshalIndent(items, "", "  ")
	if err != nil {
		return fmt.Errorf("序列化失败: %w", err)
	}

	if dir := filepath.Dir(s.path); dir != "" {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			return fmt.Errorf("创建数据目录失败: %w", err)
		}
	}

	tmp := s.path + ".tmp"
	f, err := os.Create(tmp)
	if err != nil {
		return fmt.Errorf("创建临时文件失败: %w", err)
	}

	if _, err := f.Write(data); err != nil {
		f.Close()
		os.Remove(tmp)
		return fmt.Errorf("写入临时文件失败: %w", err)
	}
	// Sync 确保数据真正落到磁盘，再重命名。否则断电可能得到 0 字节文件。
	if err := f.Sync(); err != nil {
		f.Close()
		os.Remove(tmp)
		return fmt.Errorf("刷盘失败: %w", err)
	}
	if err := f.Close(); err != nil {
		os.Remove(tmp)
		return fmt.Errorf("关闭临时文件失败: %w", err)
	}

	// Windows 上 os.Rename 对已存在的目标文件执行的是替换语义（MoveFileEx + REPLACE_EXISTING），
	// 所以这里不需要先删除旧文件，也就没有「删完再改名失败」的窗口。
	if err := os.Rename(tmp, s.path); err != nil {
		os.Remove(tmp)
		return fmt.Errorf("替换数据文件失败: %w", err)
	}
	return nil
}
