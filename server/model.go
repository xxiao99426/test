package main

import (
	"crypto/rand"
	"fmt"
	"time"
)

// 优先级与状态的合法取值。三端（Unity / React / Go）共用这一份定义，
// 前端和 Unity 侧的同名常量必须与此保持一致。
const (
	PriorityLow    = "low"
	PriorityMedium = "medium"
	PriorityHigh   = "high"

	StatusOpen       = "open"
	StatusInProgress = "in_progress"
	StatusResolved   = "resolved"
)

var validPriorities = []string{PriorityLow, PriorityMedium, PriorityHigh}
var validStatuses = []string{StatusOpen, StatusInProgress, StatusResolved}

func isValidPriority(v string) bool {
	for _, p := range validPriorities {
		if p == v {
			return true
		}
	}
	return false
}

func isValidStatus(v string) bool {
	for _, s := range validStatuses {
		if s == v {
			return true
		}
	}
	return false
}

// Position 是 Unity 世界坐标，字段名与试题第 4 节数据结构的 x / y / z 一致。
type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

// Problem 是与试题第 4 节完全对齐的问题记录。
// 时间使用 RFC3339（UTC），Unity 侧按字符串接收，React 侧用 new Date() 转本地时区。
type Problem struct {
	ID          string    `json:"id"`
	Title       string    `json:"title"`
	Description string    `json:"description"`
	Priority    string    `json:"priority"`
	Status      string    `json:"status"`
	Position    Position  `json:"position"`
	CreatedAt   time.Time `json:"createdAt"`
	UpdatedAt   time.Time `json:"updatedAt"`
}

// ProblemList 是 GET /api/problems 的响应体。
// 刻意包一层对象而不是直接返回数组：Unity 的 JsonUtility 无法反序列化顶层数组。
type ProblemList struct {
	Items []Problem `json:"items"`
	Total int       `json:"total"`
}

// ErrorBody 是统一错误响应。字段平铺，便于 Unity 侧用 JsonUtility 直接解析。
type ErrorBody struct {
	Code    string `json:"code"`
	Message string `json:"message"`
	Field   string `json:"field,omitempty"`
}

// newUUIDv4 生成 RFC 4122 版本 4 UUID。
// 手写而不引第三方库：只有 10 行，且面试时能逐行解释。
func newUUIDv4() (string, error) {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		return "", err
	}
	b[6] = (b[6] & 0x0f) | 0x40 // version 4
	b[8] = (b[8] & 0x3f) | 0x80 // variant RFC 4122
	return fmt.Sprintf("%x-%x-%x-%x-%x", b[0:4], b[4:6], b[6:8], b[8:10], b[10:16]), nil
}
