import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import './styles.css'

const container = document.getElementById('root')
if (!container) {
  // 明确抛错比留下一个空白页面更容易定位问题
  throw new Error('未找到 #root 容器，index.html 可能被破坏')
}

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
