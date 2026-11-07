#!/usr/bin/env node

const crypto = require('crypto');

function generateVapidKeys() {
  const ecdh = crypto.createECDH('prime256v1');
  ecdh.generateKeys();

  const publicKey = ecdh.getPublicKey('base64', 'compressed');
  const privateKey = ecdh.getPrivateKey('base64');

  return { publicKey, privateKey };
}

console.log('生成 VAPID 密钥对（用于 Web Push）\n');

const keys = generateVapidKeys();

console.log('公钥（PublicKey）：');
console.log(keys.publicKey);
console.log('\n私钥（PrivateKey，请保密）：');
console.log(keys.privateKey);

console.log('\n将以下配置添加到 appsettings.Secrets.json 或环境变量中：\n');
console.log(JSON.stringify({
  "Vapid": {
    "PublicKey": keys.publicKey,
    "PrivateKey": keys.privateKey,
    "Subject": "mailto:admin@example.com"
  }
}, null, 2));

console.log('\n注意：');
console.log('1. 私钥必须保密，不要提交到代码仓库');
console.log('2. Subject 应该是一个有效的 mailto: 或 https:// URL');
console.log('3. 生产环境建议使用环境变量注入密钥');
