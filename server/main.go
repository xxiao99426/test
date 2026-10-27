package main

import (
	"context"
	"errors"
	"flag"
	"log"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"
)

func main() {
	addr := flag.String("addr", ":8080", "监听地址，默认 :8080")
	dataPath := flag.String("data", "data/problems.json", "问题数据文件路径")
	flag.Parse()

	log.SetFlags(log.LstdFlags)

	store, err := NewStore(*dataPath)
	if err != nil {
		log.Fatalf("[FATAL] 初始化存储失败: %v", err)
	}
	log.Printf("[BOOT] 数据文件 %s，已加载 %d 条问题", *dataPath, store.Count())

	srv := &http.Server{
		Addr:              *addr,
		Handler:           NewServer(store).routes(),
		ReadHeaderTimeout: 5 * time.Second,
		ReadTimeout:       15 * time.Second,
		WriteTimeout:      15 * time.Second,
		IdleTimeout:       60 * time.Second,
	}

	go func() {
		log.Printf("[BOOT] 服务已启动 http://127.0.0.1%s", *addr)
		if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			log.Fatalf("[FATAL] 监听失败: %v", err)
		}
	}()

	// 优雅退出：Ctrl+C 之后给在途请求 5 秒收尾，避免写盘写一半被强杀。
	stop := make(chan os.Signal, 1)
	signal.Notify(stop, os.Interrupt, syscall.SIGTERM)
	<-stop

	log.Printf("[SHUTDOWN] 收到退出信号，开始收尾")
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	if err := srv.Shutdown(ctx); err != nil {
		log.Printf("[SHUTDOWN] 收尾超时: %v", err)
	}
	log.Printf("[SHUTDOWN] 已退出，数据已保存于 %s", *dataPath)
}
