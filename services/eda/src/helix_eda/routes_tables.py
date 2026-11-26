"""Routes for the EDA service's table endpoints.

Mirrors :mod:`helix_eda.routes_plots`: each endpoint loads the dataset, computes a
table with a pure function from :mod:`helix_eda.stats`, uploads it to S3 as a CSV and
returns its key, metadata and rows.
"""

from typing import Annotated

import pandas as pd
from fastapi import APIRouter, Depends

from helix_core.datasets import load_csv
from helix_core.storage import Storage
from helix_eda.dependencies import get_storage, require_service_token
from helix_eda.schemas import NormalityRequest, NormalityResponse, NormalityRow
from helix_eda.stats import normality_table

router = APIRouter(prefix="/tables", dependencies=[Depends(require_service_token)])


@router.post("/normality", response_model=NormalityResponse)
def post_normality(
    body: NormalityRequest, storage: Annotated[Storage, Depends(get_storage)]
) -> NormalityResponse:
    """Run normality tests on a dataset's numeric columns and upload the result as CSV.

    Args:
        body: The request body, naming the dataset, columns and output key.
        storage: The storage client to read the dataset from and write the CSV to.

    Returns:
        The uploaded CSV's key, content type, size, title and rows.
    """
    df = load_csv(storage, body.dataset_key)
    rows = normality_table(df, body.columns)
    csv_bytes = _rows_to_csv(rows)
    storage.put_bytes(body.output_key, csv_bytes, "text/csv")
    return NormalityResponse(
        key=body.output_key,
        size_bytes=len(csv_bytes),
        title="Normality tests",
        rows=rows,
    )


def _rows_to_csv(rows: list[NormalityRow]) -> bytes:
    """Render normality test rows as CSV bytes.

    Args:
        rows: The rows to render, in order.

    Returns:
        UTF-8 encoded CSV bytes, with a header row.
    """
    frame = pd.DataFrame([row.model_dump() for row in rows])
    return frame.to_csv(index=False).encode("utf-8")
