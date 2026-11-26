import { describe, expect, it } from "vitest";

import {
  defaultParams,
  describeFields,
  type JsonSchema,
  paramsToForm,
  paramsZod,
  UnsupportedSchemaError,
} from "@/lib/json-schema-form";

import catalog from "../../contracts/pipeline-catalog.json";

const steps = catalog.steps as unknown as {
  id: string;
  params_schema: JsonSchema;
}[];

function schemaOf(id: string): JsonSchema {
  const step = steps.find((s) => s.id === id);
  if (!step) throw new Error(`No step ${id} in the catalog`);
  return step.params_schema;
}

function field(id: string, name: string) {
  const found = describeFields(schemaOf(id)).find((f) => f.name === name);
  if (!found) throw new Error(`No ${name} on ${id}`);
  return found;
}

describe("describeFields", () => {
  it("describes an integer with min and max", () => {
    expect(field("random_forest_regressor", "n_estimators")).toMatchObject({
      kind: "integer",
      nullable: false,
      min: 10,
      max: 1000,
      default: 100,
    });
  });

  it("describes a nullable integer", () => {
    expect(field("random_forest_regressor", "max_depth")).toMatchObject({
      kind: "integer",
      nullable: true,
      min: 1,
      max: 100,
      default: null,
    });
  });

  it("describes an enum", () => {
    expect(field("simple_imputer", "strategy")).toMatchObject({
      kind: "enum",
      options: ["mean", "median", "most_frequent"],
      default: "median",
    });
  });

  it("describes a boolean", () => {
    expect(field("standard_scaler", "with_mean")).toMatchObject({
      kind: "boolean",
      default: true,
    });
  });

  it("describes a number with an exclusive minimum", () => {
    expect(field("lasso", "alpha")).toMatchObject({
      kind: "number",
      exclusiveMin: 0,
      default: 1,
    });
  });

  it("describes an int array with item and length bounds", () => {
    expect(field("mlp_classifier", "hidden_layer_sizes")).toMatchObject({
      kind: "intArray",
      min: 1,
      max: 1000,
      minItems: 1,
      maxItems: 5,
      default: [100],
    });
  });

  it("uses the schema's title and description", () => {
    const f = field("random_forest_regressor", "n_estimators");
    expect(f.label).toBe("N Estimators");
    expect(f.description).toBe("Number of trees.");
  });

  it("resolves refs to $defs", () => {
    const schema: JsonSchema = {
      type: "object",
      properties: { kernel: { $ref: "#/$defs/Kernel", default: "rbf" } },
      $defs: { Kernel: { type: "string", enum: ["linear", "rbf"] } },
    };

    expect(describeFields(schema)[0]).toMatchObject({
      kind: "enum",
      options: ["linear", "rbf"],
      default: "rbf",
    });
  });

  it.each<[string, JsonSchema]>([
    ["a plain string", { type: "string" }],
    ["an object", { type: "object" }],
    [
      "a three-way anyOf",
      { anyOf: [{ type: "integer" }, { type: "string" }, { type: "null" }] },
    ],
    ["an array of strings", { type: "array", items: { type: "string" } }],
    ["a ref outside $defs", { $ref: "https://example.com/schema" }],
  ])("throws for %s", (_, property) => {
    expect(() =>
      describeFields({ type: "object", properties: { p: property } }),
    ).toThrow(UnsupportedSchemaError);
  });
});

describe("paramsZod", () => {
  it("parses form text into typed params", () => {
    const parsed = paramsZod(schemaOf("mlp_classifier")).parse({
      hidden_layer_sizes: "100, 50",
      activation: "tanh",
      alpha: "0.001",
      learning_rate_init: "0.01",
      max_iter: "200",
    });

    expect(parsed).toEqual({
      hidden_layer_sizes: [100, 50],
      activation: "tanh",
      alpha: 0.001,
      learning_rate_init: 0.01,
      max_iter: 200,
    });
  });

  it("turns a blank nullable field into null", () => {
    const parsed = paramsZod(schemaOf("random_forest_regressor")).parse(
      paramsToForm(schemaOf("random_forest_regressor")),
    );

    expect(parsed.max_depth).toBeNull();
    expect(parsed.n_estimators).toBe(100);
  });

  it.each<[string, Record<string, unknown>]>([
    ["below the minimum", { n_estimators: "5" }],
    ["above the maximum", { n_estimators: "5000" }],
    ["not a whole number", { n_estimators: "10.5" }],
    ["not a number", { n_estimators: "many" }],
    ["blank but required", { n_estimators: "" }],
    ["an unknown key", { bogus: 1 }],
  ])("rejects a value %s", (_, params) => {
    const schema = schemaOf("random_forest_regressor");

    expect(
      paramsZod(schema).safeParse({ ...paramsToForm(schema), ...params })
        .success,
    ).toBe(false);
  });

  it("enforces an exclusive minimum", () => {
    const schema = schemaOf("lasso");

    expect(
      paramsZod(schema).safeParse({ ...paramsToForm(schema), alpha: "0" })
        .success,
    ).toBe(false);
    expect(
      paramsZod(schema).safeParse({ ...paramsToForm(schema), alpha: "0.1" })
        .success,
    ).toBe(true);
  });

  it("enforces an int array's item and length bounds", () => {
    const schema = schemaOf("mlp_regressor");
    const check = (sizes: string) =>
      paramsZod(schema).safeParse({
        ...paramsToForm(schema),
        hidden_layer_sizes: sizes,
      }).success;

    expect(check("100")).toBe(true);
    expect(check("0")).toBe(false);
    expect(check("")).toBe(false);
    expect(check("1, 2, 3, 4, 5, 6")).toBe(false);
  });
});

describe("the real catalog", () => {
  it.each(steps.map((step) => [step.id, step.params_schema] as const))(
    "%s renders and its defaults validate",
    (_, schema) => {
      const fields = describeFields(schema);
      expect(fields.length).toBe(Object.keys(schema.properties ?? {}).length);

      expect(paramsZod(schema).parse(paramsToForm(schema))).toEqual(
        defaultParams(schema),
      );
    },
  );
});
