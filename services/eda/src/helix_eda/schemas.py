"""Request and response models for the Helix EDA service."""

from pydantic import BaseModel, Field


class SummaryRequest(BaseModel):
    """Request body for ``POST /summary``."""

    dataset_key: str = Field(description="S3 key of the CSV dataset to summarise.")


class TopValue(BaseModel):
    """One value and its occurrence count, for a non-numeric column."""

    value: str = Field(description="The value, rendered as a string.")
    count: int = Field(description="How many rows hold this value.")


class ColumnSummary(BaseModel):
    """Summary statistics for one column of a dataset."""

    name: str = Field(description="The column's name.")
    dtype: str = Field(description="The pandas dtype, as a string (e.g. 'float64').")
    is_numeric: bool = Field(description="Whether the column holds numeric values.")
    non_null: int = Field(description="Number of non-null values.")
    missing: int = Field(description="Number of missing (null) values.")
    missing_pct: float = Field(description="Percentage of rows missing, 0-100.")
    unique: int = Field(description="Number of distinct non-null values.")
    mean: float | None = Field(description="Mean, or null for non-numeric columns.")
    std: float | None = Field(
        description="Standard deviation, or null for non-numeric columns."
    )
    min: float | None = Field(description="Minimum, or null for non-numeric columns.")
    q25: float | None = Field(
        description="25th percentile, or null for non-numeric columns."
    )
    median: float | None = Field(description="Median, or null for non-numeric columns.")
    q75: float | None = Field(
        description="75th percentile, or null for non-numeric columns."
    )
    max: float | None = Field(description="Maximum, or null for non-numeric columns.")
    top_values: list[TopValue] | None = Field(
        description="Up to 10 most common values, or null for numeric columns."
    )


class DatasetSummary(BaseModel):
    """Summary of a tabular dataset, returned by ``POST /summary``."""

    row_count: int = Field(description="Number of rows in the dataset.")
    column_count: int = Field(description="Number of columns in the dataset.")
    columns: list[ColumnSummary] = Field(
        description="Per-column summaries, in the dataset's column order."
    )
