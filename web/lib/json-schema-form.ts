import { z } from "zod";

/**
 * The subset of JSON Schema that pydantic emits for pipeline step params (`contracts/pipeline-catalog.json`).
 * Anything outside it is rejected with {@link UnsupportedSchemaError}.
 */
export interface JsonSchema {
  type?: string;
  enum?: (string | number)[];
  anyOf?: JsonSchema[];
  items?: JsonSchema;
  properties?: Record<string, JsonSchema>;
  additionalProperties?: boolean;
  $ref?: string;
  $defs?: Record<string, JsonSchema>;
  default?: unknown;
  title?: string;
  description?: string;
  minimum?: number;
  maximum?: number;
  exclusiveMinimum?: number;
  exclusiveMaximum?: number;
  minItems?: number;
  maxItems?: number;
}

/** A schema the form generator can't render: a catalog change the UI needs to learn about. */
export class UnsupportedSchemaError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "UnsupportedSchemaError";
  }
}

/** How a param is edited. */
export type FieldKind = "integer" | "number" | "boolean" | "enum" | "intArray";

/** One param of a step, ready to render as a form field. */
export interface FieldDescriptor {
  /** The param's key, exactly as in the schema (snake_case). */
  name: string;
  label: string;
  description?: string;
  kind: FieldKind;
  /** Whether null is allowed (`anyOf: [X, {type: null}]`). An empty input means null. */
  nullable: boolean;
  options?: (string | number)[];
  /** Bounds on the value, or on each item for `intArray`. */
  min?: number;
  max?: number;
  exclusiveMin?: number;
  exclusiveMax?: number;
  /** Bounds on an `intArray`'s length. */
  minItems?: number;
  maxItems?: number;
  default: unknown;
}

function resolve(schema: JsonSchema, root: JsonSchema): JsonSchema {
  if (!schema.$ref) return schema;
  const match = /^#\/\$defs\/(.+)$/.exec(schema.$ref);
  const target = match ? root.$defs?.[match[1]] : undefined;
  if (!target) {
    throw new UnsupportedSchemaError(
      `Can't resolve ${schema.$ref}: only #/$defs/… refs are supported.`,
    );
  }
  // Keywords beside the $ref (default, title, description) win over the target's.
  const { $ref: _ref, ...rest } = schema;
  return { ...resolve(target, root), ...rest };
}

function humanise(name: string): string {
  const words = name.replace(/_/g, " ");
  return words.charAt(0).toUpperCase() + words.slice(1);
}

function describeField(
  name: string,
  property: JsonSchema,
  root: JsonSchema,
): FieldDescriptor {
  let schema = resolve(property, root);
  let nullable = false;

  if (schema.anyOf) {
    const branches = schema.anyOf.map((branch) => resolve(branch, root));
    const nonNull = branches.filter((branch) => branch.type !== "null");
    if (branches.length !== 2 || nonNull.length !== 1) {
      throw new UnsupportedSchemaError(
        `${name}: only anyOf [X, {"type": "null"}] is supported.`,
      );
    }
    nullable = true;
    const { anyOf: _anyOf, ...outer } = schema;
    schema = { ...nonNull[0], ...outer };
  }

  const base = {
    name,
    label: schema.title ?? humanise(name),
    description: schema.description,
    nullable,
    default: schema.default ?? (nullable ? null : undefined),
  };
  const bounds = {
    min: schema.minimum,
    max: schema.maximum,
    exclusiveMin: schema.exclusiveMinimum,
    exclusiveMax: schema.exclusiveMaximum,
  };

  if (schema.enum) {
    return { ...base, kind: "enum", options: schema.enum };
  }

  switch (schema.type) {
    case "integer":
      return { ...base, kind: "integer", ...bounds };
    case "number":
      return { ...base, kind: "number", ...bounds };
    case "boolean":
      return { ...base, kind: "boolean" };
    case "array": {
      const items = schema.items ? resolve(schema.items, root) : undefined;
      if (items?.type !== "integer" || items.enum || items.anyOf) {
        throw new UnsupportedSchemaError(
          `${name}: only arrays of integers are supported.`,
        );
      }
      return {
        ...base,
        kind: "intArray",
        min: items.minimum,
        max: items.maximum,
        exclusiveMin: items.exclusiveMinimum,
        exclusiveMax: items.exclusiveMaximum,
        minItems: schema.minItems,
        maxItems: schema.maxItems,
      };
    }
    default:
      throw new UnsupportedSchemaError(
        `${name}: type ${JSON.stringify(schema.type)} isn't supported.`,
      );
  }
}

/**
 * Describes each param of a step's `params_schema`, in the schema's order.
 * @param schema - the step's `params_schema`
 * @returns one descriptor per property
 * @throws UnsupportedSchemaError for a schema outside the supported subset
 */
export function describeFields(schema: JsonSchema): FieldDescriptor[] {
  if (schema.type !== undefined && schema.type !== "object") {
    throw new UnsupportedSchemaError("A params schema must be an object.");
  }
  return Object.entries(schema.properties ?? {}).map(([name, property]) =>
    describeField(name, property, schema),
  );
}

/**
 * The params a step gets when it's added: every param's default.
 * @param schema - the step's `params_schema`
 * @returns param name → default value
 */
export function defaultParams(schema: JsonSchema): Record<string, unknown> {
  return Object.fromEntries(
    describeFields(schema).map((field) => [field.name, field.default]),
  );
}

function boundNumber(field: FieldDescriptor, number: z.ZodNumber): z.ZodNumber {
  let result = number;
  if (field.min !== undefined) result = result.min(field.min);
  if (field.max !== undefined) result = result.max(field.max);
  if (field.exclusiveMin !== undefined) result = result.gt(field.exclusiveMin);
  if (field.exclusiveMax !== undefined) result = result.lt(field.exclusiveMax);
  return result;
}

function isBlank(value: unknown): boolean {
  return (
    value === undefined ||
    value === null ||
    (typeof value === "string" && value.trim() === "")
  );
}

/** Parses an input's text as a number; leaves anything else for zod to reject. */
function toNumber(value: unknown): unknown {
  if (typeof value !== "string") return value;
  const number = Number(value.trim());
  return Number.isNaN(number) ? value : number;
}

/** Parses `"100, 50"` into `[100, 50]`; leaves arrays as they are. */
function toIntArray(value: unknown): unknown {
  if (typeof value !== "string") return value;
  return value
    .split(",")
    .map((part) => part.trim())
    .filter((part) => part !== "")
    .map(toNumber);
}

/**
 * The zod schema for one param's value, accepting what a form holds: a number input's text, an int array as
 * `"100, 50"`, and a blank input as null for a nullable param.
 * @param field - the param
 * @returns a schema whose output is the param's typed value
 */
export function fieldZod(field: FieldDescriptor): z.ZodType {
  let value: z.ZodType;
  switch (field.kind) {
    case "integer":
    case "number": {
      const number = boundNumber(field, z.number({ error: "Enter a number." }));
      value = z.preprocess(
        toNumber,
        field.kind === "integer"
          ? number.int({ error: "Enter a whole number." })
          : number,
      );
      break;
    }
    case "boolean":
      value = z.boolean();
      break;
    case "enum":
      value = z.union(
        (field.options ?? []).map((option) => z.literal(option)) as [
          z.ZodLiteral,
          z.ZodLiteral,
          ...z.ZodLiteral[],
        ],
        { error: `Choose one of: ${(field.options ?? []).join(", ")}.` },
      );
      break;
    case "intArray": {
      let list = z.array(
        boundNumber(field, z.number({ error: "Enter whole numbers." })).int({
          error: "Enter whole numbers.",
        }),
      );
      if (field.minItems !== undefined) list = list.min(field.minItems);
      if (field.maxItems !== undefined) list = list.max(field.maxItems);
      value = z.preprocess(toIntArray, list);
      break;
    }
  }

  if (field.nullable) {
    return z.preprocess((v) => (isBlank(v) ? null : v), value.nullable());
  }
  return z.preprocess((v) => (isBlank(v) ? undefined : v), value);
}

/**
 * The zod schema for a step's params, as a form holds them; its output is the snake_case params object to send.
 * Unknown keys are rejected, as the API's schema does (`additionalProperties: false`).
 * @param schema - the step's `params_schema`
 * @returns the params schema
 */
export function paramsZod(schema: JsonSchema) {
  return z
    .object(
      Object.fromEntries(
        describeFields(schema).map((field) => [field.name, fieldZod(field)]),
      ),
    )
    .strict();
}

/**
 * Turns a param value into what its form field holds: numbers as text, an int array as `"100, 50"`, null as `""`.
 * @param field - the param
 * @param value - the value, e.g. from a saved spec or the default
 * @returns the form value
 */
export function toFormValue(field: FieldDescriptor, value: unknown): unknown {
  if (value === null || value === undefined) {
    return field.kind === "boolean" ? Boolean(field.default) : "";
  }
  if (field.kind === "intArray" && Array.isArray(value))
    return value.join(", ");
  if (field.kind === "integer" || field.kind === "number") return String(value);
  return value;
}

/**
 * A step's params as its form holds them, filling in defaults for anything missing.
 * @param schema - the step's `params_schema`
 * @param params - the saved params, if any
 * @returns param name → form value
 */
export function paramsToForm(
  schema: JsonSchema,
  params: Record<string, unknown> = {},
): Record<string, unknown> {
  return Object.fromEntries(
    describeFields(schema).map((field) => [
      field.name,
      toFormValue(
        field,
        field.name in params ? params[field.name] : field.default,
      ),
    ]),
  );
}
