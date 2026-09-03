import { readFileSync, writeFileSync } from "node:fs";

const apiPath = new URL("../src/network/api.ts", import.meta.url);
const api = readFileSync(apiPath, "utf8");
const functionStart = api.indexOf("function formatDate(d: Date) {");
const functionEnd = api.indexOf("\n}", functionStart) + 2;
if (functionStart < 0 || functionEnd < 2) {
  throw new Error("NSwag output does not contain the expected formatDate function.");
}

const originalFormatDate = api.slice(functionStart, functionEnd);
const utcFormatDate = [
  [".getFullYear()", ".getUTCFullYear()"],
  [".getMonth()", ".getUTCMonth()"],
  [".getDate()", ".getUTCDate()"],
].reduce((output, [localGetter, utcGetter]) => {
  if (!output.includes(localGetter) && !output.includes(utcGetter)) {
    throw new Error(`NSwag formatDate no longer contains ${localGetter}.`);
  }
  return output.replaceAll(localGetter, utcGetter);
}, originalFormatDate);

if (utcFormatDate !== originalFormatDate) {
  writeFileSync(
    apiPath,
    api.slice(0, functionStart) + utcFormatDate + api.slice(functionEnd),
  );
}
