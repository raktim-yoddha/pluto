// Copies the official agent marks Hiveory bundles (LobeHub Icons, MIT, and official GitHub avatars) into the pet:
//   node scripts/import-icons.mjs ../Hiveory/src/main/services/cli/adapters/icon-data.ts
// Marks drawn in Hiveory's light grey (for dark backgrounds) also get a "-light" copy in near-black for light mode.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'

const source = process.argv[2]
if (!source) throw new Error('Pass the path to Hiveory\'s icon-data.ts')
const out = join(import.meta.dirname, '..', 'src', 'Pluto', 'Assets', 'Agents')
mkdirSync(out, { recursive: true })
const icons = [...readFileSync(source, 'utf8').matchAll(/"([a-z0-9-]+)": "data:image\/(svg\+xml|png);base64,([^"]+)"/g)]
for (const [, id, type, data] of icons) {
  const bytes = Buffer.from(data, 'base64')
  if (type === 'png') {
    writeFileSync(join(out, `${id}.png`), bytes)
    continue
  }
  const svg = bytes.toString('utf8')
  writeFileSync(join(out, `${id}.svg`), svg)
  if (/#DCD6CC|currentColor/i.test(svg)) {
    writeFileSync(join(out, `${id}-light.svg`), svg.replace(/#DCD6CC|currentColor/gi, '#1F1F1F'))
  }
}
console.log(`${icons.length} icons → ${out}`)
