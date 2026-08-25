import { defineComponent, h } from 'vue'

export const StubIcon = defineComponent({
  props: {
    name: {
      type: String,
      default: '',
    },
    size: {
      type: Number,
      default: 16,
    },
  },
  setup(props) {
    return () => h('span', {
      'data-testid': `icon-${props.name}`,
      'data-size': String(props.size),
    }, `icon:${props.name}`)
  },
})

export const StubDatePicker = defineComponent({
  props: {
    modelValue: {
      type: String,
      default: '',
    },
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h('input', {
      type: 'date',
      value: props.modelValue ?? '',
      disabled: props.disabled,
      'data-testid': 'stub-date-picker',
      onInput: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value),
      onChange: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value),
    })
  },
})

export const StubVChart = defineComponent({
  props: {
    option: {
      type: Object,
      default: () => ({}),
    },
    initOptions: {
      type: Object,
      default: () => ({}),
    },
    autoresize: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const serialize = (value: unknown) => JSON.stringify(value, (_key, entry) => (
      typeof entry === 'function' ? '[fn]' : entry
    ))

    return () => {
      const option = props.option as {
        tooltip?: { formatter?: (params: Array<Record<string, unknown>>) => string }
        yAxis?: { axisLabel?: { formatter?: (value: number) => string } }
      }
      const tooltipFormatter = option.tooltip?.formatter
      const axisFormatter = option.yAxis?.axisLabel?.formatter
      const tooltipSamples = typeof tooltipFormatter === 'function'
        ? [
            tooltipFormatter([{ axisValueLabel: 'Jan', seriesName: 'Revenue', value: 1250, color: '#2563eb' }]),
            tooltipFormatter([{}]),
            tooltipFormatter([]),
          ]
        : []
      const axisSamples = typeof axisFormatter === 'function'
        ? [Number.NaN, 1_500_000, -1_500_000, 1_500, -1_500, 100, 12.34, 12].map(axisFormatter)
        : []

      return h('div', { 'data-testid': 'stub-vchart' }, [
      h('pre', { 'data-testid': 'stub-vchart-option' }, serialize(option)),
      h('pre', { 'data-testid': 'stub-vchart-init-options' }, serialize(props.initOptions ?? {})),
      h('span', { 'data-testid': 'stub-vchart-autoresize' }, String(props.autoresize)),
      h('pre', { 'data-testid': 'stub-vchart-tooltip-samples' }, serialize(tooltipSamples)),
      h('pre', { 'data-testid': 'stub-vchart-axis-samples' }, serialize(axisSamples)),
    ])
    }
  },
})
