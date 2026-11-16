/**
 * Reads a number as the user typed it on their device: "2,5" on a German phone, "2.5" on a US one.
 * .NET does this with decimal.TryParse(text, culture). JavaScript's Number("2,5") is NaN on every device,
 * so the decimal separator has to be taken from the locale explicitly.
 */
export function parseLocalDecimal(text: string | undefined, locale: string): number | null {
  if (!text) return null;
  const parts = new Intl.NumberFormat(locale).formatToParts(12345.6);
  const decimal = parts.find((p) => p.type === 'decimal')?.value ?? '.';
  const group = parts.find((p) => p.type === 'group')?.value ?? '';

  let normalized = text.trim();
  if (group) normalized = normalized.split(group).join('');
  normalized = normalized.replace(decimal, '.');

  if (!/^[+-]?\d+(\.\d+)?$/.test(normalized)) return null;
  return Number(normalized);
}
