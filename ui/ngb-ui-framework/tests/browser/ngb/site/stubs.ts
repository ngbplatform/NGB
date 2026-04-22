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

    return () => h('div', { 'data-testid': 'stub-vchart' }, [
      h('pre', { 'data-testid': 'stub-vchart-option' }, serialize(props.option ?? {})),
      h('pre', { 'data-testid': 'stub-vchart-init-options' }, serialize(props.initOptions ?? {})),
      h('span', { 'data-testid': 'stub-vchart-autoresize' }, String(props.autoresize)),
    ])
  },
})
