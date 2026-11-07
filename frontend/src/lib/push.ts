import { apiUrl } from './api'

export interface PushSubscriptionData {
  endpoint: string
  keys: {
    p256dh: string
    auth: string
  }
}

export interface NotificationPreferences {
  enableScheduledBriefs: boolean
  enableBreakingNews: boolean
  scheduledBriefTimes: string[]
  timezone: string
}

export async function getVapidPublicKey(): Promise<string> {
  const response = await fetch(apiUrl('/api/push/vapid-public-key').toString())
  if (!response.ok) {
    throw new Error('获取VAPID公钥失败')
  }
  const data = await response.json()
  return data.publicKey
}

export async function subscribeToPush(
  registration: ServiceWorkerRegistration
): Promise<PushSubscriptionData> {
  const publicKey = await getVapidPublicKey()
  
  const subscription = await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: urlBase64ToUint8Array(publicKey)
  })

  const subscriptionData = subscription.toJSON() as PushSubscriptionData
  
  const response = await fetch(apiUrl('/api/push/subscription').toString(), {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(subscriptionData)
  })

  if (!response.ok) {
    throw new Error('订阅推送失败')
  }

  const result = await response.json()
  
  // 存储服务器返回的所有权令牌（用于后续取消订阅/更新偏好）
  if (result.ownershipToken) {
    localStorage.setItem(`push_token_${subscription.endpoint}`, result.ownershipToken)
  }

  return subscriptionData
}

export async function unsubscribeFromPush(
  registration: ServiceWorkerRegistration
): Promise<void> {
  const subscription = await registration.pushManager.getSubscription()
  if (!subscription) {
    return
  }

  const endpoint = subscription.endpoint
  const ownershipToken = localStorage.getItem(`push_token_${endpoint}`)

  if (!ownershipToken) {
    console.warn('未找到所有权令牌，无法取消订阅')
    await subscription.unsubscribe()
    return
  }

  await subscription.unsubscribe()

  await fetch(apiUrl('/api/push/subscription').toString(), {
    method: 'DELETE',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({ endpoint, ownershipToken })
  })

  localStorage.removeItem(`push_token_${endpoint}`)
}

export async function getNotificationPreferences(): Promise<NotificationPreferences> {
  const response = await fetch(apiUrl('/api/push/preferences').toString())
  if (!response.ok) {
    throw new Error('获取通知偏好失败')
  }
  return response.json()
}

export async function updateNotificationPreferences(
  registration: ServiceWorkerRegistration,
  preferences: NotificationPreferences
): Promise<void> {
  const subscription = await registration.pushManager.getSubscription()
  if (!subscription) {
    throw new Error('未找到推送订阅')
  }

  const endpoint = subscription.endpoint
  const ownershipToken = localStorage.getItem(`push_token_${endpoint}`)

  if (!ownershipToken) {
    throw new Error('未找到所有权令牌，无法更新偏好')
  }

  const response = await fetch(apiUrl('/api/push/preferences').toString(), {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      endpoint,
      ownershipToken,
      preferences
    })
  })

  if (!response.ok) {
    throw new Error('更新通知偏好失败')
  }
}

export async function checkPushSubscription(
  registration: ServiceWorkerRegistration
): Promise<PushSubscription | null> {
  return registration.pushManager.getSubscription()
}

export function isPushSupported(): boolean {
  return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window
}

export function getNotificationPermission(): NotificationPermission {
  return Notification.permission
}

export async function requestNotificationPermission(): Promise<NotificationPermission> {
  if (!('Notification' in window)) {
    throw new Error('浏览器不支持通知')
  }
  return Notification.requestPermission()
}

function urlBase64ToUint8Array(base64String: string): Uint8Array {
  const padding = '='.repeat((4 - (base64String.length % 4)) % 4)
  const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/')

  const rawData = window.atob(base64)
  const outputArray = new Uint8Array(rawData.length)

  for (let i = 0; i < rawData.length; ++i) {
    outputArray[i] = rawData.charCodeAt(i)
  }
  return outputArray
}

export function isIOSDevice(): boolean {
  return /iPad|iPhone|iPod/.test(navigator.userAgent) && !(window as any).MSStream
}

export function getIOSVersion(): number | null {
  const match = navigator.userAgent.match(/OS (\d+)_/)
  return match ? parseInt(match[1], 10) : null
}

export function isIOSPushSupported(): boolean {
  if (!isIOSDevice()) {
    return true
  }
  
  const version = getIOSVersion()
  return version !== null && version >= 16
}
