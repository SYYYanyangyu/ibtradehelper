#!/usr/bin/env node

import fs from 'fs';
import { createCanvas } from 'canvas';

const sizes = [72, 96, 128, 144, 152, 192, 384, 512];
const outputDir = '../frontend/public/icons';

// 创建输出目录
if (!fs.existsSync(outputDir)) {
  fs.mkdirSync(outputDir, { recursive: true });
}

sizes.forEach(size => {
  const canvas = createCanvas(size, size);
  const ctx = canvas.getContext('2d');

  // 背景色
  ctx.fillStyle = '#0a0e12';
  ctx.fillRect(0, 0, size, size);

  // 圆角矩形
  const radius = size * 0.15;
  ctx.beginPath();
  ctx.moveTo(radius, 0);
  ctx.lineTo(size - radius, 0);
  ctx.quadraticCurveTo(size, 0, size, radius);
  ctx.lineTo(size, size - radius);
  ctx.quadraticCurveTo(size, size, size - radius, size);
  ctx.lineTo(radius, size);
  ctx.quadraticCurveTo(0, size, 0, size - radius);
  ctx.lineTo(0, radius);
  ctx.quadraticCurveTo(0, 0, radius, 0);
  ctx.closePath();
  ctx.fill();

  // 文字
  ctx.fillStyle = '#3b82f6';
  ctx.font = `bold ${size * 0.35}px Arial, sans-serif`;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText('IB', size / 2, size / 2);

  // 保存
  const buffer = canvas.toBuffer('image/png');
  const filename = `${outputDir}/icon-${size}x${size}.png`;
  fs.writeFileSync(filename, buffer);
  console.log(`✓ Generated ${filename}`);
});

console.log('\n✓ All PNG icons generated successfully');
