# Contracts

The API (C#) and the Python workers exchange job messages over RabbitMQ. Both sides need to agree on the exact
shape of those messages, but the two languages can't share a type definition directly. `helix_core.messages`
(pydantic v2 models) is the source of truth; `helix_core.contracts` exports each model as a JSON Schema file under
`contracts/messages/`, and the API's tests validate the messages it sends and parses against those schema files
(#24). `contracts/model-metadata.schema.json`, generated the same way from `helix_core.model_metadata`, describes the
`metadata.json` written next to every trained model ([pipelines.md](pipelines.md#model-metadata)). This way a change to a message shape fails CI on whichever side doesn't update first, instead of failing at
runtime in a shared environment.

## Regenerating

```bash
cd services && uv run python -m helix_core.contracts export   # writes contracts/messages/*.schema.json and model-metadata
cd services && uv run python -m helix_core.contracts check    # exits 1 if the files are stale
```

Run `export` after changing anything in `helix_core.messages` or `helix_core.model_metadata` and commit the result. `check` is what CI runs to
catch a forgotten regeneration.

## Messages

Every message has `"schema_version": 1`. Field names are snake_case on the wire. UUID fields serialise as strings
(`format: "uuid"` in the schema).

### TrainingJob

Published by the API to `training.jobs`.

```json
{
  "schema_version": 1,
  "job_id": "1b4e28ba-2fa1-11d2-883f-0016d3cca427",
  "run_id": "…", "experiment_id": "…",
  "dataset_key": "experiments/{e}/datasets/{d}/data.csv",
  "output_prefix": "experiments/{e}/runs/{r}/",
  "pipeline": { "…": "PipelineSpec, below" }
}
```

### PipelineSpec

Embedded in `TrainingJob`. `step` is a free string here; the pipeline catalog that validates it against a registry
is #22.

```json
{
  "problem_type": "classification",
  "target_column": "label",
  "feature_columns": ["a", "b", "c"],
  "id_column": null,
  "split": { "test_size": 0.2, "random_seed": 42, "cv_folds": 5 },
  "steps": [
    { "step": "simple_imputer", "params": { "strategy": "median" } },
    { "step": "standard_scaler", "params": {} }
  ],
  "model": { "step": "random_forest_classifier", "params": { "n_estimators": 200 } },
  "search": { "method": "grid", "cv_folds": 5, "n_iter": null, "param_grid": null }
}
```

### EvaluationJob

Published by the API to `evaluation.jobs`.

```json
{
  "schema_version": 1,
  "job_id": "…", "evaluation_id": "…", "run_id": "…", "experiment_id": "…",
  "dataset_key": "experiments/{e}/datasets/{d}/data.csv",
  "model_prefix": "experiments/{e}/runs/{r}/",
  "output_prefix": "experiments/{e}/evaluations/{v}/",
  "options": {
    "plots": true,
    "global_methods": ["permutation", "shap"],
    "permutation_repeats": 10,
    "local_methods": ["lime", "shap"],
    "local_max_samples": 50,
    "ensemble_methods": ["mean", "majority_vote"],
    "num_features_to_plot": 10,
    "fuzzy": null,
    "plot_options": null
  }
}
```

`fuzzy`, when not null, requires at least one local method and one ensemble method:

```json
{
  "number_fuzzy_features": 10,
  "granular_features": true,
  "number_clusters": 5,
  "cluster_names": ["very low", "low", "medium", "high", "very high"],
  "number_rules": 5
}
```

### JobEvent

Published by workers to `jobs.events`.

```json
{
  "schema_version": 1,
  "job_id": "…",
  "job_type": "training",
  "entity_id": "…run_id or evaluation_id…",
  "status": "progress",
  "progress": 0.4,
  "message": "Cross-validating (fold 2 of 5)",
  "result": null,
  "error": null,
  "timestamp": "2026-11-18T12:00:00Z"
}
```

- `status`: `started` | `progress` | `succeeded` | `failed`. `progress` is `0.0`–`1.0` or null.
- `error` (only on `failed`): `{"type": "InvalidDataError", "message": "…"}`.
- `result` (only on `succeeded`):
  - training: `{"model_key", "metadata_key", "split_key", "cv_metrics_key" (or null), "cv_metrics" (or null),
    "best_params" (or null), "train_rows", "test_rows"}`
  - evaluation: `{"metrics": {"name": number}, "artifacts": [{"key", "kind": "plot" | "table" | "json", "title"}]}`

## Ack and reject rules

Workers use prefetch 1 and handle messages one at a time (see `helix_core.worker.Worker`):

1. If the message body doesn't parse against the job's pydantic model, the worker logs it and rejects it without
   requeue (dead-lettered) — no event is published, since there's no trusted id to attach one to.
2. Otherwise it publishes a `started` event, then runs the job.
3. On success, it publishes `succeeded` with the result, then acks.
4. On a `HelixError` (an expected, user-facing failure such as bad data or an invalid spec), it publishes `failed`
   with the error, then acks.
5. On any other exception, it publishes `failed`, logs the traceback, then rejects without requeue
   (dead-lettered).
6. If asked to stop (SIGTERM/SIGINT) while a job is running, it lets the current job finish within a grace period;
   otherwise it nacks with requeue and exits. It stops consuming new messages immediately.

## Validated by the API

The C# API's tests (#24) load these schema files and validate the messages it builds and the events it receives
against them, so a mismatch between `helix_core.messages` and the API's own message types fails a test instead of
surfacing as a runtime error against a shared RabbitMQ instance.
