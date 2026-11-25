import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { DatasetUpload } from "@/components/experiments/dataset-upload";
import { Providers } from "@/components/providers";

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function renderUpload() {
  return render(
    <Providers>
      <DatasetUpload experimentId="exp-1" />
    </Providers>,
  );
}

function fileInput() {
  return document.querySelector('input[type="file"]') as HTMLInputElement;
}

describe("DatasetUpload", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("rejects a non-CSV file", async () => {
    renderUpload();

    const file = new File(["x"], "data.txt", { type: "text/plain" });
    fireEvent.change(fileInput(), { target: { files: [file] } });

    expect(await screen.findByText("Choose a CSV file.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Upload" })).toBeDisabled();
  });

  it("rejects a CSV file over 50 MB", async () => {
    const user = userEvent.setup();
    renderUpload();

    const file = new File(["x"], "data.csv", { type: "text/csv" });
    Object.defineProperty(file, "size", { value: 51 * 1024 * 1024 });
    await user.upload(fileInput(), file);

    expect(
      await screen.findByText(/larger than the 50 MB limit/),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Upload" })).toBeDisabled();
  });

  it("uploads a valid CSV file", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      json({
        id: "ds-1",
        fileName: "data.csv",
        sizeBytes: 10,
        rowCount: 1,
        createdAt: "2026-11-01T10:00:00Z",
        columns: [],
      }),
    );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderUpload();

    const file = new File(["a,b\n1,2"], "data.csv", { type: "text/csv" });
    await user.upload(fileInput(), file);
    await user.click(screen.getByRole("button", { name: "Upload" }));

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/proxy/experiments/exp-1/dataset");
    expect(init.method).toBe("POST");
    expect(init.body).toBeInstanceOf(FormData);
  });

  it("shows the API's error message on a failed upload", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(
        json({ detail: "This experiment already has a dataset." }, 409),
      );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderUpload();

    const file = new File(["a,b\n1,2"], "data.csv", { type: "text/csv" });
    await user.upload(fileInput(), file);
    await user.click(screen.getByRole("button", { name: "Upload" }));

    expect(
      await screen.findByText("This experiment already has a dataset."),
    ).toBeInTheDocument();
  });
});
