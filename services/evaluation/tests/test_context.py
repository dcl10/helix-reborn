import pytest

from helix_core.errors import InvalidDataError
from helix_evaluation.context import load_context


def test_load_context_selects_rows_by_split_positions(classification_env) -> None:
    storage, job, df, output = classification_env

    ctx = load_context(job, storage, n_jobs=1)

    assert len(ctx.x_train) == len(output.train_index)
    assert len(ctx.x_test) == len(output.test_index)
    expected_first_row = df.iloc[output.train_index[0]][["f0", "f1", "f2", "f3"]]
    assert list(ctx.x_train.iloc[0]) == pytest.approx(list(expected_first_row))


def test_load_context_maps_string_classes_to_indices(classification_env) -> None:
    storage, job, df, output = classification_env

    ctx = load_context(job, storage, n_jobs=1)

    classes = output.metadata.classes
    expected = (
        df.iloc[output.train_index]["target"]
        .map({label: index for index, label in enumerate(classes)})
        .to_numpy()
    )
    assert list(ctx.y_train) == list(expected)


def test_load_context_raises_on_unknown_label(classification_env) -> None:
    storage, job, df, output = classification_env
    df = df.copy()
    df.loc[output.train_index[0], "target"] = "maybe"
    storage.put_bytes(
        job.dataset_key, df.to_csv(index=False).encode("utf-8"), "text/csv"
    )

    with pytest.raises(InvalidDataError):
        load_context(job, storage, n_jobs=1)


def test_load_context_regression_keeps_numeric_target(regression_env) -> None:
    storage, job, df, output = regression_env

    ctx = load_context(job, storage, n_jobs=1)

    expected = df.iloc[output.train_index]["target"].to_numpy()
    assert list(ctx.y_train) == pytest.approx(list(expected))
