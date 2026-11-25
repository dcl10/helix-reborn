import { afterEach, describe, expect, it, vi } from "vitest";

import {
  ApiError,
  createExperiment,
  deleteExperiment,
  experimentPath,
  getDataset,
  getExperiment,
  listExperiments,
  updateExperiment,
  uploadDataset,
} from "@/lib/experiments";

function jsonResponse(body: unknown, status: number) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const experiment = {
  id: "exp-1",
  name: "My experiment",
  description: null,
  ownerSub: "user-1",
  createdAt: "2026-11-01T10:00:00Z",
  updatedAt: "2026-11-01T10:00:00Z",
  dataset: null,
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("experimentPath", () => {
  it("builds the experiment's own path with no tab", () => {
    expect(experimentPath("exp-1")).toBe("/experiments/exp-1");
  });

  it("builds a tab's path", () => {
    expect(experimentPath("exp-1", "data")).toBe("/experiments/exp-1/data");
  });
});

describe("listExperiments", () => {
  it("gets the experiments list through the proxy", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(jsonResponse([experiment], 200));
    vi.stubGlobal("fetch", fetchMock);

    await listExperiments();

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/experiments",
      expect.objectContaining({ headers: expect.anything() }),
    );
  });
});

describe("getExperiment", () => {
  it("gets one experiment by id", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(experiment, 200));
    vi.stubGlobal("fetch", fetchMock);

    await getExperiment("exp-1");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/experiments/exp-1",
      expect.anything(),
    );
  });
});

describe("createExperiment", () => {
  it("posts the name and description", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(experiment, 201));
    vi.stubGlobal("fetch", fetchMock);

    await createExperiment({ name: "My experiment", description: "Notes" });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/experiments",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ name: "My experiment", description: "Notes" }),
      }),
    );
  });

  it("sends null for an empty description", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(experiment, 201));
    vi.stubGlobal("fetch", fetchMock);

    await createExperiment({ name: "My experiment", description: "" });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/experiments",
      expect.objectContaining({
        body: JSON.stringify({ name: "My experiment", description: null }),
      }),
    );
  });

  it("throws an ApiError with the API's message when it fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse({ detail: "Nope." }, 400)),
    );

    await expect(
      createExperiment({ name: "My experiment", description: "" }),
    ).rejects.toEqual(new ApiError("Nope.", 400));
  });
});

describe("updateExperiment", () => {
  it("patches the experiment by id", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(experiment, 200));
    vi.stubGlobal("fetch", fetchMock);

    await updateExperiment({
      id: "exp-1",
      name: "Renamed",
      description: "Notes",
    });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/experiments/exp-1",
      expect.objectContaining({
        method: "PATCH",
        body: JSON.stringify({ name: "Renamed", description: "Notes" }),
      }),
    );
  });
});

describe("deleteExperiment", () => {
  it("deletes the experiment by id", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await deleteExperiment("exp-1");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/experiments/exp-1",
      expect.objectContaining({ method: "DELETE" }),
    );
  });
});

describe("getDataset", () => {
  it("gets the experiment's dataset", async () => {
    const dataset = {
      id: "ds-1",
      fileName: "data.csv",
      sizeBytes: 100,
      rowCount: 10,
      createdAt: "2026-11-01T10:00:00Z",
      columns: [],
    };
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(dataset, 200));
    vi.stubGlobal("fetch", fetchMock);

    await getDataset("exp-1");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/experiments/exp-1/dataset",
      expect.anything(),
    );
  });

  it("throws a 404 ApiError when there is no dataset", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(null, { status: 404 })),
    );

    await expect(getDataset("exp-1")).rejects.toMatchObject({ status: 404 });
  });
});

describe("uploadDataset", () => {
  it("sends the file as FormData to the dataset endpoint", async () => {
    const dataset = {
      id: "ds-1",
      fileName: "data.csv",
      sizeBytes: 3,
      rowCount: 1,
      createdAt: "2026-11-01T10:00:00Z",
      columns: [],
    };
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(dataset, 201));
    vi.stubGlobal("fetch", fetchMock);
    const file = new File(["a,b\n1,2"], "data.csv", { type: "text/csv" });

    await uploadDataset("exp-1", file);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/experiments/exp-1/dataset",
      expect.objectContaining({ method: "POST" }),
    );
    const [, init] = fetchMock.mock.calls[0];
    expect(init.body).toBeInstanceOf(FormData);
    expect((init.body as FormData).get("file")).toBe(file);
  });

  it("surfaces the API's problem detail on a 409 conflict", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          jsonResponse({ detail: "Experiment already has a dataset." }, 409),
        ),
    );
    const file = new File(["a"], "data.csv", { type: "text/csv" });

    await expect(uploadDataset("exp-1", file)).rejects.toEqual(
      new ApiError("Experiment already has a dataset.", 409),
    );
  });

  it("surfaces the first validation error on a 400", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse(
          {
            title: "One or more validation errors occurred.",
            errors: { file: ["The file isn't a valid CSV."] },
          },
          400,
        ),
      ),
    );
    const file = new File(["a"], "data.csv", { type: "text/csv" });

    await expect(uploadDataset("exp-1", file)).rejects.toEqual(
      new ApiError(
        "The file isn't a valid CSV.",
        400,
        "One or more validation errors occurred.",
      ),
    );
  });
});
