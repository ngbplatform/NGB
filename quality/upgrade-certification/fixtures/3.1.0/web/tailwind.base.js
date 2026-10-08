export const ngbTailwindBaseConfig = {
  // Dark theme is toggled by adding/removing the `dark` class on <html>.
  darkMode: 'class',
  theme: {
    extend: {
      // NGB design tokens are defined as CSS variables in ngb-ui-framework/src/styles/tailwind.css.
      // We map them to Tailwind color utilities so @apply can use classes like bg-ngb-bg.
      colors: {
        // Semantic primary token used by interactive controls. It stays aliasable
        // independently from legacy blue naming and supports Tailwind alpha modifiers.
        'ngb-primary': 'rgb(var(--ngb-primary-rgb) / <alpha-value>)',
        'ngb-primary-hover': 'rgb(var(--ngb-primary-hover-rgb) / <alpha-value>)',

        'ngb-blue': 'var(--ngb-blue)',
        'ngb-blue-dark': 'var(--ngb-blue-dark)',
        'ngb-blue-hover': 'var(--ngb-blue-hover)',

        'ngb-text': 'var(--ngb-text)',
        'ngb-muted': 'var(--ngb-muted)',
        'ngb-border': 'var(--ngb-border)',
        'ngb-bg': 'var(--ngb-bg)',
        'ngb-card': 'var(--ngb-card)',

        'ngb-danger': 'var(--ngb-danger)',
        'ngb-warn': 'var(--ngb-warn)',
        'ngb-success': 'var(--ngb-success)',
        'ngb-neutral-subtle': 'var(--ngb-neutral-subtle)',
        'ngb-success-subtle': 'var(--ngb-success-subtle)',
        'ngb-warn-subtle': 'var(--ngb-warn-subtle)',
        'ngb-danger-subtle': 'var(--ngb-danger-subtle)',
        'ngb-success-border': 'var(--ngb-success-border)',
        'ngb-warn-border': 'var(--ngb-warn-border)',
        'ngb-danger-border': 'var(--ngb-danger-border)',

        'ngb-accent-1': 'var(--ngb-accent-1)',
        'ngb-accent-2': 'var(--ngb-accent-2)',
        'ngb-accent-3': 'var(--ngb-accent-3)',
        'ngb-accent-4': 'var(--ngb-accent-4)',
      },

      boxShadow: {
        // used by .ngb-card utility in ngb-ui-framework styles
        card: 'var(--ngb-shadow-1)',
      },
    },
  },
  plugins: [],
}
