// Rasterises tools/icon.svg into build/icon.png (1024px) and build/icon.ico
// (multi-resolution) using Electron for rendering and png2icons for the ICO.
//
//   npm run make-icon        (defined in package.json)
//
// Run this whenever tools/icon.svg changes; commit the generated build/ assets.

const { app, BrowserWindow } = require('electron');
const fs = require('fs');
const path = require('path');
const png2icons = require('png2icons');

const SIZE = 1024;
const ROOT = path.join(__dirname, '..');
const SVG_PATH = path.join(__dirname, 'icon.svg');
// build/ -> consumed by electron-builder (installer + embedded exe icon).
// assets/ -> shipped inside the app (asar) for the runtime window / tray icon.
const OUT_DIRS = [path.join(ROOT, 'build'), path.join(ROOT, 'assets')];
const OUT_DIR = OUT_DIRS[0];
const PNG_PATH = path.join(OUT_DIR, 'icon.png');
const ICO_PATH = path.join(OUT_DIR, 'icon.ico');

async function main() {
  for (const d of OUT_DIRS) if (!fs.existsSync(d)) fs.mkdirSync(d, { recursive: true });

  const svg = fs.readFileSync(SVG_PATH, 'utf8');
  const html =
    '<!doctype html><meta charset="utf-8">' +
    '<style>html,body{margin:0;padding:0;background:transparent}' +
    `img{display:block;width:${SIZE}px;height:${SIZE}px}</style>` +
    `<img src="data:image/svg+xml;base64,${Buffer.from(svg).toString('base64')}">`;

  const win = new BrowserWindow({
    width: SIZE,
    height: SIZE,
    show: false,
    frame: false,
    transparent: true,
    backgroundColor: '#00000000',
    useContentSize: true,
    webPreferences: { offscreen: true, sandbox: true }
  });

  await win.loadURL('data:text/html;base64,' + Buffer.from(html).toString('base64'));
  // Give the SVG (blur filters, gradients) a moment to paint.
  await new Promise((r) => setTimeout(r, 400));

  const image = await win.webContents.capturePage({ x: 0, y: 0, width: SIZE, height: SIZE });
  const pngBuffer = image.toPNG();

  // BILINEAR downscaling; last arg false -> BMP for small frames, PNG for large.
  const ico = png2icons.createICO(pngBuffer, png2icons.BILINEAR, 0, false);
  if (!ico) throw new Error('png2icons.createICO returned null');

  for (const d of OUT_DIRS) {
    fs.writeFileSync(path.join(d, 'icon.png'), pngBuffer);
    fs.writeFileSync(path.join(d, 'icon.ico'), ico);
    console.log(`wrote ${path.relative(ROOT, d)}/icon.png (${pngBuffer.length} b) + icon.ico (${ico.length} b)`);
  }

  win.destroy();
  app.quit();
}

app.disableHardwareAcceleration();
app.whenReady().then(main).catch((err) => {
  console.error(err);
  app.exit(1);
});
