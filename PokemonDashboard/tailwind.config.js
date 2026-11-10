/** @type {import('tailwindcss').Config} */
// Theme colours are CSS variables holding RGB triplets, set per theme in assets/themes.css. The
// utilities the components already use resolve through them: slate-* here is the theme's neutral
// ramp, not Tailwind's slate, and indigo-500 to 700 are the theme's accent.
const v = (name) => `rgb(var(--${name}) / <alpha-value>)`
const ramp = (prefix, shades) => Object.fromEntries(shades.map((s) => [s, v(`${prefix}-${s}`)]))

module.exports = {
  content: [
    './src/renderer/index.html',
    './src/renderer/src/**/*.{js,ts,jsx,tsx}'
  ],
  theme: {
    extend: {
      colors: {
        'shiny-gold': '#FFD700',
        'shiny-gold-light': '#FFF176',
        'card-bg': v('card-bg'),
        'card-border': v('card-border'),
        'card-header': v('card-header'),
        'accent-blue': '#0f3460',
        'accent-purple': '#533483',
        white: v('ink'),
        slate: ramp('n', [100, 200, 300, 400, 500, 600, 700, 800, 900]),
        indigo: ramp('accent', [500, 600, 700]),
      },
      // Fills named card-border are the row panels; borders keep the border colour.
      backgroundColor: {
        'card-border': v('panel'),
      },
      fontFamily: {
        'pokemon': ['Segoe UI', 'system-ui', 'sans-serif'],
      }
    },
  },
  plugins: [],
}
