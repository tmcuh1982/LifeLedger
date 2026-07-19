import type { Config } from 'tailwindcss'

export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        ink: 'rgb(var(--color-ink) / <alpha-value>)',
        inkDeep: 'rgb(var(--color-ink-deep) / <alpha-value>)',
        panel: 'rgb(var(--color-panel) / <alpha-value>)',
        panelHigh: 'rgb(var(--color-panel-high) / <alpha-value>)',
        mist: 'rgb(var(--color-mist) / <alpha-value>)',
        muted: 'rgb(var(--color-muted) / <alpha-value>)',
        sky: 'rgb(var(--color-sky) / <alpha-value>)',
        ocean: 'rgb(var(--color-ocean) / <alpha-value>)',
        success: 'rgb(var(--color-success) / <alpha-value>)',
        warning: 'rgb(var(--color-warning) / <alpha-value>)',
        danger: 'rgb(var(--color-danger) / <alpha-value>)',
      },
      fontFamily: { sans: ['Inter', 'ui-sans-serif', 'system-ui', 'sans-serif'] },
      boxShadow: { glass: '0 8px 32px 0 rgba(0, 0, 0, 0.18)' },
    },
  },
  plugins: [],
} satisfies Config
