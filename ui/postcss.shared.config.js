export function createNgbPostcssConfig(tailwindConfigPath) {
  return {
    plugins: {
      tailwindcss: {
        config: tailwindConfigPath,
      },
      autoprefixer: {},
    },
  }
}
