import { boolean, object, text } from './playerRecords';

export function readAuthoringAvailability(value: unknown, path = 'Authoring.availability') {
  const { available } = object({ available: boolean })(value, path);
  if (available) return { available: true as const };
  return { available: false as const, ...object({ reason: text })(value, path) };
}

export type AuthoringAvailability = ReturnType<typeof readAuthoringAvailability>;
