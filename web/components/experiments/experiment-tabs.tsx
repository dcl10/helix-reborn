"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

import { experimentPath, type ExperimentTab } from "@/lib/experiments";
import { cn } from "@/lib/utils";

/** One tab in the experiment layout's navigation. */
interface TabItem {
  tab: ExperimentTab;
  label: string;
}

/** Every tab in the experiment layout, in display order. */
export const experimentTabs: TabItem[] = [
  { tab: "data", label: "Data" },
  { tab: "eda", label: "EDA" },
  { tab: "pipelines", label: "Pipelines" },
  { tab: "runs", label: "Runs" },
  { tab: "evaluations", label: "Evaluations" },
  { tab: "predict", label: "Predict" },
  { tab: "files", label: "Files" },
];

/**
 * The experiment's tab navigation, with the current tab highlighted. Reads the URL, which on this
 * dynamic route is only known per request, so it suspends there: render it inside a `<Suspense>`
 * boundary whose fallback is {@link ExperimentTabLinks} with no `pathname`.
 * @param experimentId - the experiment ID the tabs link within
 * @returns the tab navigation
 */
export function ExperimentTabs({ experimentId }: { experimentId: string }) {
  return (
    <ExperimentTabLinks experimentId={experimentId} pathname={usePathname()} />
  );
}

/**
 * The experiment's tab navigation, highlighting the tab `pathname` is in. Doesn't read the URL itself.
 * @param experimentId - the experiment ID the tabs link within
 * @param pathname - the current path; with none, no tab is highlighted
 * @returns the tab navigation
 */
export function ExperimentTabLinks({
  experimentId,
  pathname,
}: {
  experimentId: string;
  pathname?: string;
}) {
  return (
    <nav aria-label="Experiment" className="flex items-center gap-1 border-b">
      {experimentTabs.map(({ tab, label }) => {
        const href = experimentPath(experimentId, tab);
        const active = pathname !== undefined && pathname.startsWith(href);

        return (
          <Link
            key={tab}
            href={href}
            aria-current={active ? "page" : undefined}
            className={cn(
              "border-b-2 border-transparent px-3 py-2 text-sm font-medium text-muted-foreground transition-colors hover:text-foreground",
              active && "border-foreground text-foreground",
            )}
          >
            {label}
          </Link>
        );
      })}
    </nav>
  );
}
