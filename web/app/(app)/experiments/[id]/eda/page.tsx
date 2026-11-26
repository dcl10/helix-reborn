import { Suspense } from "react";

import { EdaTab } from "@/components/eda/eda-tab";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * The EDA tab: dataset summary, plot builder, normality tests and plot gallery.
 * @param params - the route params, including the experiment ID
 * @returns the tab content
 */
export default function EdaPage({
  params,
}: PageProps<"/experiments/[id]/eda">) {
  return (
    <Suspense fallback={<Skeleton className="h-48 w-full" />}>
      <EdaTabForParams params={params} />
    </Suspense>
  );
}

async function EdaTabForParams({
  params,
}: Pick<PageProps<"/experiments/[id]/eda">, "params">) {
  const { id } = await params;
  return <EdaTab experimentId={id} />;
}
