import { useAtomValue } from "jotai";
import { isDemoAtom } from "../network/backend";
import { useEffect } from "react";

export function useSetTitle(title: string): void {
  const demo = useAtomValue(isDemoAtom);
  useEffect(() => {
    if (!demo) {
      document.title = title + " - FMS Insight";
    }
  }, [demo, title]);
}
