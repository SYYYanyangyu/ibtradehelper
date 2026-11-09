#!/usr/bin/env node

const fs = require('fs');
const path = require('path');

const sizes = [72, 96, 128, 144, 152, 192, 384, 512];
const iconsDir = path.join(__dirname, '..', 'frontend', 'public', 'icons');

if (!fs.existsSync(iconsDir)) {
  fs.mkdirSync(iconsDir, { recursive: true });
}

const svgTemplate = (size) => `<?xml version="1.0" encoding="UTF-8"?>
<svg width="${size}" height="${size}" viewBox="0 0 ${size} ${size}" xmlns="http://www.w3.org/2000/svg">
  <rect width="${size}" height="${size}" fill="#0a0e12" rx="${size * 0.15}"/>
  <text x="50%" y="50%" text-anchor="middle" dy=".35em" fill="#3b82f6" font-family="system-ui, -apple-system, sans-serif" font-size="${size * 0.35}" font-weight="700">IB</text>
</svg>`;

console.log('生成 PWA 图标占位符...');

sizes.forEach(size => {
  const svg = svgTemplate(size);
  const filename = `icon-${size}x${size}.svg`;
  const filepath = path.join(iconsDir, filename);
  
  fs.writeFileSync(filepath, svg, 'utf8');
  console.log(`✓ 已生成 ${filename}`);
});

console.log('\n提示：这些是简单的SVG占位符图标。');
console.log('建议在生产环境中替换为专业设计的PNG图标。');
console.log('如需转换为PNG，可使用 ImageMagick 或在线工具。');
console.log('\n示例命令（需要 ImageMagick）：');
console.log('  convert icon-192x192.svg icon-192x192.png');
