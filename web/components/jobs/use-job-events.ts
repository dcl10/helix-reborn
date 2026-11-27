"use client";

import { useEffect, useRef, useState } from "react";

import { SseParser } from "@/lib/sse";

/** Where the live connection is: streaming, finished, or given up in favour of polling. */
export type JobEventsState = "connecting" | "live" | "ended" | "polling";

/** How many times to reconnect after the stream drops before falling back to polling. */
const RECONNECTS = 1;

/**
 * Follows a job's entity (a run or an evaluation) live: reads the API's server-sent events through the proxy with
 * `fetch` and a stream reader, hands each `status` event's state to `onStatus`, and stops at `end`. If the stream
 * drops before then it reconnects once; after that it gives up and returns `polling`, for the caller to poll instead.
 * @param url - the events URL, through the proxy
 * @param onStatus - applies a state, typically to the TanStack cache; awaited before the next event
 * @returns the connection's state
 */
export function useJobEvents<T>(
  url: string,
  onStatus: (state: T) => Promise<void> | void,
): JobEventsState {
  const [state, setState] = useState<JobEventsState>("connecting");
  // The latest callback, without restarting the stream when the caller passes a new function.
  const onStatusRef = useRef(onStatus);
  useEffect(() => {
    onStatusRef.current = onStatus;
  }, [onStatus]);

  useEffect(() => {
    const controller = new AbortController();

    /** Reads one connection; true once the job has ended. */
    const follow = async (): Promise<boolean> => {
      const response = await fetch(url, {
        headers: { Accept: "text/event-stream" },
        signal: controller.signal,
      });
      if (!response.ok || !response.body)
        throw new Error(`The events stream returned ${response.status}.`);

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      const parser = new SseParser();
      setState("live");
      for (;;) {
        const { done, value } = await reader.read();
        if (done) return false;
        for (const message of parser.push(
          decoder.decode(value, { stream: true }),
        )) {
          if (message.event === "status") {
            await onStatusRef.current(JSON.parse(message.data) as T);
          } else if (message.event === "end") {
            await reader.cancel();
            return true;
          }
        }
      }
    };

    void (async () => {
      for (let attempt = 0; attempt <= RECONNECTS; attempt++) {
        try {
          if (await follow()) {
            setState("ended");
            return;
          }
        } catch {
          if (controller.signal.aborted) return;
        }
        if (controller.signal.aborted) return;
      }
      setState("polling");
    })();

    return () => controller.abort();
  }, [url]);

  return state;
}
