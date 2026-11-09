import { describe, it, expect, beforeEach, vi } from 'vitest'
import {
  isPushSupported,
  isIOSDevice,
  isIOSPushSupported,
  getIOSVersion
} from '../push'

describe('Push Notification Utils', () => {
  describe('isPushSupported', () => {
    it('should return true when all required APIs are available', () => {
      global.navigator = {
        serviceWorker: {}
      } as any
      global.window = {
        PushManager: class {},
        Notification: class {}
      } as any

      expect(isPushSupported()).toBe(true)
    })

    it('should return false when serviceWorker is not available', () => {
      global.navigator = {} as any
      global.window = {
        PushManager: class {},
        Notification: class {}
      } as any

      expect(isPushSupported()).toBe(false)
    })
  })

  describe('isIOSDevice', () => {
    it('should return true for iPad', () => {
      Object.defineProperty(global.navigator, 'userAgent', {
        value: 'Mozilla/5.0 (iPad; CPU OS 16_0 like Mac OS X)',
        configurable: true
      })

      expect(isIOSDevice()).toBe(true)
    })

    it('should return true for iPhone', () => {
      Object.defineProperty(global.navigator, 'userAgent', {
        value: 'Mozilla/5.0 (iPhone; CPU iPhone OS 16_0 like Mac OS X)',
        configurable: true
      })

      expect(isIOSDevice()).toBe(true)
    })

    it('should return false for Android', () => {
      Object.defineProperty(global.navigator, 'userAgent', {
        value: 'Mozilla/5.0 (Linux; Android 11)',
        configurable: true
      })

      expect(isIOSDevice()).toBe(false)
    })
  })

  describe('getIOSVersion', () => {
    it('should extract iOS version correctly', () => {
      Object.defineProperty(global.navigator, 'userAgent', {
        value: 'Mozilla/5.0 (iPhone; CPU iPhone OS 16_4 like Mac OS X)',
        configurable: true
      })

      expect(getIOSVersion()).toBe(16)
    })

    it('should return null for non-iOS device', () => {
      Object.defineProperty(global.navigator, 'userAgent', {
        value: 'Mozilla/5.0 (Linux; Android 11)',
        configurable: true
      })

      expect(getIOSVersion()).toBeNull()
    })
  })

  describe('isIOSPushSupported', () => {
    it('should return true for non-iOS devices', () => {
      Object.defineProperty(global.navigator, 'userAgent', {
        value: 'Mozilla/5.0 (Linux; Android 11)',
        configurable: true
      })

      expect(isIOSPushSupported()).toBe(true)
    })

    it('should return true for iOS 16+', () => {
      Object.defineProperty(global.navigator, 'userAgent', {
        value: 'Mozilla/5.0 (iPhone; CPU iPhone OS 16_4 like Mac OS X)',
        configurable: true
      })

      expect(isIOSPushSupported()).toBe(true)
    })

    it('should return false for iOS 15', () => {
      Object.defineProperty(global.navigator, 'userAgent', {
        value: 'Mozilla/5.0 (iPhone; CPU iPhone OS 15_0 like Mac OS X)',
        configurable: true
      })

      expect(isIOSPushSupported()).toBe(false)
    })
  })
})
