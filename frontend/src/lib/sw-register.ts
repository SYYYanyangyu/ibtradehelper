export async function registerServiceWorker(): Promise<ServiceWorkerRegistration | null> {
  if (!('serviceWorker' in navigator)) {
    console.warn('Service Worker 不受支持')
    return null
  }

  try {
    const registration = await navigator.serviceWorker.register('/sw.js', {
      scope: '/'
    })

    registration.addEventListener('updatefound', () => {
      const newWorker = registration.installing
      if (!newWorker) return

      newWorker.addEventListener('statechange', () => {
        if (newWorker.state === 'installed' && navigator.serviceWorker.controller) {
          console.log('Service Worker 有新版本可用')
          if (confirm('应用有更新，是否刷新页面？')) {
            window.location.reload()
          }
        }
      })
    })

    console.log('Service Worker 注册成功:', registration.scope)
    return registration
  } catch (error) {
    console.error('Service Worker 注册失败:', error)
    return null
  }
}

export async function unregisterServiceWorker(): Promise<boolean> {
  if (!('serviceWorker' in navigator)) {
    return false
  }

  try {
    const registration = await navigator.serviceWorker.getRegistration()
    if (registration) {
      return await registration.unregister()
    }
    return false
  } catch (error) {
    console.error('Service Worker 注销失败:', error)
    return false
  }
}
