#!/bin/bash

# 使用 Node.js canvas 生成 PNG 图标
# 这是一个简单的生成脚本，生产环境应该使用专业设计的图标

ICONS_DIR="../frontend/public/icons"
SIZES=(72 96 128 144 152 192 384 512)

# 创建一个简单的 HTML canvas 生成器
cat > /tmp/icon-generator.html << 'EOF'
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>Icon Generator</title>
</head>
<body>
    <canvas id="canvas"></canvas>
    <script>
        const sizes = [72, 96, 128, 144, 152, 192, 384, 512];
        
        sizes.forEach(size => {
            const canvas = document.getElementById('canvas');
            canvas.width = size;
            canvas.height = size;
            const ctx = canvas.getContext('2d');
            
            // 背景
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
            ctx.font = `bold ${size * 0.35}px system-ui, -apple-system, sans-serif`;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText('IB', size / 2, size / 2);
            
            // 下载
            const link = document.createElement('a');
            link.download = `icon-${size}x${size}.png`;
            link.href = canvas.toDataURL('image/png');
            link.click();
        });
    </script>
</body>
</html>
EOF

echo "PNG图标生成器已创建：/tmp/icon-generator.html"
echo "请在浏览器中打开该文件，它会自动生成并下载所有尺寸的PNG图标"
echo "然后将下载的图标移动到 $ICONS_DIR 目录"
