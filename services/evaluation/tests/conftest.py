from uuid import uuid4

import boto3
import pandas as pd
import pytest
from moto import mock_aws
from sklearn.datasets import make_classification, make_regression

from helix_core.messages import EvaluationJob, PipelineSpec, TrainingJob
from helix_core.storage import Storage, join_key
from helix_training.train import train

BUCKET = "helix"
FEATURES = ["f0", "f1", "f2", "f3"]
N_ROWS = 80


@pytest.fixture
def s3_storage():
    with mock_aws():
        client = boto3.client("s3", region_name="us-east-1")
        client.create_bucket(Bucket=BUCKET)
        yield Storage(client, BUCKET)


def _classification_df() -> pd.DataFrame:
    x, y = make_classification(
        n_samples=N_ROWS,
        n_features=len(FEATURES),
        n_informative=3,
        n_redundant=0,
        n_classes=2,
        random_state=42,
    )
    df = pd.DataFrame(x, columns=FEATURES)
    df["target"] = [("no", "yes")[label] for label in y]
    return df


def _regression_df() -> pd.DataFrame:
    x, y = make_regression(
        n_samples=N_ROWS, n_features=len(FEATURES), noise=0.5, random_state=42
    )
    df = pd.DataFrame(x, columns=FEATURES)
    df["target"] = y
    return df


def _train_and_upload(
    storage: Storage, problem_type: str, df: pd.DataFrame, model_step: str
):
    dataset_key = "experiments/e/datasets/d/data.csv"
    model_prefix = "experiments/e/runs/r/"
    training_job = TrainingJob(
        job_id=uuid4(),
        run_id=uuid4(),
        experiment_id=uuid4(),
        dataset_key=dataset_key,
        output_prefix=model_prefix,
        pipeline=PipelineSpec.model_validate(
            {
                "problem_type": problem_type,
                "target_column": "target",
                "feature_columns": FEATURES,
                "steps": [{"step": "standard_scaler"}],
                "model": {"step": model_step},
            }
        ),
    )
    output = train(training_job, df, n_jobs=1, version="test", progress=lambda *_: None)

    storage.put_bytes(dataset_key, df.to_csv(index=False).encode("utf-8"), "text/csv")
    storage.put_bytes(
        join_key(model_prefix, "model.onnx"),
        output.onnx_bytes,
        "application/octet-stream",
    )
    storage.put_json(
        join_key(model_prefix, "metadata.json"), output.metadata.model_dump(mode="json")
    )
    storage.put_json(
        join_key(model_prefix, "split.json"),
        {"train_index": output.train_index, "test_index": output.test_index},
    )

    evaluation_job = EvaluationJob(
        job_id=uuid4(),
        evaluation_id=uuid4(),
        run_id=training_job.run_id,
        experiment_id=training_job.experiment_id,
        dataset_key=dataset_key,
        model_prefix=model_prefix,
        output_prefix="experiments/e/evaluations/v/",
    )
    return evaluation_job, output


@pytest.fixture
def classification_env(s3_storage: Storage):
    df = _classification_df()
    job, output = _train_and_upload(
        s3_storage, "classification", df, "logistic_regression"
    )
    return s3_storage, job, df, output


@pytest.fixture
def regression_env(s3_storage: Storage):
    df = _regression_df()
    job, output = _train_and_upload(s3_storage, "regression", df, "linear_regression")
    return s3_storage, job, df, output
