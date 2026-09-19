/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      fontFamily: {
        // Cairo carries Arabic and Latin in one family, so a screen mixing a
        // customer name with a phone number does not change typeface mid-line.
        sans: ['Cairo', 'Tajawal', 'Segoe UI', 'system-ui', 'sans-serif'],
        mono: ['Cascadia Mono', 'ui-monospace', 'Consolas', 'monospace'],
      },
      colors: {
        // The same palette as the Agent App, so the two halves of the system
        // look like one product. Dark suits a screen that is open all shift,
        // and lets the single accent carry meaning rather than compete with a
        // bright background.
        ink: {
          950: '#0F1115', // canvas
          900: '#171A21', // surface
          800: '#1E222B', // raised surface
          700: '#2A2F3A', // border
        },
        // The brand colour. #4F8CFF began as the blue from the CallPoc proof of
        // concept and was carried here as a placeholder; on 2026-09-20 the
        // client chose to keep it, so it is now the restaurant's colour in this
        // system and not a stand-in. The Agent App's Accent is the same value —
        // change both together or the two apps stop matching.
        brand: {
          50: '#25355A',
          100: '#25355A',
          200: '#345079',
          300: '#3F6BAE',
          400: '#4F8CFF',
          500: '#4F8CFF',
          600: '#4F8CFF',
          700: '#6BA0FF',
          800: '#8AB4FF',
          900: '#B9D0FF',
        },
      },
      boxShadow: {
        card: '0 1px 2px 0 rgb(0 0 0 / 0.30)',
        raised: '0 8px 24px -8px rgb(0 0 0 / 0.55)',
      },
      keyframes: {
        'fade-in': {
          from: { opacity: '0', transform: 'translateY(-2px)' },
          to: { opacity: '1', transform: 'none' },
        },
      },
      animation: {
        'fade-in': 'fade-in 120ms ease-out',
      },
    },
  },
  plugins: [],
}
