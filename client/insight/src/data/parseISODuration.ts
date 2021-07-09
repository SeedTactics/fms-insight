// https://github.com/date-fns/date-fns/pull/1947
import { Duration } from "date-fns";

const nr = "-?\\d+(?:[\\.,]\\d+)?";
const dateRegex = "(" + nr + "Y)?(" + nr + "M)?(" + nr + "D)?";
const timeRegex = "T(" + nr + "H)?(" + nr + "M)?(" + nr + "S)?";
const durationRegex = new RegExp("P" + dateRegex + "(?:" + timeRegex + ")?");

export default function parseISODuration(argument: string): Duration | null {
  const match = durationRegex.exec(argument);
  if (!match) {
    return null;
  }

  // at least one part must be specified
  if (!match[1] && !match[2] && !match[3] && !match[4] && !match[5] && !match[6]) {
    return null;
  }

  const duration: Duration = {};
  if (match[1]) duration.years = parseFloat(match[1]);
  if (match[2]) duration.months = parseFloat(match[2]);
  if (match[3]) duration.days = parseFloat(match[3]);
  if (match[4]) duration.hours = parseFloat(match[4]);
  if (match[5]) duration.minutes = parseFloat(match[5]);
  if (match[6]) duration.seconds = parseFloat(match[6]);
  return duration;
}

export function durationToSeconds(duration: string): number {
  const dur = parseISODuration(duration);
  if (!dur) return 0;
  const days = (dur.years ?? 0) * 365 + (dur.months ?? 0) * 30 + (dur.days ?? 0);
  const hours = days * 24 + (dur.hours ?? 0);
  const minutes = hours * 60 + (dur.minutes ?? 0);
  return minutes * 60 + (dur.seconds ?? 0);
}

export function durationToMinutes(duration: string): number {
  return durationToSeconds(duration) / 60;
}
