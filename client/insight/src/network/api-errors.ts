/* Copyright (c) 2026, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

import { ApiException } from "./api.js";

function mediaType(headers: Readonly<Record<string, unknown>>): string {
  const value = headers["content-type"];
  return typeof value === "string" ? (value.split(";")[0] ?? "").trim().toLowerCase() : "";
}

function nonBlank(value: unknown): string | null {
  return typeof value === "string" && value.trim() !== "" ? value : null;
}

function problemMessage(response: string): string | null {
  try {
    const problem: unknown = JSON.parse(response);
    if (problem === null || typeof problem !== "object") return null;
    return (
      ("detail" in problem ? nonBlank(problem.detail) : null) ??
      ("title" in problem ? nonBlank(problem.title) : null)
    );
  } catch {
    return null;
  }
}

/**
 * The server's explanation for a failed request: the detail of a JSON problem or a plain-text
 * message (also assumed without a content type). Null when the server gave none, including HTML
 * from a proxy.
 */
export function serverErrorMessage(error: unknown): string | null {
  if (error === null || typeof error !== "object" || !ApiException.isApiException(error)) {
    return null;
  }
  const type = mediaType(error.headers);
  return type === "application/problem+json" || type === "application/json"
    ? problemMessage(error.response)
    : type === "text/plain" || type === ""
      ? nonBlank(error.response)
      : null;
}

/** The server's explanation for a failed request, otherwise the request error's own message. */
export function apiErrorMessage(error: unknown, fallback = "Error"): string {
  const message = serverErrorMessage(error);
  if (message !== null) return message;
  if (error instanceof Error) return error.message;
  return nonBlank(error) ?? fallback;
}
