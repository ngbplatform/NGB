import { fileURLToPath } from 'node:url'

import { createNgbPostcssConfig } from '../postcss.shared.config.js'

export default createNgbPostcssConfig(fileURLToPath(new URL('./tailwind.config.js', import.meta.url)))
