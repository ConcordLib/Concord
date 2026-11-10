# Concord Brand Palette

The visual identity for Concord, a .NET runtime method-patching library for mod
authors. Two anchor colors (teal + violet) with warm accents for data and a
warm near-black for dark surfaces.

---

## Anchor colors

| Role | Name | Hex | RGB | Notes |
| --- | --- | --- | --- | --- |
| Primary | Teal | `#2BB8A8` | `43, 184, 168` | Theme primary and solid accent. The raster logo uses brighter cyan tones. |
| Accent | Violet | `#7E5BD6` | `126, 91, 214` | Theme secondary. The raster logo uses brighter violet tones. |
| Neutral | Near-black | `#16110B` | `22, 17, 11` | Neutral shell color. The dark page background is `#0D0F17`. |

---

## Extended palette (docs theme)

Derived shades and complementary colors used across the documentation site
(`templates/concord/public/main.css`). All defined as CSS custom properties on
`:root`.

### Brand scale

| Token | Hex | Role |
| --- | --- | --- |
| `--cc-teal` | `#2BB8A8` | Primary brand color, list markers, inline code, active borders, and solid accents |
| `--cc-teal-light` | `#6FE0D2` | Dark-theme links and light accents |
| `--cc-purple` | `#7E5BD6` | Secondary brand color |

### Link and syntax variants

| Hex | Role |
| --- | --- |
| `#9D7FE8` | Keywords, built-ins, types, and class names in syntax highlighting |
| `#8FEDE0` | Dark-theme link hover |
| `#0D6B63` | Light-theme links |
| `#0A5A53` | Light-theme link hover |

### Warm accents (complementary)

| Token         | Hex       | Role |
|---------------|-----------|------|
| `--cc-amber`  | `#FBBF24` | WARNING callout accent |
| `--cc-mint`   | `#34D399` | TIP callout accent |
| `--cc-rose`   | `#FB7185` | CAUTION callout accent |
| (no token)    | `#E8C547` | Strings in syntax highlighting (warm gold) |
| (no token)    | `#F0883E` | Numbers / literals in syntax highlighting (warm orange) |

### Neutrals (dark theme)

| Token               | Hex       | Role |
|---------------------|-----------|------|
| `--cc-bg`           | `#0D0F17` | Page background |
| `--cc-bg-elevated`  | `#141823` | Navbar, footer, offcanvas |
| `--cc-bg-card`      | `#1A1F2E` | Cards, table headers, callouts, and API panels |
| `--cc-bg-hover`     | `#232938` | Hover states |
| `--cc-bg-code`      | `#111420` | Code block background |
| `--cc-text`         | `#E2E8F0` | Body text |
| `--cc-text-secondary` | `#94A3B8` | Secondary text, breadcrumbs |
| `--cc-text-muted`   | `#64748B` | Breadcrumbs, secondary navigation, placeholders, and code controls |
| `--cc-text-bright`  | `#F1F5F9` | Headings, strong text |

### Neutrals (light theme)

| Token               | Hex       | Role |
|---------------------|-----------|------|
| `--cc-bg`           | `#F8F7FC` | Page background (warm off-white) |
| `--cc-bg-elevated`  | `#FFFFFF` | Navbar, footer, content cards |
| `--cc-bg-card`      | `#FFFFFF` | Cards, callouts |
| `--cc-bg-hover`     | `#F1EFF7` | Hover states |
| `--cc-bg-code`      | `#F5F3FA` | Code block background |
| `--cc-text`         | `#1E1B2E` | Body text |
| `--cc-text-secondary` | `#4B5563` | Secondary text |
| `--cc-text-muted`   | `#6B7280` | Muted text |
| `--cc-text-bright`  | `#0F0A1E` | Headings, strong text |

---

## Syntax highlighting

The code theme is built around the two anchor colors: violet for structural
elements (keywords, types) and teal for active elements (functions,
attributes, variables). Warm gold and orange provide contrast for data.

| Highlight.js selector | Hex | Use |
| --- | --- | --- |
| `.hljs-keyword`, `.hljs-built_in`, `.hljs-type`, `.hljs-class .hljs-title` | `#9D7FE8` | Keywords, built-ins, types, and class names |
| `.hljs-title`, `.hljs-title.function_` | `#2BB8A8` | Function and method names |
| `.hljs-string`, `.hljs-attr` | `#E8C547` | Strings and attributes |
| `.hljs-number`, `.hljs-literal` | `#F0883E` | Numbers and literals |
| `.hljs-comment`, `.hljs-meta` | `#828FA3` | Comments and metadata; comments are italic |
| `.hljs-variable`, `.hljs-property` | `#6FE0D2` | Variables and properties |
| `.hljs-params` | inherited | Body text color |

---

## Solid accents

The documentation theme uses flat fills throughout. Teal marks primary actions,
article headings, code blocks, section titles, and feature-card edges. Violet is
reserved for secondary actions and syntax highlighting.

| Treatment | CSS | Current use |
| --- | --- | --- |
| Primary accent | `var(--cc-teal)` | Heading underlines, code stripes, section titles, and feature-card edges |
| Primary action | `#0F766E` | Primary button; `#115E59` on hover |
| Secondary action | `#6A47C2` | Secondary button; `#7853D1` on hover |

---

## Typography

| Role | Family | Weights | Source |
|------|--------|---------|--------|
| Body / UI | Roboto | 300, 400, 500, 700, 900 | Google Fonts |
| Code | JetBrains Mono | 400, 500, 600, 700 | Google Fonts |

---

## Assets

The theme assets live in `templates/concord/assets/`.

| File | Dimensions | Current use |
| --- | --- | --- |
| `logo.png` | 863 x 277 | Landing page, navbar, mobile drawer, and footer |
| `logo_full.png` | 1536 x 1024 | Full logo artwork; the template does not reference it |
| `icon.png` | 553 x 554 | Source icon; the template does not reference it directly |
| `favicon.svg` | 553 x 554 view box | SVG favicon |
| `favicon.ico` | multiple sizes | ICO favicon |
| `favicon-96x96.png` | 96 x 96 | PNG favicon |
| `apple-touch-icon.png` | 180 x 180 | Apple touch icon |
| `web-app-manifest-192x192.png` | 192 x 192 | Web app manifest icon |
| `web-app-manifest-512x512.png` | 512 x 512 | Web app manifest icon |
| `site.webmanifest` | n/a | Browser manifest |

`Palette.png` lives at the repo root. It is an 880 x 320 RGB swatch image.

---

## Domain

concordlib.dev
