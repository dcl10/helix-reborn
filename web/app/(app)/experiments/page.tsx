import type { Metadata } from "next";

export const metadata: Metadata = { title: "Experiments · Helix" };

export default function ExperimentsPage() {
  return (
    <div>
      <h1 className="text-2xl font-semibold tracking-tight">Experiments</h1>
      <p className="text-muted-foreground">Coming soon.</p>
    </div>
  );
}
