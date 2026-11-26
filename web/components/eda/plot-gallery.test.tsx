import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { PlotGallery } from "@/components/eda/plot-gallery";
import { Providers } from "@/components/providers";
import type { Artifact } from "@/lib/eda";

function artifact(overrides: Partial<Artifact>): Artifact {
  return {
    id: "a-1",
    kind: "edaPlot",
    title: "PCA",
    contentType: "image/png",
    sizeBytes: 100,
    sourceType: "eda",
    sourceId: null,
    createdAt: "2026-11-26T10:00:00Z",
    fileName: "a-1.png",
    ...overrides,
  };
}

function stubArtifacts(artifacts: Artifact[]) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue(
      new Response(JSON.stringify(artifacts), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    ),
  );
}

function renderGallery() {
  return render(
    <Providers>
      <PlotGallery experimentId="exp-1" />
    </Providers>,
  );
}

describe("PlotGallery", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders each plot's image and download link, newest first", async () => {
    stubArtifacts([
      artifact({
        id: "old",
        title: "Older plot",
        createdAt: "2026-11-25T10:00:00Z",
      }),
      artifact({
        id: "new",
        title: "Newer plot",
        createdAt: "2026-11-26T10:00:00Z",
      }),
    ]);
    renderGallery();

    const images = await screen.findAllByRole("img");
    expect(images.map((image) => image.getAttribute("alt"))).toEqual([
      "Newer plot",
      "Older plot",
    ]);
    expect(images[0]).toHaveAttribute(
      "src",
      "/api/proxy/experiments/exp-1/artifacts/new/content",
    );
    expect(images[0]).toHaveAttribute("loading", "lazy");
    expect(
      screen.getByRole("link", { name: "Download Newer plot" }),
    ).toHaveAttribute(
      "href",
      "/api/proxy/experiments/exp-1/artifacts/new/content?download=true",
    );
  });

  it("leaves out EDA tables", async () => {
    stubArtifacts([
      artifact({ id: "plot", title: "A plot" }),
      artifact({
        id: "table",
        kind: "edaTable",
        title: "Normality tests",
        contentType: "text/csv",
      }),
    ]);
    renderGallery();

    expect(
      await screen.findByRole("img", { name: "A plot" }),
    ).toBeInTheDocument();
    expect(screen.queryByText("Normality tests")).not.toBeInTheDocument();
  });

  it("says when there are no plots yet", async () => {
    stubArtifacts([]);
    renderGallery();

    expect(await screen.findByText(/No plots yet/)).toBeInTheDocument();
  });
});
