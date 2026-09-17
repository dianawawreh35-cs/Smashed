/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      fontFamily: {
        // Cairo carries Arabic and Latin in one family, so a screen mixing a
        // customer name with a phone number does not change typeface mid-line.
        sans: ['Cairo', 'Tajawal', 'Segoe UI', 'system-ui', 'sans-serif'],
        // Phone numbers, extensions and durations: tabular figures keep columns
        // aligned and stop digits jumping as a call timer counts.
        mono: ['ui-monospace', 'SFMono-Regular', 'Menlo', 'Consolas', 'monospace'],
      },
      colors: {
        // The restaurant's orange. Used for primary actions and almost nothing
        // else - an interface that is orange everywhere makes nothing stand out.
        brand: {
          50: '#fff7ed',
          100: '#ffedd5',
          200: '#fed7aa',
          300: '#fdba74',
          400: '#fb923c',
          500: '#f97316',
          600: '#ea580c',
          700: '#c2410c',
          800: '#9a3412',
          900: '#7c2d12',
        },
      },
      boxShadow: {
        // One soft elevation for cards, one for anything that floats above them.
        card: '0 1px 2px 0 rgb(15 23 42 / 0.04), 0 1px 3px 0 rgb(15 23 42 / 0.06)',
        raised: '0 4px 6px -1px rgb(15 23 42 / 0.08), 0 2px 4px -2px rgb(15 23 42 / 0.06)',
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
