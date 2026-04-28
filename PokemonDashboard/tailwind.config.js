/** @type {import('tailwindcss').Config} */
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
        'card-bg': '#1a1a2e',
        'card-border': '#2d2d4e',
        'card-header': '#16213e',
        'accent-blue': '#0f3460',
        'accent-purple': '#533483',
      },
      fontFamily: {
        'pokemon': ['Segoe UI', 'system-ui', 'sans-serif'],
      }
    },
  },
  plugins: [],
}
