const sharp = require('sharp');
const fs = require('node:fs/promises');
const path = require('node:path');
const root = path.join(__dirname, '..');
async function render(size) {
  const mask = Buffer.from(`<svg width="${size}" height="${size}"><rect width="${size}" height="${size}" rx="${size * .22}" fill="white"/></svg>`);
  return sharp(path.join(root, 'logo.png')).resize(size, size, { fit: 'cover' }).ensureAlpha().composite([{ input: mask, blend: 'dest-in' }]).png().toBuffer();
}
(async () => {
  await fs.mkdir(path.join(root, 'assets'), { recursive: true });
  await fs.writeFile(path.join(root, 'assets/Logo-rounded.png'), await render(512));
  const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
  const images = await Promise.all(sizes.map(render));
  const header = Buffer.alloc(6 + sizes.length * 16);
  header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  images.forEach((image, i) => {
    const position = 6 + i * 16;
    header[position] = header[position + 1] = sizes[i] === 256 ? 0 : sizes[i];
    header.writeUInt16LE(1, position + 4); header.writeUInt16LE(32, position + 6);
    header.writeUInt32LE(image.length, position + 8); header.writeUInt32LE(offset, position + 12); offset += image.length;
  });
  await fs.writeFile(path.join(root, 'assets/CodexMonitor.ico'), Buffer.concat([header, ...images]));
  console.log('Generated transparent rounded logo and 9-size ICO.');
})().catch(error => { console.error(error.message); process.exitCode = 1; });
