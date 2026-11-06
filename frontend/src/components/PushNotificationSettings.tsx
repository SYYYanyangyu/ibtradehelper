import { useState, useEffect, useCallback } from 'react'
import { Bell, BellOff, AlertCircle, CheckCircle, Smartphone, Info } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from './ui/Card'
import { Badge } from './ui/Badge'
import {
  isPushSupported,
  getNotificationPermission,
  requestNotificationPermission,
  subscribeToPush,
  unsubscribeFromPush,
  checkPushSubscription,
  getNotificationPreferences,
  updateNotificationPreferences,
  isIOSDevice,
  isIOSPushSupported,
  type NotificationPreferences
} from '../lib/push'
import { cn } from '../lib/utils'

export function PushNotificationSettings() {
  const [supported, setSupported] = useState(false)
  const [permission, setPermission] = useState<NotificationPermission>('default')
  const [subscribed, setSubscribed] = useState(false)
  const [loading, setLoading] = useState(true)
  const [preferences, setPreferences] = useState<NotificationPreferences>({
    enableScheduledBriefs: true,
    enableBreakingNews: true,
    scheduledBriefTimes: ['08:00', '21:00'],
    timezone: 'Asia/Shanghai'
  })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [swRegistration, setSwRegistration] = useState<ServiceWorkerRegistration | null>(null)

  const isIOS = isIOSDevice()
  const iosSupported = isIOSPushSupported()

  useEffect(() => {
    const checkStatus = async () => {
      setSupported(isPushSupported())
      setPermission(getNotificationPermission())

      if (isPushSupported() && 'serviceWorker' in navigator) {
        try {
          const registration = await navigator.serviceWorker.ready
          setSwRegistration(registration)
          
          const subscription = await checkPushSubscription(registration)
          setSubscribed(!!subscription)

          if (subscription) {
            const prefs = await getNotificationPreferences()
            setPreferences(prefs)
          }
        } catch (err) {
          console.error('检查推送订阅状态失败:', err)
        }
      }

      setLoading(false)
    }

    checkStatus()
  }, [])

  const showMessage = useCallback((type: 'success' | 'error', message: string) => {
    if (type === 'success') {
      setSuccess(message)
      setTimeout(() => setSuccess(null), 3500)
    } else {
      setError(message)
      setTimeout(() => setError(null), 3500)
    }
  }, [])

  const handleSubscribe = async () => {
    if (!swRegistration) {
      showMessage('error', 'Service Worker 未就绪')
      return
    }

    setLoading(true)
    setError(null)

    try {
      const perm = await requestNotificationPermission()
      setPermission(perm)

      if (perm !== 'granted') {
        showMessage('error', '通知权限被拒绝')
        setLoading(false)
        return
      }

      await subscribeToPush(swRegistration)
      await updateNotificationPreferences(preferences)
      
      setSubscribed(true)
      showMessage('success', '推送订阅成功')
    } catch (err) {
      console.error('订阅推送失败:', err)
      showMessage('error', err instanceof Error ? err.message : '订阅失败')
    } finally {
      setLoading(false)
    }
  }

  const handleUnsubscribe = async () => {
    if (!swRegistration) {
      showMessage('error', 'Service Worker 未就绪')
      return
    }

    setLoading(true)
    setError(null)

    try {
      await unsubscribeFromPush(swRegistration)
      setSubscribed(false)
      showMessage('success', '已取消推送订阅')
    } catch (err) {
      console.error('取消订阅失败:', err)
      showMessage('error', err instanceof Error ? err.message : '取消订阅失败')
    } finally {
      setLoading(false)
    }
  }

  const handleUpdatePreferences = async () => {
    setSaving(true)
    setError(null)

    try {
      await updateNotificationPreferences(preferences)
      showMessage('success', '偏好设置已保存')
    } catch (err) {
      console.error('保存偏好设置失败:', err)
      showMessage('error', err instanceof Error ? err.message : '保存失败')
    } finally {
      setSaving(false)
    }
  }

  if (loading) {
    return (
      <Card>
        <CardContent className="flex items-center justify-center py-8">
          <div className="flex items-center gap-2 text-muted">
            <div className="h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent" />
            <span className="text-sm">加载中...</span>
          </div>
        </CardContent>
      </Card>
    )
  }

  if (!supported || (isIOS && !iosSupported)) {
    return (
      <Card className="border-amber-500/30 bg-amber-500/5">
        <CardContent className="flex flex-col items-center gap-3 py-8 text-center">
          <AlertCircle className="text-amber-500" size={28} />
          {isIOS && !iosSupported ? (
            <>
              <p className="text-sm text-amber-600 dark:text-amber-400">
                iOS 推送通知需要 iOS 16.4 或更高版本
              </p>
              <p className="text-xs text-muted">
                请更新您的 iOS 系统以使用此功能
              </p>
            </>
          ) : (
            <>
              <p className="text-sm text-amber-600 dark:text-amber-400">
                当前浏览器不支持推送通知
              </p>
              <p className="text-xs text-muted">
                请使用 Chrome、Edge、Safari 或 Firefox 等现代浏览器
              </p>
            </>
          )}
        </CardContent>
      </Card>
    )
  }

  return (
    <div className="space-y-4">
      {(error || success) && (
        <div
          className={cn(
            'flex items-center gap-2 rounded-lg border px-3 py-2 text-sm',
            error
              ? 'border-down/25 bg-down/5 text-down'
              : 'border-up/25 bg-up/5 text-up'
          )}
        >
          {error ? <AlertCircle size={16} /> : <CheckCircle size={16} />}
          <span>{error || success}</span>
        </div>
      )}

      <Card>
        <CardHeader>
          <div className="flex items-start gap-3">
            <div className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-primary/10 text-primary">
              <Bell size={20} />
            </div>
            <div className="min-w-0 flex-1">
              <CardTitle className="flex items-center gap-2">
                推送通知
              </CardTitle>
              <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-muted-2">
                {subscribed ? (
                  <Badge tone="primary">已订阅</Badge>
                ) : (
                  <Badge tone="neutral">未订阅</Badge>
                )}
                {permission === 'granted' && (
                  <Badge tone="primary">已授权</Badge>
                )}
              </div>
            </div>
          </div>
        </CardHeader>

        <CardContent className="space-y-4">
          {isIOS && (
            <div className="flex items-start gap-2 rounded-lg border border-primary/25 bg-primary/5 px-3 py-2 text-xs text-primary">
              <Info size={14} className="mt-0.5 shrink-0" />
              <div className="min-w-0 flex-1 space-y-1">
                <p className="font-medium">iOS 安装说明</p>
                <ol className="list-inside list-decimal space-y-0.5 text-muted">
                  <li>点击 Safari 底部的「分享」按钮</li>
                  <li>向下滚动，选择「添加到主屏幕」</li>
                  <li>点击右上角「添加」完成安装</li>
                  <li>从主屏幕打开应用后即可订阅推送</li>
                </ol>
              </div>
            </div>
          )}

          {!subscribed ? (
            <div className="space-y-3">
              <p className="text-sm text-muted">
                订阅后，您将在每个工作日的早晨和晚间收到金融简报推送，以及重要突发新闻提醒。
              </p>
              <button
                onClick={handleSubscribe}
                disabled={loading}
                className="flex w-full items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-sm font-medium text-white transition hover:opacity-90 disabled:opacity-50"
              >
                <Bell size={16} />
                订阅推送通知
              </button>
            </div>
          ) : (
            <div className="space-y-4">
              <div className="space-y-3">
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={preferences.enableScheduledBriefs}
                    onChange={(e) =>
                      setPreferences({ ...preferences, enableScheduledBriefs: e.target.checked })
                    }
                    className="h-4 w-4 rounded border-border bg-surface-2 text-primary focus:ring-2 focus:ring-primary focus:ring-offset-0"
                  />
                  <span className="font-medium text-foreground">定时金融简报</span>
                </label>
                {preferences.enableScheduledBriefs && (
                  <div className="ml-6 space-y-2 text-xs text-muted">
                    <p>工作日推送时间（北京时间）：</p>
                    <div className="flex flex-wrap gap-2">
                      {preferences.scheduledBriefTimes.map((time) => (
                        <Badge key={time} tone="neutral">
                          {time}
                        </Badge>
                      ))}
                    </div>
                  </div>
                )}
              </div>

              <div className="space-y-3">
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={preferences.enableBreakingNews}
                    onChange={(e) =>
                      setPreferences({ ...preferences, enableBreakingNews: e.target.checked })
                    }
                    className="h-4 w-4 rounded border-border bg-surface-2 text-primary focus:ring-2 focus:ring-primary focus:ring-offset-0"
                  />
                  <span className="font-medium text-foreground">重要突发新闻</span>
                </label>
                {preferences.enableBreakingNews && (
                  <p className="ml-6 text-xs text-muted">
                    仅推送经过筛选的高优先级市场动态，已自动去重
                  </p>
                )}
              </div>

              <div className="flex gap-2">
                <button
                  onClick={handleUpdatePreferences}
                  disabled={saving}
                  className="flex flex-1 items-center justify-center gap-2 rounded-lg border border-primary bg-primary/10 px-4 py-2 text-sm font-medium text-primary transition hover:bg-primary/20 disabled:opacity-50"
                >
                  {saving ? (
                    <>
                      <div className="h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent" />
                      保存中...
                    </>
                  ) : (
                    '保存偏好'
                  )}
                </button>
                <button
                  onClick={handleUnsubscribe}
                  disabled={loading}
                  className="flex items-center gap-2 rounded-lg border border-border px-4 py-2 text-sm font-medium text-muted transition hover:bg-surface-2 hover:text-foreground disabled:opacity-50"
                >
                  <BellOff size={16} />
                  取消订阅
                </button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {isIOS && (
        <Card className="border-primary/20 bg-primary/5">
          <CardContent className="flex items-start gap-3 py-4">
            <Smartphone size={20} className="shrink-0 text-primary" />
            <div className="min-w-0 flex-1 space-y-1 text-xs">
              <p className="font-medium text-primary">移动端安装提示</p>
              <p className="text-muted">
                将应用添加到主屏幕后，可以像原生App一样使用，享受更好的体验和离线访问能力。
              </p>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
