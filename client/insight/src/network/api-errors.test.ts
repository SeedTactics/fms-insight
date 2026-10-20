import { expect, test } from "vitest";
import { ApiException } from "./api.js";
import { apiErrorMessage } from "./api-errors.js";

function failure(contentType: string | null, response: string): ApiException {
  return new ApiException(
    "An unexpected server error occurred.",
    409,
    response,
    contentType === null ? {} : { "content-type": contentType },
    null,
  );
}

test.each([
  [
    "application/problem+json",
    JSON.stringify({ title: "Conflict", status: 409, detail: "Material is not eligible." }),
    "Material is not eligible.",
  ],
  [
    "application/problem+json; charset=utf-8",
    JSON.stringify({ title: "One or more validation errors occurred.", status: 400 }),
    "One or more validation errors occurred.",
  ],
  [
    "text/plain; charset=utf-8",
    "Material must be waiting in a queue.",
    "Material must be waiting in a queue.",
  ],
  [null, "Material must be waiting in a queue.", "Material must be waiting in a queue."],
])("reads the server explanation from %s", (contentType, response, expected) => {
  expect(apiErrorMessage(failure(contentType, response))).toBe(expected);
});

test.each([
  ["text/html", "<html>Service unavailable</html>"],
  ["application/problem+json", JSON.stringify({ detail: 123 })],
  ["application/problem+json", "not json"],
  ["text/plain", "   "],
])("uses the request error for an unsupported %s response", (contentType, response) => {
  expect(apiErrorMessage(failure(contentType, response))).toBe(
    "An unexpected server error occurred.",
  );
});

test("describes errors which are not server responses", () => {
  expect(apiErrorMessage(new Error("Network failure"))).toBe("Network failure");
  expect(apiErrorMessage("Rejected")).toBe("Rejected");
  expect(apiErrorMessage(undefined, "Error loading log history")).toBe("Error loading log history");
});
