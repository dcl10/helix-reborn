import { Suspense } from "react";

import { ExperimentHeader } from "@/components/experiments/experiment-header";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * The layout for one experiment: its header (name, description, edit and delete) and tab navigation,
 * above the active tab's content. Awaits `params` inside a `<Suspense>` boundary so the rest of the
 * shell can prerender.
 * @param children - the active tab's page
 * @param params - the route params, including the experiment ID
 * @returns the experiment layout
 */
export default function ExperimentLayout({
  children,
  params,
}: LayoutProps<"/experiments/[id]">) {
  return (
    <div className="flex flex-col gap-6">
      <Suspense fallback={<Skeleton className="h-24 w-full" />}>
        <ExperimentHeaderForParams params={params} />
      </Suspense>
      {children}
    </div>
  );
}

async function ExperimentHeaderForParams({
  params,
}: Pick<LayoutProps<"/experiments/[id]">, "params">) {
  const { id } = await params;
  return <ExperimentHeader experimentId={id} />;
}
