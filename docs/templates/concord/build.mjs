import { readFileSync, writeFileSync, readdirSync, statSync, existsSync } from 'node:fs'
import { join, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import { compile, optimize } from '@tailwindcss/node'

const __dirname = dirname(fileURLToPath(import.meta.url))
const base = __dirname

// Gather candidate class tokens from files Tailwind should generate
// utilities for: the master template, theme JS, and markdown content.
function walk(dir, out) {
  if (!existsSync(dir)) return out
  for (const name of readdirSync(dir)) {
    const p = join(dir, name)
    const st = statSync(p)
    if (st.isDirectory()) walk(p, out)
    else if (/\.(tmpl|md|js|html)$/.test(name)) out.push(p)
  }
  return out
}

const sources = [join(base, 'layout', '_master.tmpl'), join(base, 'public', 'theme.js')]
walk(join(base, '..', '..', 'docs'), sources)
walk(join(base, '..', '..', 'api'), sources)
sources.push(join(base, '..', '..', 'index.md'))

const tokenRe = /[^\s"'=<>]+/g
const candidates = new Set()
for (const f of sources) {
  if (!existsSync(f)) continue
  const txt = readFileSync(f, 'utf8')
  let m
  while ((m = tokenRe.exec(txt))) {
    const t = m[0]
    if (t.length > 1 && !/^\d/.test(t)) candidates.add(t)
  }
}

const css = readFileSync(join(base, 'src', 'tailwind.css'), 'utf8')
const compiler = await compile(css, {
  base,
  from: join(base, 'src', 'tailwind.css'),
  onDependency: () => {},
})

let out = compiler.build([...candidates])
out = optimize(out, { file: 'concord.css', minify: true }).code

// DaisyUI emits a few decorative backgrounds even when the matching
// components are not used. Keep the generated theme flat as well.
const flatGeneratedBackgrounds = new Map([
  [
    'background-image:var(--page-scroll-lock) linear-gradient(var(--root-bg,#0000), var(--root-bg,#0000));',
    'background-image:none;',
  ],
  [
    'linear-gradient(var(--root-bg,#0000), var(--root-bg,#0000)) var(--root-bg,#0000)',
    'var(--root-bg,#0000)',
  ],
  [
    'linear-gradient(var(--root-bg,#0000), var(--root-bg,#0000)) color-mix(in srgb, var(--root-bg,#0000), oklch(0% 0 0) calc(var(--page-has-backdrop,0) * 40%))',
    'color-mix(in srgb, var(--root-bg,#0000), oklch(0% 0 0) calc(var(--page-has-backdrop,0) * 40%))',
  ],
  [
    'radial-gradient(circle at 35% 30%, oklch(1 0 0 / calc(var(--depth) * .5)), #0000)',
    'none',
  ],
  [
    'linear-gradient(45deg,#0000 50%,currentColor 50%),linear-gradient(135deg,currentColor 50%,#0000 50%)',
    'none',
  ],
  [
    'linear-gradient(135deg,#0000 50%,currentColor 50%),linear-gradient(45deg,currentColor 50%,#0000 50%)',
    'none',
  ],
])

for (const [background, replacement] of flatGeneratedBackgrounds) {
  out = out.replaceAll(background, replacement)
}

// DaisyUI also emits unused aura keyframes. Remove them so the generated
// stylesheet does not carry decorative glow effects that Concord never uses.
out = out.replace(/@keyframes aura-glow(?:-after)?\{(?:[^{}]|\{[^{}]*\})*\}/g, '')
out = out.replace(/text-shadow:[^;}]+;?/g, '')

const gradientPattern = /(?:linear|radial|conic)-gradient\(/i
const glowPattern = /(?:text-shadow\s*:|drop-shadow\(|@keyframes[^{}]*glow|box-shadow\s*:\s*0\s+0\s+(?:[1-9]|\.\d)|box-shadow\s*:[^;]*,\s*0\s+0\s+(?:[1-9]|\.\d))/i
const mainCss = readFileSync(join(base, 'public', 'main.css'), 'utf8')
if (gradientPattern.test(out) || gradientPattern.test(mainCss)) {
  const generatedMatches = out.match(/[^{}]{0,180}(?:linear|radial|conic)-gradient\([^;}]+[^{}]{0,220}/gi) ?? []
  const customMatches = mainCss.match(/(?:linear|radial|conic)-gradient\([^;}]+/gi) ?? []
  throw new Error(`Gradient found after building the flat Concord theme: ${[...generatedMatches, ...customMatches].join(' | ')}`)
}
if (glowPattern.test(out) || glowPattern.test(mainCss)) {
  throw new Error('Glow effect found after building the flat Concord theme')
}

writeFileSync(join(base, 'public', 'concord.css'), out)
console.log(`concord.css written (${out.length} bytes, ${candidates.size} candidates scanned)`)
