import type { Config } from 'tailwindcss'

export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        ink: '#0b1326',
        inkDeep: '#060e20',
        panel: '#171f33',
        panelHigh: '#222a3d',
        mist: '#dae2fd',
        muted: '#c4c7c8',
        sky: '#adc9eb',
        ocean: '#304b68',
        success: '#b9f6c8',
        warning: '#ffddb0',
        danger: '#ffb4ab',
      },
      fontFamily: { sans: ['Inter', 'ui-sans-serif', 'system-ui', 'sans-serif'] },
      boxShadow: { glass: '0 8px 32px 0 rgba(0, 0, 0, 0.18)' },
    },
  },
  plugins: [],
} satisfies Config
