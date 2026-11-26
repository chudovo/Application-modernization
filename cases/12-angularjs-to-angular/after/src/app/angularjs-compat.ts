// What AngularJS did inside its templates, and what an Angular application has to do in its own code.
// Angular has no "filter" and no "orderBy" pipe, on purpose. The behaviour users are used to still has to be kept.

/**
 * The AngularJS "filter" filter with a text: an item matches if any of its values contains the text, ignoring
 * letter case. Not only the column a developer happens to think of.
 */
export function matchesAnyField(item: object, text: string): boolean {
  const term = text.toLowerCase();
  return Object.values(item).some((value) => value !== null && value !== undefined && String(value).toLowerCase().includes(term));
}

/**
 * The comparison the AngularJS "orderBy" filter used: strings are compared lower-cased, character by character
 * (so "bernd" comes before "Zoë", and "É" after "Z"), and empty values come last.
 */
export function compareLikeOrderBy(a: unknown, b: unknown): number {
  const missingA = a === null || a === undefined;
  const missingB = b === null || b === undefined;
  if (missingA || missingB) return missingA === missingB ? 0 : missingA ? 1 : -1;

  const x = typeof a === 'string' ? a.toLowerCase() : a;
  const y = typeof b === 'string' ? b.toLowerCase() : b;
  return x === y ? 0 : (x as string | number) < (y as string | number) ? -1 : 1;
}

/** The "medium" date format of AngularJS. Angular's own "medium" has a comma between date and time. */
export const MEDIUM_AS_ANGULARJS = 'MMM d, y h:mm:ss a';
