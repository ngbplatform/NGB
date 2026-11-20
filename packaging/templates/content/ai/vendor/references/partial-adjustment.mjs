// Reference oracle for positive credit magnitudes and a frozen, half-up tax policy.
// Amounts use the currency's minor unit. The rate is a fraction, e.g. 75n / 1000n.
export function creditTax({ lineBase, lineTax, priorBase, priorTax, creditBase, numerator, denominator }) {
  const amounts = [lineBase, lineTax, priorBase, priorTax, creditBase, numerator, denominator]
  if (!amounts.every(value => typeof value === 'bigint' && value >= 0n)) {
    throw new RangeError('Use nonnegative integer amounts and rate components.')
  }
  const newBase = priorBase + creditBase
  if (!lineBase || !creditBase || !denominator || newBase > lineBase || priorTax > lineTax) {
    throw new RangeError('Invalid credit amount, rate or remaining line balance.')
  }
  if (newBase === lineBase) return lineTax - priorTax

  const roundedTax = (2n * newBase * numerator + denominator) / (2n * denominator)
  const targetTax = roundedTax < lineTax ? roundedTax : lineTax
  if (targetTax < priorTax) throw new RangeError('Earlier allocations exceed the cumulative tax.')
  return targetTax - priorTax
}
