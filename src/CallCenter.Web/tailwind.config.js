/** A colour that light and dark set differently (S-69): see index.css. */
const themed = (name) => `rgb(var(--${name}) / <alpha-value>)`

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
      // Every shade below is a CSS variable (index.css), set once for dark and
      // once for light, so the whole app follows `data-theme` on the page
      // (S-69). The values and the reasons for them are in index.css. The
      // <alpha-value> keeps opacity classes such as bg-ink-800/60 working.
      colors: {
        // The same palette as the Agent App, so the two halves of the system
        // look like one product.
        ink: {
          950: themed('ink-950'), // canvas
          900: themed('ink-900'), // surface
          800: themed('ink-800'), // raised surface
          700: themed('ink-700'), // border
        },
        // The brand colour, #4F8CFF in dark. It began as the blue from the
        // CallPoc proof of concept; on 2026-09-20 the client chose to keep it.
        // The Agent App's Accent is the same value - change both together or
        // the two apps stop matching.
        brand: {
          50: themed('brand-50'),
          100: themed('brand-100'),
          200: themed('brand-200'),
          300: themed('brand-300'),
          400: themed('brand-400'),
          500: themed('brand-500'),
          600: themed('brand-600'),
          700: themed('brand-700'),
          800: themed('brand-800'),
          900: themed('brand-900'),
          // A filled button's blue (M-W08): white on it is at least 4.5:1.
          // Only .btn-primary uses it.
          button: themed('brand-button'),
        },
        // The text greys. In dark they are Tailwind's own, but for slate-500,
        // which is lifted to meet AA on every surface (M-W08); in light they
        // turn dark, so the same class still means the same emphasis.
        slate: {
          100: themed('slate-100'),
          200: themed('slate-200'),
          300: themed('slate-300'),
          400: themed('slate-400'),
          500: themed('slate-500'),
        },
        // The pale reds, ambers and greens that are text on a dark tint
        // (notices, badges). Tailwind's own in dark; deep in light, where a
        // pale one would vanish. 500 and 600, the fills, are left alone.
        red: {
          200: themed('red-200'),
          300: themed('red-300'),
          400: themed('red-400'),
        },
        amber: {
          100: themed('amber-100'),
          200: themed('amber-200'),
          300: themed('amber-300'),
          400: themed('amber-400'),
        },
        emerald: {
          300: themed('emerald-300'),
          400: themed('emerald-400'),
        },
      },
      boxShadow: {
        card: 'var(--shadow-card)',
        raised: 'var(--shadow-raised)',
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
