import { redirect } from "next/navigation";
import { Suspense } from "react";

import { experimentPath } from "@/lib/experiments";

/**
 * An experiment's own path redirects to its Data tab. A later issue (#38) replaces this with an
 * overview.
 * @param params - the route params, including the experiment ID
 * @returns nothing; redirects before rendering
 */
export default function ExperimentPage({
  params,
}: PageProps<"/experiments/[id]">) {
  return (
    <Suspense>
      <RedirectToData params={params} />
    </Suspense>
  );
}

async function RedirectToData({
  params,
}: Pick<PageProps<"/experiments/[id]">, "params">): Promise<never> {
  const { id } = await params;
  redirect(experimentPath(id, "data"));
}
